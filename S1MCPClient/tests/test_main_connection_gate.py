"""Tests for connection gating in src.main.can_call_tool."""

from types import SimpleNamespace

from src import main


class _FakeTcpClient:
    """Small test double for the parts of ``TcpClient`` used by ``can_call_tool``."""

    def __init__(self, connected: bool, handshake_error=None):
        self._connected = connected
        self._handshake_error = handshake_error
        self.connect_called = 0
        self.call_called = 0

    def is_connected(self) -> bool:
        return self._connected

    def connect(self) -> None:
        self.connect_called += 1
        self._connected = True

    def call(self, method: str, params: dict):
        self.call_called += 1
        return SimpleNamespace(
            error=self._handshake_error,
            result={"instructions": "hello", "available_methods": ["handshake"]},
        )


def test_can_call_tool_allows_lifecycle_when_disconnected():
    """Lifecycle tool calls must bypass connection gating."""

    main.is_connected = False
    main.tcp_client = None

    allowed, message = main.can_call_tool("s1_game")

    assert allowed is True
    assert message == ""


def test_can_call_tool_lazy_reconnects_and_allows_non_lifecycle_tool():
    """A successful lazy reconnect should allow normal tool calls."""

    main.is_connected = False
    main.server_instructions = None
    main.tcp_client = _FakeTcpClient(connected=False, handshake_error=None)

    allowed, message = main.can_call_tool("s1_player")

    assert allowed is True
    assert message == ""
    assert main.is_connected is True
    assert main.server_instructions == "hello"
    assert main.tcp_client.connect_called == 1
    assert main.tcp_client.call_called == 1


def test_can_call_tool_denies_when_lazy_reconnect_fails():
    """Reconnect failures should return a user-facing not-connected error."""

    main.is_connected = False
    main.server_instructions = None
    main.tcp_client = _FakeTcpClient(
        connected=False,
        handshake_error=SimpleNamespace(code=-1, message="nope"),
    )

    allowed, message = main.can_call_tool("s1_player")

    assert allowed is False
    assert "not connected" in message.lower()
