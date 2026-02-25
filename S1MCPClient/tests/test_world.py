"""Unit tests for the s1_world tool."""

import json
import pytest

from src.tools.world import handle, _format_logs
from tests.conftest import make_response, make_error_response


# ---------------------------------------------------------------------------
# Basic action routing
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_get_state(mock_tcp):
    mock_tcp.async_call.return_value = make_response({"scene": "Main"})
    await handle({"action": "get_state"}, mock_tcp)
    mock_tcp.async_call.assert_called_once_with("get_game_state", {})


@pytest.mark.asyncio
async def test_list_locations(mock_tcp):
    mock_tcp.async_call.return_value = make_response([])
    await handle({"action": "list_locations"}, mock_tcp)
    mock_tcp.async_call.assert_called_once_with("list_properties", {})


@pytest.mark.asyncio
async def test_get_location_by_id(mock_tcp):
    mock_tcp.async_call.return_value = make_response({"id": "house1"})
    await handle({"action": "get_location", "property_id": "house1"}, mock_tcp)
    mock_tcp.async_call.assert_called_once_with("get_property", {"property_id": "house1"})


@pytest.mark.asyncio
async def test_get_location_by_name(mock_tcp):
    mock_tcp.async_call.return_value = make_response({"name": "Farm"})
    await handle({"action": "get_location", "property_name": "Farm"}, mock_tcp)
    mock_tcp.async_call.assert_called_once_with("get_property", {"property_name": "Farm"})


@pytest.mark.asyncio
async def test_get_location_with_both_id_and_name(mock_tcp):
    """Both id and name provided — both should be forwarded."""
    mock_tcp.async_call.return_value = make_response({})
    await handle({"action": "get_location", "property_id": "h1", "property_name": "House"}, mock_tcp)
    call_params = mock_tcp.async_call.call_args[0][1]
    assert "property_id" in call_params
    assert "property_name" in call_params


@pytest.mark.asyncio
async def test_get_location_requires_id_or_name(mock_tcp):
    result = await handle({"action": "get_location"}, mock_tcp)
    mock_tcp.async_call.assert_not_called()
    assert "property_id or property_name" in result[0].text


@pytest.mark.asyncio
async def test_list_vehicles(mock_tcp):
    mock_tcp.async_call.return_value = make_response([])
    await handle({"action": "list_vehicles"}, mock_tcp)
    mock_tcp.async_call.assert_called_once_with("list_vehicles", {})


@pytest.mark.asyncio
async def test_get_vehicle(mock_tcp):
    mock_tcp.async_call.return_value = make_response({"id": "car1"})
    await handle({"action": "get_vehicle", "vehicle_id": "car1"}, mock_tcp)
    mock_tcp.async_call.assert_called_once_with("get_vehicle", {"vehicle_id": "car1"})


@pytest.mark.asyncio
async def test_get_vehicle_requires_id(mock_tcp):
    result = await handle({"action": "get_vehicle"}, mock_tcp)
    mock_tcp.async_call.assert_not_called()
    assert "vehicle_id is required" in result[0].text


# ---------------------------------------------------------------------------
# capture_logs
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_capture_logs_no_filters(mock_tcp):
    log_result = {"lines": [], "total_lines_in_file": 0, "filtered_count": 0, "filters_applied": []}
    mock_tcp.async_call.return_value = make_response(log_result)
    await handle({"action": "capture_logs"}, mock_tcp)
    mock_tcp.async_call.assert_called_once_with("capture_logs", {})


@pytest.mark.asyncio
async def test_capture_logs_with_filters(mock_tcp):
    mock_tcp.async_call.return_value = make_response(
        {"lines": [], "total_lines_in_file": 0, "filtered_count": 0, "filters_applied": []}
    )
    args = {"action": "capture_logs", "last_n_lines": 50, "keyword": "error"}
    await handle(args, mock_tcp)
    call_params = mock_tcp.async_call.call_args[0][1]
    assert call_params["last_n_lines"] == 50
    assert call_params["keyword"] == "error"


@pytest.mark.asyncio
async def test_capture_logs_formats_dict_lines(mock_tcp):
    log_result = {
        "lines": [{"line_number": 1, "timestamp": "12:00:00", "content": "hello"}],
        "total_lines_in_file": 1,
        "filtered_count": 1,
        "filters_applied": [],
    }
    mock_tcp.async_call.return_value = make_response(log_result)
    result = await handle({"action": "capture_logs"}, mock_tcp)
    assert "hello" in result[0].text
    assert "12:00:00" in result[0].text


@pytest.mark.asyncio
async def test_capture_logs_formats_raw_string_lines(mock_tcp):
    """Lines that are plain strings (not dicts) should not crash."""
    log_result = {
        "lines": ["raw log line"],
        "total_lines_in_file": 1,
        "filtered_count": 1,
        "filters_applied": [],
    }
    mock_tcp.async_call.return_value = make_response(log_result)
    result = await handle({"action": "capture_logs"}, mock_tcp)
    assert "raw log line" in result[0].text


# ---------------------------------------------------------------------------
# list_saves / load_save
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_list_saves_with_saves_key(mock_tcp):
    mock_tcp.async_call.return_value = make_response({"saves": ["s1", "s2"], "count": 2})
    result = await handle({"action": "list_saves"}, mock_tcp)
    assert "Found 2 save(s)" in result[0].text


@pytest.mark.asyncio
async def test_list_saves_without_saves_key(mock_tcp):
    mock_tcp.async_call.return_value = make_response(["slot0", "slot1"])
    result = await handle({"action": "list_saves"}, mock_tcp)
    assert "slot0" in result[0].text


@pytest.mark.asyncio
async def test_load_save(mock_tcp):
    mock_tcp.async_call.return_value = make_response({"message": "Loaded successfully"})
    result = await handle({"action": "load_save", "slot_index": 0}, mock_tcp)
    mock_tcp.async_call.assert_called_once_with("load_save", {"slot_index": 0})
    assert "Loaded successfully" in result[0].text


@pytest.mark.asyncio
async def test_load_save_requires_slot_index(mock_tcp):
    result = await handle({"action": "load_save"}, mock_tcp)
    mock_tcp.async_call.assert_not_called()
    assert "slot_index is required" in result[0].text


@pytest.mark.asyncio
async def test_load_save_returned_to_menu_note(mock_tcp):
    mock_tcp.async_call.return_value = make_response(
        {"message": "Done", "returned_to_menu": True}
    )
    result = await handle({"action": "load_save", "slot_index": 1}, mock_tcp)
    assert "Returned to menu" in result[0].text


# ---------------------------------------------------------------------------
# Edge cases
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_unknown_action(mock_tcp):
    result = await handle({"action": "explode"}, mock_tcp)
    mock_tcp.async_call.assert_not_called()
    assert "Unknown action" in result[0].text


@pytest.mark.asyncio
async def test_exception_returns_generic_error(mock_tcp):
    mock_tcp.async_call.side_effect = RuntimeError("network failure")
    result = await handle({"action": "get_state"}, mock_tcp)
    assert "internal error" in result[0].text.lower()
    assert "network failure" not in result[0].text


# ---------------------------------------------------------------------------
# _format_logs unit tests
# ---------------------------------------------------------------------------

def test_format_logs_non_dict_result():
    result = _format_logs("raw string")
    assert result[0].text == "raw string"


def test_format_logs_empty():
    result = _format_logs({"lines": [], "total_lines_in_file": 0, "filtered_count": 0,
                           "filters_applied": []})
    assert "No lines matched" in result[0].text


def test_format_logs_with_warning():
    result = _format_logs({"lines": [], "total_lines_in_file": 0, "filtered_count": 0,
                           "filters_applied": [], "warning": "file truncated"})
    assert "file truncated" in result[0].text


def test_format_logs_with_filters_list():
    result = _format_logs({"lines": [], "total_lines_in_file": 0, "filtered_count": 0,
                           "filters_applied": ["last_n=10", "keyword=err"]})
    assert "last_n=10" in result[0].text
