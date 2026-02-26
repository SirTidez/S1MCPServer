"""Unit tests for TcpClient."""

import asyncio
import socket
import struct
import json
import threading
from unittest.mock import MagicMock, patch, call

import pytest

from src.tcp_client import TcpClient, TcpConnectionError
from src.models.response import Response, ErrorResponse


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def _encode_response(data: dict) -> bytes:
    """Build a length-prefixed JSON response as the server would send."""
    payload = json.dumps(data).encode("utf-8")
    return struct.pack("<I", len(payload)) + payload


# ---------------------------------------------------------------------------
# Initial state
# ---------------------------------------------------------------------------

def test_initial_state():
    client = TcpClient()
    assert not client.is_connected()
    assert client.reconnect_delay == 0.25
    assert client.host == "localhost"
    assert client.port == 8765


def test_reconnect_delay_default_is_quarter_second():
    c = TcpClient()
    assert c.reconnect_delay == 0.25


# ---------------------------------------------------------------------------
# connect / disconnect
# ---------------------------------------------------------------------------

def test_connect_raises_on_refused():
    """Connecting to a port where nothing is listening raises TcpConnectionError."""
    client = TcpClient(port=19999, timeout=0.5)
    with pytest.raises(TcpConnectionError):
        client.connect()


def test_disconnect_when_not_connected_is_safe():
    client = TcpClient()
    client.disconnect()  # must not raise
    assert not client.is_connected()


def test_double_connect_is_idempotent():
    """Calling connect() twice without disconnecting should not open a second socket."""
    client = TcpClient()

    mock_sock = MagicMock()
    mock_sock.recv.return_value = b""
    mock_sock.connect.return_value = None
    mock_sock.setsockopt.return_value = None
    mock_sock.settimeout.return_value = None

    with patch("src.tcp_client.socket.socket", return_value=mock_sock):
        client._connected = True  # pretend already connected
        client._socket = mock_sock
        client.connect()  # second call — should short-circuit

    # socket.socket() should NOT have been called again
    assert client._socket is mock_sock


# ---------------------------------------------------------------------------
# async_call wraps call_with_retry
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_async_call_delegates_to_call_with_retry():
    client = TcpClient()
    expected = Response(id=1, result={"ok": True}, error=None)

    with patch.object(client, "call_with_retry", return_value=expected) as mock_cwr:
        result = await client.async_call("get_player", {"x": 1})

    mock_cwr.assert_called_once_with("get_player", {"x": 1})
    assert result is expected


@pytest.mark.asyncio
async def test_async_call_runs_in_executor():
    """async_call should NOT block the event loop — verify it runs in a thread pool."""
    client = TcpClient()
    called_thread_id = None
    main_thread_id = threading.current_thread().ident

    def fake_cwr(method, params=None, max_retries=3):
        nonlocal called_thread_id
        called_thread_id = threading.current_thread().ident
        return Response(id=1, result={}, error=None)

    with patch.object(client, "call_with_retry", side_effect=fake_cwr):
        await client.async_call("test")

    # The actual work must have happened on a different thread
    assert called_thread_id is not None
    assert called_thread_id != main_thread_id


# ---------------------------------------------------------------------------
# call_with_retry retry logic
# ---------------------------------------------------------------------------

def test_call_with_retry_succeeds_on_first_attempt():
    client = TcpClient()
    expected = Response(id=1, result={"ok": True}, error=None)

    with patch.object(client, "call", return_value=expected) as mock_call:
        result = client.call_with_retry("ping")

    mock_call.assert_called_once_with("ping", None)
    assert result is expected


def test_call_with_retry_retries_on_connection_error():
    client = TcpClient(reconnect_delay=0.0)
    expected = Response(id=1, result={}, error=None)
    attempts = []

    def flaky_call(method, params=None):
        attempts.append(1)
        if len(attempts) < 2:
            raise TcpConnectionError("transient")
        return expected

    with patch.object(client, "call", side_effect=flaky_call):
        with patch.object(client, "connect"):
            with patch.object(client, "disconnect"):
                result = client.call_with_retry("ping", max_retries=3)

    assert len(attempts) == 2
    assert result is expected


def test_call_with_retry_raises_after_all_attempts():
    client = TcpClient(reconnect_delay=0.0)

    with patch.object(client, "call", side_effect=TcpConnectionError("dead")):
        with patch.object(client, "connect", side_effect=TcpConnectionError("still dead")):
            with patch.object(client, "disconnect"):
                with pytest.raises(TcpConnectionError):
                    client.call_with_retry("ping", max_retries=2)


def test_call_with_retry_respects_max_retries():
    client = TcpClient(reconnect_delay=0.0)
    call_count = [0]

    def always_fail(method, params=None):
        call_count[0] += 1
        raise TcpConnectionError("fail")

    with patch.object(client, "call", side_effect=always_fail):
        with patch.object(client, "connect", side_effect=TcpConnectionError("no server")):
            with patch.object(client, "disconnect"):
                with pytest.raises(TcpConnectionError):
                    client.call_with_retry("ping", max_retries=3)

    assert call_count[0] == 3


# ---------------------------------------------------------------------------
# Protocol layer: _read_message / _write_message
# ---------------------------------------------------------------------------

def test_read_message_reads_length_prefixed_data():
    client = TcpClient()
    client._connected = True

    payload = b'{"id":1,"result":{},"error":null}'
    length_prefix = struct.pack("<I", len(payload))
    full_message = length_prefix + payload

    mock_sock = MagicMock()
    # recv: first call returns 4-byte prefix, second call returns payload
    mock_sock.recv.side_effect = [length_prefix, payload]
    client._socket = mock_sock

    data = client._read_message()
    assert data == full_message


def test_read_message_raises_on_short_prefix():
    client = TcpClient()
    client._connected = True
    mock_sock = MagicMock()
    mock_sock.recv.return_value = b"\x00\x00"  # only 2 bytes
    client._socket = mock_sock

    with pytest.raises(TcpConnectionError, match="message length"):
        client._read_message()


def test_write_message_sends_all_bytes():
    client = TcpClient()
    client._connected = True
    mock_sock = MagicMock()
    mock_sock.send.side_effect = lambda data: len(data)
    client._socket = mock_sock

    data = b"hello world"
    client._write_message(data)
    mock_sock.send.assert_called()
