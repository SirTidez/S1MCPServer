"""Unit tests for the s1_npc tool."""

import json
import pytest

from src.tools.npc import handle
from tests.conftest import make_response, make_error_response


@pytest.mark.asyncio
async def test_list_calls_list_npcs(mock_tcp):
    mock_tcp.async_call.return_value = make_response([])
    await handle({"action": "list"}, mock_tcp)
    mock_tcp.async_call.assert_called_once_with("list_npcs", None)


@pytest.mark.asyncio
async def test_list_with_filter(mock_tcp):
    mock_tcp.async_call.return_value = make_response([])
    await handle({"action": "list", "filter": "conscious"}, mock_tcp)
    mock_tcp.async_call.assert_called_once_with("list_npcs", {"filter": "conscious"})


@pytest.mark.asyncio
async def test_get_calls_get_npc(mock_tcp):
    mock_tcp.async_call.return_value = make_response({"npc_id": "kyle"})
    await handle({"action": "get", "npc_id": "kyle"}, mock_tcp)
    mock_tcp.async_call.assert_called_once_with("get_npc", {"npc_id": "kyle"})


@pytest.mark.asyncio
async def test_get_requires_npc_id(mock_tcp):
    result = await handle({"action": "get"}, mock_tcp)
    mock_tcp.async_call.assert_not_called()
    assert "npc_id is required" in result[0].text


@pytest.mark.asyncio
async def test_teleport_calls_teleport_npc(mock_tcp):
    mock_tcp.async_call.return_value = make_response({"success": True})
    pos = {"x": 0.0, "y": 1.0, "z": 0.0}
    await handle({"action": "teleport", "npc_id": "kyle", "position": pos}, mock_tcp)
    mock_tcp.async_call.assert_called_once_with(
        "teleport_npc", {"npc_id": "kyle", "position": pos}
    )


@pytest.mark.asyncio
async def test_teleport_requires_npc_id(mock_tcp):
    result = await handle({"action": "teleport", "position": {"x": 0, "y": 0, "z": 0}}, mock_tcp)
    mock_tcp.async_call.assert_not_called()
    assert "npc_id is required" in result[0].text


@pytest.mark.asyncio
async def test_teleport_requires_position(mock_tcp):
    result = await handle({"action": "teleport", "npc_id": "kyle"}, mock_tcp)
    mock_tcp.async_call.assert_not_called()
    assert "position is required" in result[0].text


@pytest.mark.asyncio
async def test_set_health_calls_set_npc_health(mock_tcp):
    mock_tcp.async_call.return_value = make_response({"success": True})
    await handle({"action": "set_health", "npc_id": "kyle", "health": 75.0}, mock_tcp)
    mock_tcp.async_call.assert_called_once_with(
        "set_npc_health", {"npc_id": "kyle", "health": 75.0}
    )


@pytest.mark.asyncio
async def test_set_health_allows_zero(mock_tcp):
    """health=0 is valid (NPC knocked out), must not be rejected."""
    mock_tcp.async_call.return_value = make_response({"success": True})
    await handle({"action": "set_health", "npc_id": "kyle", "health": 0}, mock_tcp)
    mock_tcp.async_call.assert_called_once()


@pytest.mark.asyncio
async def test_set_health_requires_npc_id(mock_tcp):
    result = await handle({"action": "set_health", "health": 50}, mock_tcp)
    mock_tcp.async_call.assert_not_called()
    assert "npc_id is required" in result[0].text


@pytest.mark.asyncio
async def test_set_health_requires_health_param(mock_tcp):
    result = await handle({"action": "set_health", "npc_id": "kyle"}, mock_tcp)
    mock_tcp.async_call.assert_not_called()
    assert "health is required" in result[0].text


@pytest.mark.asyncio
async def test_unknown_action(mock_tcp):
    result = await handle({"action": "dance"}, mock_tcp)
    mock_tcp.async_call.assert_not_called()
    assert "Unknown action" in result[0].text


@pytest.mark.asyncio
async def test_error_response_propagated(mock_tcp):
    mock_tcp.async_call.return_value = make_error_response("NPC not found", code=-32000)
    result = await handle({"action": "get", "npc_id": "ghost"}, mock_tcp)
    assert "NPC not found" in result[0].text


@pytest.mark.asyncio
async def test_exception_returns_generic_error(mock_tcp):
    mock_tcp.async_call.side_effect = ConnectionError("socket died")
    result = await handle({"action": "list"}, mock_tcp)
    assert "internal error" in result[0].text.lower()
    assert "socket died" not in result[0].text
