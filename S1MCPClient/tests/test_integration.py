"""Integration tests: real socket round-trip between TcpClient and a mock server."""

import json
import socket
import struct
import threading
import time

import pytest

from src.tcp_client import TcpClient, TcpConnectionError
from src.protocol import deserialize_response


# ---------------------------------------------------------------------------
# Minimal mock TCP server
# ---------------------------------------------------------------------------

class MockTcpServer:
    """
    Minimal in-process TCP server that speaks the same length-prefixed JSON-RPC
    protocol as the C# mod.  Responds to any method with a configurable result.
    """

    def __init__(self, port: int = 0):
        self._server_sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self._server_sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self._server_sock.bind(("127.0.0.1", port))
        self._server_sock.listen(1)
        self._server_sock.settimeout(2.0)
        self.port: int = self._server_sock.getsockname()[1]
        self._thread: threading.Thread | None = None
        self._stop = threading.Event()
        # Handlers: method -> result dict (or callable(method, params) -> result)
        self.handlers: dict = {}
        self.requests_received: list = []

    # ------------------------------------------------------------------
    # Protocol helpers
    # ------------------------------------------------------------------

    @staticmethod
    def _recv_exact(conn: socket.socket, n: int) -> bytes:
        buf = b""
        while len(buf) < n:
            chunk = conn.recv(n - len(buf))
            if not chunk:
                raise ConnectionError("Client closed connection")
            buf += chunk
        return buf

    @staticmethod
    def _read_request(conn: socket.socket) -> dict:
        length_bytes = MockTcpServer._recv_exact(conn, 4)
        length = struct.unpack("<I", length_bytes)[0]
        payload = MockTcpServer._recv_exact(conn, length)
        return json.loads(payload.decode("utf-8"))

    @staticmethod
    def _send_response(conn: socket.socket, response: dict) -> None:
        payload = json.dumps(response).encode("utf-8")
        conn.sendall(struct.pack("<I", len(payload)) + payload)

    # ------------------------------------------------------------------
    # Server loop
    # ------------------------------------------------------------------

    def _serve(self) -> None:
        try:
            conn, _ = self._server_sock.accept()
        except (socket.timeout, OSError):
            return

        conn.settimeout(2.0)
        try:
            while not self._stop.is_set():
                try:
                    req = self._read_request(conn)
                except (ConnectionError, OSError, socket.timeout):
                    break

                self.requests_received.append(req)
                method = req.get("method", "")
                req_id = req.get("id", 0)

                if method in self.handlers:
                    handler = self.handlers[method]
                    if callable(handler):
                        result = handler(method, req.get("params", {}))
                    else:
                        result = handler
                    self._send_response(conn, {"id": req_id, "result": result, "error": None})
                else:
                    self._send_response(conn, {
                        "id": req_id,
                        "result": None,
                        "error": {"code": -32601, "message": f"Method not found: {method}"}
                    })
        finally:
            conn.close()

    def start(self) -> None:
        self._thread = threading.Thread(target=self._serve, daemon=True)
        self._thread.start()

    def stop(self) -> None:
        self._stop.set()
        self._server_sock.close()
        if self._thread:
            self._thread.join(timeout=2.0)


# ---------------------------------------------------------------------------
# Fixture
# ---------------------------------------------------------------------------

@pytest.fixture
def mock_server():
    server = MockTcpServer()
    server.start()
    yield server
    server.stop()


def _make_client(port: int) -> TcpClient:
    return TcpClient(host="127.0.0.1", port=port, timeout=2.0, reconnect_delay=0.0)


# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------

def test_basic_round_trip(mock_server):
    """Client sends a request; server echoes a result; client gets it back."""
    mock_server.handlers["ping"] = {"pong": True}

    client = _make_client(mock_server.port)
    client.connect()
    try:
        response = client.call("ping", {})
        assert response.error is None
        assert response.result == {"pong": True}
    finally:
        client.disconnect()


def test_request_id_roundtrip(mock_server):
    """Response ID must match the request ID the client sent."""
    mock_server.handlers["echo"] = {"ok": True}

    client = _make_client(mock_server.port)
    client.connect()
    try:
        response = client.call("echo", {})
        assert response.id == client._request_id_counter  # last used ID
    finally:
        client.disconnect()


def test_server_error_response_parsed(mock_server):
    """An error response from the server is correctly parsed into response.error."""
    # No handler registered → server returns Method not found
    client = _make_client(mock_server.port)
    client.connect()
    try:
        response = client.call("nonexistent_method", {})
        assert response.error is not None
        assert response.error.code == -32601
    finally:
        client.disconnect()


def test_params_forwarded_to_server(mock_server):
    """Params sent by the client reach the server intact."""
    received_params = {}

    def capture(method, params):
        received_params.update(params)
        return {"ok": True}

    mock_server.handlers["set_health"] = capture

    client = _make_client(mock_server.port)
    client.connect()
    try:
        client.call("set_health", {"npc_id": "kyle", "health": 50.0})
        assert received_params["npc_id"] == "kyle"
        assert received_params["health"] == 50.0
    finally:
        client.disconnect()


def test_multiple_sequential_calls(mock_server):
    """Multiple sequential calls on the same connection all succeed."""
    mock_server.handlers["a"] = {"r": "a"}
    mock_server.handlers["b"] = {"r": "b"}
    mock_server.handlers["c"] = {"r": "c"}

    # Need the server to handle multiple requests — replace with a loop server
    results = []

    class LoopMockServer(MockTcpServer):
        def _serve(self):
            try:
                conn, _ = self._server_sock.accept()
            except socket.timeout:
                return
            conn.settimeout(2.0)
            try:
                while not self._stop.is_set():
                    try:
                        req = self._read_request(conn)
                    except (ConnectionError, OSError, socket.timeout):
                        break
                    method = req.get("method", "")
                    req_id = req.get("id", 0)
                    result = self.handlers.get(method, {"fallback": True})
                    if callable(result):
                        result = result(method, req.get("params", {}))
                    self._send_response(conn, {"id": req_id, "result": result, "error": None})
            finally:
                conn.close()

    server = LoopMockServer()
    server.handlers = {"a": {"r": "a"}, "b": {"r": "b"}, "c": {"r": "c"}}
    server.start()

    client = _make_client(server.port)
    client.connect()
    try:
        for method in ("a", "b", "c"):
            resp = client.call(method, {})
            results.append(resp.result["r"])
    finally:
        client.disconnect()
        server.stop()

    assert results == ["a", "b", "c"]


def test_connect_to_closed_port_raises():
    """Connecting to a port with no listener raises TcpConnectionError."""
    client = TcpClient(host="127.0.0.1", port=19998, timeout=0.3, reconnect_delay=0.0)
    with pytest.raises(TcpConnectionError):
        client.connect()


@pytest.mark.asyncio
async def test_async_call_round_trip(mock_server):
    """async_call works end-to-end through a real socket."""
    mock_server.handlers["get_player"] = {"health": 100, "money": 500}

    client = _make_client(mock_server.port)
    client.connect()
    try:
        response = await client.async_call("get_player", {})
        assert response.error is None
        assert response.result["health"] == 100
    finally:
        client.disconnect()
