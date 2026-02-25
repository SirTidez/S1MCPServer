"""NPC tool — all NPC operations in one call."""

import json
from typing import Any, Dict
from mcp.types import Tool, TextContent

from ..tcp_client import TcpClient
from ..utils.logger import get_logger

logger = get_logger()

TOOL_NAME = "s1_npc"

TOOL = Tool(
    name=TOOL_NAME,
    description=(
        "Interact with NPCs. Choose an action:\n"
        "- list: List all NPCs (optional: filter = conscious|unconscious|in_building|in_vehicle)\n"
        "- get: Get NPC details (requires: npc_id)\n"
        "- teleport: Move NPC to position (requires: npc_id, position {x,y,z})\n"
        "- set_health: Set NPC health value (requires: npc_id, health)"
    ),
    inputSchema={
        "type": "object",
        "properties": {
            "action": {
                "type": "string",
                "enum": ["list", "get", "teleport", "set_health"],
                "description": "Operation to perform"
            },
            "npc_id": {
                "type": "string",
                "description": "NPC unique identifier (get/teleport/set_health)"
            },
            "filter": {
                "type": "string",
                "enum": ["conscious", "unconscious", "in_building", "in_vehicle"],
                "description": "Optional filter for list action"
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
            "health": {
                "type": "number",
                "description": "New health value (set_health only)"
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
            if f := arguments.get("filter"):
                params["filter"] = f
            resp = await tcp_client.async_call("list_npcs", params or None)
        elif action == "get":
            npc_id = arguments.get("npc_id")
            if not npc_id:
                return [TextContent(type="text", text="Error: npc_id is required")]
            resp = await tcp_client.async_call("get_npc", {"npc_id": npc_id})
        elif action == "teleport":
            npc_id = arguments.get("npc_id")
            position = arguments.get("position")
            if not npc_id:
                return [TextContent(type="text", text="Error: npc_id is required")]
            if not position:
                return [TextContent(type="text", text="Error: position is required")]
            resp = await tcp_client.async_call("teleport_npc", {"npc_id": npc_id, "position": position})
        elif action == "set_health":
            npc_id = arguments.get("npc_id")
            health = arguments.get("health")
            if not npc_id:
                return [TextContent(type="text", text="Error: npc_id is required")]
            if health is None:
                return [TextContent(type="text", text="Error: health is required")]
            resp = await tcp_client.async_call("set_npc_health", {"npc_id": npc_id, "health": health})
        else:
            return [TextContent(type="text", text=f"Error: Unknown action '{action}'")]

        if resp.error:
            return [TextContent(type="text", text=f"Error: {resp.error.message} (code: {resp.error.code})")]
        return [TextContent(type="text", text=json.dumps(resp.result, indent=2))]

    except Exception as e:
        logger.exception(f"Error in s1_npc/{action}")
        return [TextContent(type="text", text="An internal error occurred while processing your request.")]


TOOL_HANDLERS = {TOOL_NAME: handle}


def get_tools(_tcp_client: TcpClient) -> list[Tool]:
    return [TOOL]
