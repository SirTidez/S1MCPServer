"""Unit tests for the s1_inspect tool."""

import pytest

from src.tools.inspect import handle
from tests.conftest import make_response, make_error_response


# ---------------------------------------------------------------------------
# Discovery actions
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_find_objects_no_filters(mock_tcp):
    mock_tcp.async_call.return_value = make_response([])
    await handle({"action": "find_objects"}, mock_tcp)
    mock_tcp.async_call.assert_called_once_with("find_gameobjects", {})


@pytest.mark.asyncio
async def test_find_objects_with_name_pattern(mock_tcp):
    """Sends 'name_pattern', not 'name' (P2 fix)."""
    mock_tcp.async_call.return_value = make_response([])
    await handle({"action": "find_objects", "name_pattern": "Player"}, mock_tcp)
    call_params = mock_tcp.async_call.call_args[0][1]
    assert "name_pattern" in call_params
    assert "name" not in call_params


@pytest.mark.asyncio
async def test_find_by_type_sends_component_type(mock_tcp):
    """Sends 'component_type', not 'type_name' (P1 fix)."""
    mock_tcp.async_call.return_value = make_response([])
    await handle({"action": "find_by_type", "component_type": "Rigidbody"}, mock_tcp)
    method, params = mock_tcp.async_call.call_args[0]
    assert method == "find_objects_by_type"
    assert params["component_type"] == "Rigidbody"
    assert "type_name" not in params


@pytest.mark.asyncio
async def test_find_by_type_requires_component_type(mock_tcp):
    result = await handle({"action": "find_by_type"}, mock_tcp)
    mock_tcp.async_call.assert_not_called()
    assert "component_type" in result[0].text


@pytest.mark.asyncio
async def test_search_types_sends_pattern_not_query(mock_tcp):
    """Sends 'pattern' + 'component_types_only', not 'query' + 'include_non_public' (P1 fix)."""
    mock_tcp.async_call.return_value = make_response([])
    await handle({"action": "search_types", "pattern": "NPC"}, mock_tcp)
    method, params = mock_tcp.async_call.call_args[0]
    assert method == "search_types"
    assert params["pattern"] == "NPC"
    assert "query" not in params
    assert "include_non_public" not in params


@pytest.mark.asyncio
async def test_search_types_component_types_only_flag(mock_tcp):
    mock_tcp.async_call.return_value = make_response([])
    await handle({"action": "search_types", "pattern": "X", "component_types_only": True}, mock_tcp)
    call_params = mock_tcp.async_call.call_args[0][1]
    assert call_params.get("component_types_only") is True


@pytest.mark.asyncio
async def test_search_types_requires_pattern(mock_tcp):
    result = await handle({"action": "search_types"}, mock_tcp)
    mock_tcp.async_call.assert_not_called()
    assert "pattern" in result[0].text


@pytest.mark.asyncio
async def test_list_scenes(mock_tcp):
    mock_tcp.async_call.return_value = make_response(["Main"])
    await handle({"action": "list_scenes"}, mock_tcp)
    mock_tcp.async_call.assert_called_once_with("list_scenes", {})


@pytest.mark.asyncio
async def test_get_hierarchy_no_scene(mock_tcp):
    mock_tcp.async_call.return_value = make_response({})
    await handle({"action": "get_hierarchy"}, mock_tcp)
    mock_tcp.async_call.assert_called_once_with("get_scene_hierarchy", {})


@pytest.mark.asyncio
async def test_get_hierarchy_with_scene(mock_tcp):
    mock_tcp.async_call.return_value = make_response({})
    await handle({"action": "get_hierarchy", "scene_name": "Main"}, mock_tcp)
    call_params = mock_tcp.async_call.call_args[0][1]
    assert call_params["scene_name"] == "Main"


@pytest.mark.asyncio
async def test_get_scene_objects(mock_tcp):
    mock_tcp.async_call.return_value = make_response([])
    await handle({"action": "get_scene_objects"}, mock_tcp)
    mock_tcp.async_call.assert_called_once_with("get_scene_objects", {})


# ---------------------------------------------------------------------------
# Inspection actions
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_inspect_object_requires_object_name(mock_tcp):
    result = await handle({"action": "inspect_object"}, mock_tcp)
    mock_tcp.async_call.assert_not_called()
    assert "object_name" in result[0].text


@pytest.mark.asyncio
async def test_inspect_object_with_type(mock_tcp):
    mock_tcp.async_call.return_value = make_response({})
    await handle({"action": "inspect_object", "object_name": "Player", "object_type": "NPC"}, mock_tcp)
    call_params = mock_tcp.async_call.call_args[0][1]
    assert call_params["object_type"] == "NPC"


@pytest.mark.asyncio
async def test_list_components(mock_tcp):
    mock_tcp.async_call.return_value = make_response([])
    await handle({"action": "list_components", "object_name": "Enemy"}, mock_tcp)
    mock_tcp.async_call.assert_called_once_with("list_components", {"object_name": "Enemy"})


@pytest.mark.asyncio
async def test_get_component(mock_tcp):
    mock_tcp.async_call.return_value = make_response({})
    await handle({"action": "get_component", "object_name": "Enemy", "component_type": "Health"}, mock_tcp)
    mock_tcp.async_call.assert_called_once_with(
        "get_component_by_type", {"object_name": "Enemy", "component_type": "Health"}
    )


@pytest.mark.asyncio
async def test_inspect_component_max_depth_zero(mock_tcp):
    """max_depth=0 is a valid falsy value — must NOT be silently dropped (walrus fix)."""
    mock_tcp.async_call.return_value = make_response({})
    await handle({"action": "inspect_component", "object_name": "X", "max_depth": 0}, mock_tcp)
    call_params = mock_tcp.async_call.call_args[0][1]
    assert "max_depth" in call_params
    assert call_params["max_depth"] == 0


@pytest.mark.asyncio
async def test_get_member(mock_tcp):
    mock_tcp.async_call.return_value = make_response(42)
    await handle({"action": "get_member", "object_name": "X", "member_path": "health.current"}, mock_tcp)
    mock_tcp.async_call.assert_called_once_with(
        "get_member_value", {"object_name": "X", "member_path": "health.current"}
    )


@pytest.mark.asyncio
async def test_get_member_requires_object_and_path(mock_tcp):
    result = await handle({"action": "get_member", "object_name": "X"}, mock_tcp)
    assert "member_path" in result[0].text


@pytest.mark.asyncio
async def test_get_transform(mock_tcp):
    mock_tcp.async_call.return_value = make_response({})
    await handle({"action": "get_transform", "object_name": "Player"}, mock_tcp)
    mock_tcp.async_call.assert_called_once_with("get_transform", {"object_name": "Player"})


@pytest.mark.asyncio
async def test_is_active(mock_tcp):
    mock_tcp.async_call.return_value = make_response(True)
    await handle({"action": "is_active", "object_name": "Enemy"}, mock_tcp)
    mock_tcp.async_call.assert_called_once_with("is_active", {"object_name": "Enemy"})


# ---------------------------------------------------------------------------
# Modification actions
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_set_field(mock_tcp):
    mock_tcp.async_call.return_value = make_response({"success": True})
    await handle({
        "action": "set_field",
        "object_name": "Player",
        "field_name": "health",
        "value": 100
    }, mock_tcp)
    call_params = mock_tcp.async_call.call_args[0][1]
    assert call_params["value"] == 100


@pytest.mark.asyncio
async def test_set_field_value_zero_is_valid(mock_tcp):
    """value=0 is falsy but valid — must not be rejected."""
    mock_tcp.async_call.return_value = make_response({"success": True})
    await handle({
        "action": "set_field",
        "object_name": "Player",
        "field_name": "health",
        "value": 0
    }, mock_tcp)
    mock_tcp.async_call.assert_called_once()
    call_params = mock_tcp.async_call.call_args[0][1]
    assert call_params["value"] == 0


@pytest.mark.asyncio
async def test_set_field_requires_value_key(mock_tcp):
    result = await handle({
        "action": "set_field",
        "object_name": "Player",
        "field_name": "health"
    }, mock_tcp)
    mock_tcp.async_call.assert_not_called()
    assert "value is required" in result[0].text


@pytest.mark.asyncio
async def test_set_active_false_is_valid(mock_tcp):
    """active=False is falsy but valid."""
    mock_tcp.async_call.return_value = make_response({"success": True})
    await handle({"action": "set_active", "object_name": "Enemy", "active": False}, mock_tcp)
    mock_tcp.async_call.assert_called_once()
    call_params = mock_tcp.async_call.call_args[0][1]
    assert call_params["active"] is False


@pytest.mark.asyncio
async def test_set_active_requires_active_key(mock_tcp):
    result = await handle({"action": "set_active", "object_name": "Enemy"}, mock_tcp)
    mock_tcp.async_call.assert_not_called()
    assert "active is required" in result[0].text


@pytest.mark.asyncio
async def test_call_method(mock_tcp):
    mock_tcp.async_call.return_value = make_response(None)
    await handle({
        "action": "call_method",
        "object_name": "NPC",
        "method_name": "TakeDamage",
        "args": [10],
        "component_type": "Health"
    }, mock_tcp)
    call_params = mock_tcp.async_call.call_args[0][1]
    assert call_params["args"] == [10]
    assert call_params["component_type"] == "Health"


@pytest.mark.asyncio
async def test_set_transform(mock_tcp):
    mock_tcp.async_call.return_value = make_response({"success": True})
    pos = {"x": 1.0, "y": 2.0, "z": 3.0}
    await handle({"action": "set_transform", "object_name": "Cube", "position": pos}, mock_tcp)
    call_params = mock_tcp.async_call.call_args[0][1]
    assert call_params["position"] == pos


@pytest.mark.asyncio
async def test_set_component_property_requires_value(mock_tcp):
    result = await handle({
        "action": "set_component_property",
        "object_name": "X",
        "component_type": "Health",
        "property_name": "Max"
    }, mock_tcp)
    mock_tcp.async_call.assert_not_called()
    assert "value is required" in result[0].text


# ---------------------------------------------------------------------------
# Generic error handling
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_unknown_action(mock_tcp):
    result = await handle({"action": "fly"}, mock_tcp)
    mock_tcp.async_call.assert_not_called()
    assert "Unknown action" in result[0].text


@pytest.mark.asyncio
async def test_exception_returns_generic_error(mock_tcp):
    mock_tcp.async_call.side_effect = RuntimeError("unexpected crash")
    result = await handle({"action": "list_scenes"}, mock_tcp)
    assert "internal error" in result[0].text.lower()
    assert "unexpected crash" not in result[0].text
