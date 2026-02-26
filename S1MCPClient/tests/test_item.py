"""Unit tests for the s1_item tool."""

import pytest

from src.tools.item import handle
from tests.conftest import make_response, make_error_response


@pytest.mark.asyncio
async def test_list_calls_list_items(mock_tcp):
    mock_tcp.async_call.return_value = make_response([])
    await handle({"action": "list"}, mock_tcp)
    mock_tcp.async_call.assert_called_once_with("list_items", None)


@pytest.mark.asyncio
async def test_list_with_category(mock_tcp):
    mock_tcp.async_call.return_value = make_response([])
    await handle({"action": "list", "category": "drug"}, mock_tcp)
    mock_tcp.async_call.assert_called_once_with("list_items", {"category": "drug"})


@pytest.mark.asyncio
async def test_get_calls_get_item(mock_tcp):
    mock_tcp.async_call.return_value = make_response({"item_id": "weed_brick"})
    await handle({"action": "get", "item_id": "weed_brick"}, mock_tcp)
    mock_tcp.async_call.assert_called_once_with("get_item", {"item_id": "weed_brick"})


@pytest.mark.asyncio
async def test_get_requires_item_id(mock_tcp):
    result = await handle({"action": "get"}, mock_tcp)
    mock_tcp.async_call.assert_not_called()
    assert "item_id is required" in result[0].text


@pytest.mark.asyncio
async def test_spawn_calls_spawn_item(mock_tcp):
    mock_tcp.async_call.return_value = make_response({"success": True})
    pos = {"x": 10.0, "y": 0.5, "z": 20.0}
    await handle({"action": "spawn", "item_id": "cash", "position": pos}, mock_tcp)
    mock_tcp.async_call.assert_called_once_with(
        "spawn_item", {"item_id": "cash", "position": pos}
    )


@pytest.mark.asyncio
async def test_spawn_omits_quantity_when_one(mock_tcp):
    """quantity=1 is default — should NOT be included in the params dict."""
    mock_tcp.async_call.return_value = make_response({"success": True})
    pos = {"x": 0, "y": 0, "z": 0}
    await handle({"action": "spawn", "item_id": "x", "position": pos, "quantity": 1}, mock_tcp)
    call_kwargs = mock_tcp.async_call.call_args[0][1]
    assert "quantity" not in call_kwargs


@pytest.mark.asyncio
async def test_spawn_includes_quantity_when_not_one(mock_tcp):
    mock_tcp.async_call.return_value = make_response({"success": True})
    pos = {"x": 0, "y": 0, "z": 0}
    await handle({"action": "spawn", "item_id": "x", "position": pos, "quantity": 5}, mock_tcp)
    call_kwargs = mock_tcp.async_call.call_args[0][1]
    assert call_kwargs["quantity"] == 5


@pytest.mark.asyncio
async def test_spawn_requires_item_id(mock_tcp):
    result = await handle({"action": "spawn", "position": {"x": 0, "y": 0, "z": 0}}, mock_tcp)
    mock_tcp.async_call.assert_not_called()
    assert "item_id is required" in result[0].text


@pytest.mark.asyncio
async def test_spawn_requires_position(mock_tcp):
    result = await handle({"action": "spawn", "item_id": "cash"}, mock_tcp)
    mock_tcp.async_call.assert_not_called()
    assert "position is required" in result[0].text


@pytest.mark.asyncio
async def test_unknown_action(mock_tcp):
    result = await handle({"action": "delete"}, mock_tcp)
    mock_tcp.async_call.assert_not_called()
    assert "Unknown action" in result[0].text


@pytest.mark.asyncio
async def test_error_response_propagated(mock_tcp):
    mock_tcp.async_call.return_value = make_error_response("Item not found", code=-32000)
    result = await handle({"action": "get", "item_id": "ghost_item"}, mock_tcp)
    assert "Item not found" in result[0].text


@pytest.mark.asyncio
async def test_exception_returns_generic_error(mock_tcp):
    mock_tcp.async_call.side_effect = OSError("pipe broken")
    result = await handle({"action": "list"}, mock_tcp)
    assert "internal error" in result[0].text.lower()
    assert "pipe broken" not in result[0].text
