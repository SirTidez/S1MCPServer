"""Optional live integration tests against a real Schedule I install and running mod."""

from __future__ import annotations

import os
from pathlib import Path

import pytest

from src.tcp_client import TcpClient
from src.tools.game_lifecycle_tools import _detect_game_version, _is_game_running


LIVE_TESTS_ENABLED = os.getenv("S1_LIVE_TESTS") == "1"
LIVE_GAME_DIR = Path(
    os.getenv("S1_LIVE_GAME_DIR", r"D:\SteamLibrary\steamapps\common\Schedule I_public")
)
LIVE_HOST = os.getenv("S1_LIVE_HOST", "127.0.0.1")
LIVE_PORT = int(os.getenv("S1_LIVE_PORT", "8765"))


def _require_live_tests() -> None:
    """Skip the current test unless explicit live-test opt-in is enabled."""

    if not LIVE_TESTS_ENABLED:
        pytest.skip("Set S1_LIVE_TESTS=1 to run live Schedule I integration tests")


@pytest.mark.live_game
def test_live_game_installation_path_and_version_detection() -> None:
    """Validate that the configured live install path exists and has a detectable runtime."""

    _require_live_tests()

    assert LIVE_GAME_DIR.exists(), f"Game directory does not exist: {LIVE_GAME_DIR}"

    game_exe = LIVE_GAME_DIR / "Schedule I.exe"
    assert game_exe.exists(), f"Game executable does not exist: {game_exe}"

    detected = _detect_game_version(str(LIVE_GAME_DIR))
    assert detected in {"mono", "il2cpp"}


@pytest.mark.live_game
def test_live_mod_handshake_when_game_running() -> None:
    """Perform a real handshake against the running game mod over TCP."""

    _require_live_tests()

    if not _is_game_running():
        pytest.skip(
            "Schedule I is not running; start game with the S1MCPServer mod first"
        )

    client = TcpClient(host=LIVE_HOST, port=LIVE_PORT, timeout=5.0, reconnect_delay=0.5)
    client.connect()
    try:
        response = client.call("handshake", {})
        assert response.error is None, f"Handshake failed: {response.error}"
        assert isinstance(response.result, dict), (
            "Handshake result must be a JSON object"
        )
        assert "available_methods" in response.result, (
            "Handshake missing available_methods"
        )
    finally:
        client.disconnect()
