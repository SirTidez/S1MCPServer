"""Player tool — all player operations in one call."""

import json
from typing import Any, Dict
from mcp.types import Tool, TextContent

from ..tcp_client import TcpClient
from ..utils.logger import get_logger

logger = get_logger()

TOOL_NAME = "s1_player"

TOOL = Tool(
    name=TOOL_NAME,
    description=(
        "Interact with the player character. Choose an action:\n"
        "- get: Get full player state (position, health, money, network status)\n"
        "- get_inventory: Get the player's inventory\n"
        "- teleport: Teleport player to position (requires: position {x,y,z})\n"
        "- add_item: Add item(s) to inventory (requires: item_id; optional: quantity)"
    ),
    inputSchema={
        "type": "object",
        "properties": {
            "action": {
                "type": "string",
                "enum": ["get", "get_inventory", "teleport", "add_item"],
                "description": "Operation to perform"
            },
            "position": {
                "type": "object",
                "properties": {
                    "x": {"type": "number"},
                    "y": {"type": "number"},
                    "z": {"type": "number"}
                },
                "required": ["x", "y", "z"],
                "description": "Target coordinates (teleport only)"
            },
            "item_id": {
                "type": "string",
                "description": "Item ID to add (add_item only)"
            },
            "quantity": {
                "type": "integer",
                "minimum": 1,
                "description": "How many to add (add_item, default: 1)",
                "default": 1
            }
        },
        "required": ["action"]
    }
)


async def handle(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    action = arguments.get("action")

    try:
        if action == "get":
            resp = await tcp_client.async_call("get_player", {})
        elif action == "get_inventory":
            resp = await tcp_client.async_call("get_player_inventory", {})
        elif action == "teleport":
            position = arguments.get("position")
            if not position:
                return [TextContent(type="text", text="Error: position is required for teleport")]
            resp = await tcp_client.async_call("teleport_player", {"position": position})
        elif action == "add_item":
            item_id = arguments.get("item_id")
            if not item_id:
                return [TextContent(type="text", text="Error: item_id is required for add_item")]
            qty = arguments.get("quantity", 1)
            if isinstance(qty, bool) or not isinstance(qty, int) or qty < 1:
                return [TextContent(type="text", text="Error: quantity must be a positive integer")]
            resp = await tcp_client.async_call("add_item_to_player", {
                "item_id": item_id,
                "quantity": qty
            })
        else:
            return [TextContent(type="text", text=f"Error: Unknown action '{action}'")]

        if resp.error:
            return [TextContent(type="text", text=f"Error: {resp.error.message} (code: {resp.error.code})")]
        return [TextContent(type="text", text=json.dumps(resp.result, indent=2))]

    except Exception as e:
        logger.exception(f"Error in s1_player/{action}")
        return [TextContent(type="text", text="An internal error occurred while processing your request.")]


TOOL_HANDLERS = {TOOL_NAME: handle}


def get_tools(_tcp_client: TcpClient) -> list[Tool]:
    return [TOOL]
