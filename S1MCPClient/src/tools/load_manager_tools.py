"""LoadManager MCP tools for save game management."""

from typing import Any, Dict
from mcp.types import Tool, TextContent

from ..tcp_client import TcpClient
from ..utils.logger import get_logger


logger = get_logger()


def get_load_manager_tools(tcp_client: TcpClient) -> list[Tool]:
    """
    Get all LoadManager MCP tools.
    
    Args:
        tcp_client: TCP client instance
    
    Returns:
        List of MCP Tool definitions
    """
    return [
        Tool(
            name="s1_list_saves",
            description="List all available save games with their properties. Returns an array of save game objects with all available metadata.",
            inputSchema={
                "type": "object",
                "properties": {},
                "required": []
            }
        ),
        Tool(
            name="s1_load_save",
            description=(
                "Load a specific save game by slot index. If a save is already loaded (not in menu scene), "
                "this will automatically return to the menu first before loading the requested save. "
                "Note: Slot indices are 0-based (use 0 for first save, 1 for second, etc.)."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "slot_index": {
                        "type": "integer",
                        "description": "The 0-based index of the save slot to load",
                        "minimum": 0
                    }
                },
                "required": ["slot_index"]
            }
        )
    ]


async def handle_s1_list_saves(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """Handle s1_list_saves tool call."""
    try:
        response = tcp_client.call_with_retry("list_saves", {})
        
        if response.error:
            return [TextContent(
                type="text",
                text=f"Error: {response.error.message} (code: {response.error.code})"
            )]
        
        import json
        result_text = json.dumps(response.result, indent=2)
        
        # Add helpful summary
        if isinstance(response.result, dict) and "saves" in response.result:
            save_count = response.result.get("count", 0)
            summary = f"Found {save_count} save game(s):\n\n{result_text}"
            return [TextContent(type="text", text=summary)]
        
        return [TextContent(type="text", text=result_text)]
    except Exception as e:
        logger.error(f"Error in s1_list_saves: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]


async def handle_s1_load_save(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """Handle s1_load_save tool call."""
    try:
        slot_index = arguments.get("slot_index")
        
        if slot_index is None:
            return [TextContent(
                type="text",
                text="Error: slot_index parameter is required"
            )]
        
        response = tcp_client.call_with_retry("load_save", {"slot_index": slot_index})
        
        if response.error:
            return [TextContent(
                type="text",
                text=f"Error: {response.error.message} (code: {response.error.code})"
            )]
        
        # Format success message
        if isinstance(response.result, dict):
            success = response.result.get("success", False)
            message = response.result.get("message", "")
            returned_to_menu = response.result.get("returned_to_menu", False)
            
            status_text = f"✓ {message}"
            if returned_to_menu:
                status_text += "\n  Note: Game was returned to menu before loading the save."
            
            return [TextContent(type="text", text=status_text)]
        
        import json
        return [TextContent(type="text", text=json.dumps(response.result, indent=2))]
    except Exception as e:
        logger.error(f"Error in s1_load_save: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]


# Tool handler mapping
TOOL_HANDLERS = {
    "s1_list_saves": handle_s1_list_saves,
    "s1_load_save": handle_s1_load_save,
}

