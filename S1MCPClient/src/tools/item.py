"""Item tool — all item operations in one call."""

import json
from typing import Any, Dict
from mcp.types import Tool, TextContent

from ..tcp_client import TcpClient
from ..utils.logger import get_logger

logger = get_logger()

TOOL_NAME = "s1_item"

TOOL = Tool(
    name=TOOL_NAME,
    description=(
        "Interact with game items. Choose an action:\n"
        "- list: List all item definitions (optional: category filter)\n"
        "- get: Get item details (requires: item_id)\n"
        "- spawn: Spawn item in world (requires: item_id, position {x,y,z}; optional: quantity)"
    ),
    inputSchema={
        "type": "object",
        "properties": {
            "action": {
                "type": "string",
                "enum": ["list", "get", "spawn"],
                "description": "Operation to perform"
            },
            "item_id": {
                "type": "string",
                "description": "Item unique identifier (get/spawn)"
            },
            "category": {
                "type": "string",
                "description": "Category filter (list only)"
            },
            "position": {
                "type": "object",
                "properties": {
                    "x": {"type": "number"},
                    "y": {"type": "number"},
                    "z": {"type": "number"}
                },
                "required": ["x", "y", "z"],
                "description": "Spawn coordinates (spawn only)"
            },
            "quantity": {
                "type": "integer",
                "minimum": 1,
                "description": "Number to spawn (spawn only, default: 1)",
                "default": 1
            }
        },
        "required": ["action"]
    }
)


async def handle(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    action = arguments.get("action")

    try:
        if action == "list":
            params = {}
            if c := arguments.get("category"):
                params["category"] = c
            resp = await tcp_client.async_call("list_items", params or None)
        elif action == "get":
            item_id = arguments.get("item_id")
            if not item_id:
                return [TextContent(type="text", text="Error: item_id is required")]
            resp = await tcp_client.async_call("get_item", {"item_id": item_id})
        elif action == "spawn":
            item_id = arguments.get("item_id")
            position = arguments.get("position")
            if not item_id:
                return [TextContent(type="text", text="Error: item_id is required")]
            if not position:
                return [TextContent(type="text", text="Error: position is required")]
            params = {"item_id": item_id, "position": position}
            qty = arguments.get("quantity", 1)
            if isinstance(qty, bool) or not isinstance(qty, int) or qty < 1:
                return [TextContent(type="text", text="Error: quantity must be a positive integer")]
            if qty != 1:
                params["quantity"] = qty
            resp = await tcp_client.async_call("spawn_item", params)
        else:
            return [TextContent(type="text", text=f"Error: Unknown action '{action}'")]

        if resp.error:
            return [TextContent(type="text", text=f"Error: {resp.error.message} (code: {resp.error.code})")]
        return [TextContent(type="text", text=json.dumps(resp.result, indent=2))]

    except Exception:
        logger.exception(f"Error in s1_item/{action}")
        return [TextContent(type="text", text="An internal error occurred while processing your request.")]


TOOL_HANDLERS = {TOOL_NAME: handle}


def get_tools(_tcp_client: TcpClient) -> list[Tool]:
    return [TOOL]
