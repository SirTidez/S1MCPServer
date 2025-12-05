"""NPC-related MCP tools."""

from typing import Any, Dict, Optional
from mcp.types import Tool, TextContent

from ..tcp_client import TcpClient
from ..utils.logger import get_logger


logger = get_logger()


def get_npc_tools(tcp_client: TcpClient) -> list[Tool]:
    """
    Get all NPC-related MCP tools.
    
    Args:
        tcp_client: TCP client instance
    
    Returns:
        List of MCP Tool definitions
    """
    return [
        Tool(
            name="s1_get_npc",
            description="Get detailed information about a specific NPC by ID",
            inputSchema={
                "type": "object",
                "properties": {
                    "npc_id": {
                        "type": "string",
                        "description": "The unique identifier of the NPC"
                    }
                },
                "required": ["npc_id"]
            }
        ),
        Tool(
            name="s1_list_npcs",
            description="List all NPCs in the game, optionally filtered by state",
            inputSchema={
                "type": "object",
                "properties": {
                    "filter": {
                        "type": "string",
                        "enum": ["conscious", "unconscious", "in_building", "in_vehicle"],
                        "description": "Optional filter to apply to the list"
                    }
                },
                "required": []
            }
        ),
        Tool(
            name="s1_get_npc_position",
            description="Get the current position of an NPC",
            inputSchema={
                "type": "object",
                "properties": {
                    "npc_id": {
                        "type": "string",
                        "description": "The unique identifier of the NPC"
                    }
                },
                "required": ["npc_id"]
            }
        ),
        Tool(
            name="s1_teleport_npc",
            description="Teleport an NPC to a specific position",
            inputSchema={
                "type": "object",
                "properties": {
                    "npc_id": {
                        "type": "string",
                        "description": "The unique identifier of the NPC"
                    },
                    "position": {
                        "type": "object",
                        "properties": {
                            "x": {"type": "number", "description": "X coordinate"},
                            "y": {"type": "number", "description": "Y coordinate"},
                            "z": {"type": "number", "description": "Z coordinate"}
                        },
                        "required": ["x", "y", "z"],
                        "description": "Target position coordinates"
                    }
                },
                "required": ["npc_id", "position"]
            }
        ),
        Tool(
            name="s1_set_npc_health",
            description="Modify an NPC's health value",
            inputSchema={
                "type": "object",
                "properties": {
                    "npc_id": {
                        "type": "string",
                        "description": "The unique identifier of the NPC"
                    },
                    "health": {
                        "type": "number",
                        "description": "New health value"
                    }
                },
                "required": ["npc_id", "health"]
            }
        )
    ]


async def handle_s1_get_npc(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """Handle s1_get_npc tool call."""
    npc_id = arguments.get("npc_id")
    if not npc_id:
        return [TextContent(type="text", text="Error: npc_id is required")]
    
    try:
        response = tcp_client.call_with_retry("get_npc", {"npc_id": npc_id})
        
        if response.error:
            return [TextContent(
                type="text",
                text=f"Error: {response.error.message} (code: {response.error.code})"
            )]
        
        import json
        return [TextContent(type="text", text=json.dumps(response.result, indent=2))]
    except Exception as e:
        logger.error(f"Error in s1_get_npc: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]


async def handle_s1_list_npcs(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """Handle s1_list_npcs tool call."""
    filter_value = arguments.get("filter")
    params = {}
    if filter_value:
        params["filter"] = filter_value
    
    try:
        response = tcp_client.call_with_retry("list_npcs", params if params else None)
        
        if response.error:
            return [TextContent(
                type="text",
                text=f"Error: {response.error.message} (code: {response.error.code})"
            )]
        
        import json
        return [TextContent(type="text", text=json.dumps(response.result, indent=2))]
    except Exception as e:
        logger.error(f"Error in s1_list_npcs: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]


async def handle_s1_get_npc_position(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """Handle s1_get_npc_position tool call."""
    npc_id = arguments.get("npc_id")
    if not npc_id:
        return [TextContent(type="text", text="Error: npc_id is required")]
    
    try:
        response = tcp_client.call_with_retry("get_npc_position", {"npc_id": npc_id})
        
        if response.error:
            return [TextContent(
                type="text",
                text=f"Error: {response.error.message} (code: {response.error.code})"
            )]
        
        import json
        return [TextContent(type="text", text=json.dumps(response.result, indent=2))]
    except Exception as e:
        logger.error(f"Error in s1_get_npc_position: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]


async def handle_s1_teleport_npc(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """Handle s1_teleport_npc tool call."""
    npc_id = arguments.get("npc_id")
    position = arguments.get("position")
    
    if not npc_id:
        return [TextContent(type="text", text="Error: npc_id is required")]
    if not position:
        return [TextContent(type="text", text="Error: position is required")]
    
    try:
        response = tcp_client.call_with_retry("teleport_npc", {
            "npc_id": npc_id,
            "position": position
        })
        
        if response.error:
            return [TextContent(
                type="text",
                text=f"Error: {response.error.message} (code: {response.error.code})"
            )]
        
        import json
        return [TextContent(type="text", text=json.dumps(response.result, indent=2))]
    except Exception as e:
        logger.error(f"Error in s1_teleport_npc: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]


async def handle_s1_set_npc_health(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """Handle s1_set_npc_health tool call."""
    npc_id = arguments.get("npc_id")
    health = arguments.get("health")
    
    if not npc_id:
        return [TextContent(type="text", text="Error: npc_id is required")]
    if health is None:
        return [TextContent(type="text", text="Error: health is required")]
    
    try:
        response = tcp_client.call_with_retry("set_npc_health", {
            "npc_id": npc_id,
            "health": health
        })
        
        if response.error:
            return [TextContent(
                type="text",
                text=f"Error: {response.error.message} (code: {response.error.code})"
            )]
        
        import json
        return [TextContent(type="text", text=json.dumps(response.result, indent=2))]
    except Exception as e:
        logger.error(f"Error in s1_set_npc_health: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]


# Tool handler mapping
TOOL_HANDLERS = {
    "s1_get_npc": handle_s1_get_npc,
    "s1_list_npcs": handle_s1_list_npcs,
    "s1_get_npc_position": handle_s1_get_npc_position,
    "s1_teleport_npc": handle_s1_teleport_npc,
    "s1_set_npc_health": handle_s1_set_npc_health,
}

