"""World tool — game state, locations, vehicles, logs, and saves."""

import json
from typing import Any, Dict
from mcp.types import Tool, TextContent

from ..tcp_client import TcpClient
from ..utils.logger import get_logger

logger = get_logger()

TOOL_NAME = "s1_world"

TOOL = Tool(
    name=TOOL_NAME,
    description=(
        "Query and manage world/game state. Choose an action:\n"
        "- get_state: Current scene, game time, network mode, and loaded mods\n"
        "- list_locations: List all properties/buildings in the game\n"
        "- get_location: Get a specific property (requires: property_id or property_name)\n"
        "- list_vehicles: List all vehicles in the world\n"
        "- get_vehicle: Get vehicle details (requires: vehicle_id)\n"
        "- capture_logs: Read game logs from MelonLoader (optional: last_n_lines, first_n_lines, "
        "keyword, from_timestamp, to_timestamp, include_pattern, exclude_pattern)\n"
        "- list_saves: List all save game slots\n"
        "- load_save: Load a save by slot index (requires: slot_index, 0-based)"
    ),
    inputSchema={
        "type": "object",
        "properties": {
            "action": {
                "type": "string",
                "enum": [
                    "get_state", "list_locations", "get_location",
                    "list_vehicles", "get_vehicle",
                    "capture_logs", "list_saves", "load_save"
                ],
                "description": "Operation to perform"
            },
            "property_id": {
                "type": "string",
                "description": "Property unique identifier (get_location)"
            },
            "property_name": {
                "type": "string",
                "description": "Property name as alternative to property_id (get_location)"
            },
            "vehicle_id": {
                "type": "string",
                "description": "Vehicle unique identifier (get_vehicle)"
            },
            "slot_index": {
                "type": "integer",
                "minimum": 0,
                "description": "0-based save slot index (load_save)"
            },
            "last_n_lines": {
                "type": "integer",
                "minimum": 1,
                "description": "Return last N log lines (capture_logs)"
            },
            "first_n_lines": {
                "type": "integer",
                "minimum": 1,
                "description": "Return first N log lines (capture_logs)"
            },
            "keyword": {
                "type": "string",
                "description": "Filter logs by keyword (capture_logs)"
            },
            "from_timestamp": {
                "type": "string",
                "description": "Filter logs from timestamp HH:mm:ss (capture_logs)"
            },
            "to_timestamp": {
                "type": "string",
                "description": "Filter logs to timestamp HH:mm:ss (capture_logs)"
            },
            "include_pattern": {
                "type": "string",
                "description": "Regex pattern — only matching lines (capture_logs)"
            },
            "exclude_pattern": {
                "type": "string",
                "description": "Regex pattern — exclude matching lines (capture_logs)"
            }
        },
        "required": ["action"]
    }
)


async def handle(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    action = arguments.get("action")

    try:
        if action == "get_state":
            resp = await tcp_client.async_call("get_game_state", {})

        elif action == "list_locations":
            resp = await tcp_client.async_call("list_properties", {})

        elif action == "get_location":
            pid = arguments.get("property_id")
            pname = arguments.get("property_name")
            if not pid and not pname:
                return [TextContent(type="text", text="Error: property_id or property_name is required")]
            params = {}
            if pid:
                params["property_id"] = pid
            if pname:
                params["property_name"] = pname
            resp = await tcp_client.async_call("get_property", params)

        elif action == "list_vehicles":
            resp = await tcp_client.async_call("list_vehicles", {})

        elif action == "get_vehicle":
            vid = arguments.get("vehicle_id")
            if not vid:
                return [TextContent(type="text", text="Error: vehicle_id is required")]
            resp = await tcp_client.async_call("get_vehicle", {"vehicle_id": vid})

        elif action == "capture_logs":
            params = {k: arguments[k] for k in (
                "last_n_lines", "first_n_lines", "keyword",
                "from_timestamp", "to_timestamp", "include_pattern", "exclude_pattern"
            ) if k in arguments}
            resp = await tcp_client.async_call("capture_logs", params)
            if resp.error:
                return [TextContent(type="text", text=f"Error: {resp.error.message} (code: {resp.error.code})")]
            return _format_logs(resp.result)

        elif action == "list_saves":
            resp = await tcp_client.async_call("list_saves", {})
            if resp.error:
                return [TextContent(type="text", text=f"Error: {resp.error.message} (code: {resp.error.code})")]
            result_text = json.dumps(resp.result, indent=2)
            if isinstance(resp.result, dict) and "saves" in resp.result:
                count = resp.result.get("count", 0)
                return [TextContent(type="text", text=f"Found {count} save(s):\n\n{result_text}")]
            return [TextContent(type="text", text=result_text)]

        elif action == "load_save":
            slot = arguments.get("slot_index")
            if slot is None:
                return [TextContent(type="text", text="Error: slot_index is required")]
            resp = await tcp_client.async_call("load_save", {"slot_index": slot})
            if resp.error:
                return [TextContent(type="text", text=f"Error: {resp.error.message} (code: {resp.error.code})")]
            if isinstance(resp.result, dict):
                msg = resp.result.get("message", "")
                note = "\n  Note: Returned to menu first." if resp.result.get("returned_to_menu") else ""
                return [TextContent(type="text", text=f"✓ {msg}{note}")]
            return [TextContent(type="text", text=json.dumps(resp.result, indent=2))]

        else:
            return [TextContent(type="text", text=f"Error: Unknown action '{action}'")]

        if resp.error:
            return [TextContent(type="text", text=f"Error: {resp.error.message} (code: {resp.error.code})")]
        return [TextContent(type="text", text=json.dumps(resp.result, indent=2))]

    except Exception as e:
        logger.error(f"Error in s1_world/{action}: {e}")
        return [TextContent(type="text", text=f"Error: {e}")]


def _format_logs(result) -> list[TextContent]:
    if not isinstance(result, dict):
        return [TextContent(type="text", text=str(result))]

    lines = result.get("lines", [])
    total = result.get("total_lines_in_file", 0)
    filtered = result.get("filtered_count", 0)
    filters = result.get("filters_applied", [])
    warning = result.get("warning")

    out = []
    if warning:
        out.append(f"Warning: {warning}\n")
    out.append(f"Log Summary: {len(lines)} lines returned ({filtered} after filters, {total} total)")
    if filters:
        out.append("Filters: " + ", ".join(filters))
    if lines:
        out.append("\n--- Log Lines ---")
        for lo in lines:
            ln = lo.get("line_number", "?")
            ts = lo.get("timestamp", "")
            content = lo.get("content", "")
            out.append(f"[{ln}]{f' [{ts}]' if ts else ''} {content}")
    else:
        out.append("(No lines matched)")
    return [TextContent(type="text", text="\n".join(out))]


TOOL_HANDLERS = {TOOL_NAME: handle}


def get_tools(_tcp_client: TcpClient) -> list[Tool]:
    return [TOOL]
