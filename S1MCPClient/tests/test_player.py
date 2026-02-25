"""Unit tests for the s1_player tool."""

import json
import pytest
from unittest.mock import AsyncMock

from src.tools.player import handle
from tests.conftest import make_response, make_error_response


@pytest.mark.asyncio
async def test_get_calls_get_player(mock_tcp):
    mock_tcp.async_call.return_value = make_response({"health": 100})
    result = await handle({"action": "get"}, mock_tcp)
    mock_tcp.async_call.assert_called_once_with("get_player", {})
    assert "health" in result[0].text


@pytest.mark.asyncio
async def test_get_inventory_calls_correct_method(mock_tcp):
    mock_tcp.async_call.return_value = make_response({"items": []})
    result = await handle({"action": "get_inventory"}, mock_tcp)
    mock_tcp.async_call.assert_called_once_with("get_player_inventory", {})
    assert "items" in result[0].text


@pytest.mark.asyncio
async def test_teleport_calls_teleport_player(mock_tcp):
    mock_tcp.async_call.return_value = make_response({"success": True})
    pos = {"x": 1.0, "y": 2.0, "z": 3.0}
    result = await handle({"action": "teleport", "position": pos}, mock_tcp)
    mock_tcp.async_call.assert_called_once_with("teleport_player", {"position": pos})


@pytest.mark.asyncio
async def test_teleport_requires_position(mock_tcp):
    result = await handle({"action": "teleport"}, mock_tcp)
    mock_tcp.async_call.assert_not_called()
    assert "position is required" in result[0].text


@pytest.mark.asyncio
async def test_add_item_calls_add_item_to_player(mock_tcp):
    mock_tcp.async_call.return_value = make_response({"success": True})
    result = await handle({"action": "add_item", "item_id": "weed_brick"}, mock_tcp)
    mock_tcp.async_call.assert_called_once_with(
        "add_item_to_player", {"item_id": "weed_brick", "quantity": 1}
    )


@pytest.mark.asyncio
async def test_add_item_custom_quantity(mock_tcp):
    mock_tcp.async_call.return_value = make_response({"success": True})
    await handle({"action": "add_item", "item_id": "cash", "quantity": 5}, mock_tcp)
    mock_tcp.async_call.assert_called_once_with(
        "add_item_to_player", {"item_id": "cash", "quantity": 5}
    )


@pytest.mark.asyncio
async def test_add_item_requires_item_id(mock_tcp):
    result = await handle({"action": "add_item"}, mock_tcp)
    mock_tcp.async_call.assert_not_called()
    assert "item_id is required" in result[0].text


@pytest.mark.asyncio
@pytest.mark.parametrize("bad_qty", [0, -1, True, False, 1.5, "2"])
async def test_add_item_rejects_invalid_quantity(mock_tcp, bad_qty):
    result = await handle({"action": "add_item", "item_id": "x", "quantity": bad_qty}, mock_tcp)
    mock_tcp.async_call.assert_not_called()
    assert "quantity" in result[0].text.lower()


@pytest.mark.asyncio
async def test_unknown_action_returns_error(mock_tcp):
    result = await handle({"action": "fly"}, mock_tcp)
    mock_tcp.async_call.assert_not_called()
    assert "Unknown action" in result[0].text


@pytest.mark.asyncio
async def test_error_response_propagated(mock_tcp):
    mock_tcp.async_call.return_value = make_error_response("Not found", code=-32601)
    result = await handle({"action": "get"}, mock_tcp)
    assert "Not found" in result[0].text
    assert "-32601" in result[0].text


@pytest.mark.asyncio
async def test_exception_returns_generic_error(mock_tcp):
    mock_tcp.async_call.side_effect = RuntimeError("boom")
    result = await handle({"action": "get"}, mock_tcp)
    assert "internal error" in result[0].text.lower()
    assert "boom" not in result[0].text  # internal details not leaked
