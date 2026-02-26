"""Game tool — lifecycle management and documentation search."""

from typing import Any, Dict
from mcp.types import Tool, TextContent

from ..tcp_client import TcpClient
from ..utils.config import Config
from ..utils.logger import get_logger
from .game_lifecycle_tools import (
    handle_s1_launch_game, handle_s1_close_game, handle_s1_get_game_process_info
)
from .s1api_docs_tools import handle_s1_search_s1api_docs

logger = get_logger()

TOOL_NAME = "s1_game"

TOOL = Tool(
    name=TOOL_NAME,
    description=(
        "Manage the game process and search documentation. Choose an action:\n"
        "- launch: Launch the game (requires: version = il2cpp|mono; optional: enable_debugger, wait_for_connection)\n"
        "- close: Forcefully close the running game\n"
        "- process_info: Get game process status and resource usage\n"
        "- search_docs: Search S1API documentation (requires: topic; optional: tokens)"
    ),
    inputSchema={
        "type": "object",
        "properties": {
            "action": {
                "type": "string",
                "enum": ["launch", "close", "process_info", "search_docs"],
                "description": "Operation to perform"
            },
            "version": {
                "type": "string",
                "enum": ["il2cpp", "mono"],
                "description": "Game version to launch (launch only)"
            },
            "enable_debugger": {
                "type": "boolean",
                "description": "Enable MelonLoader debugger (launch only)",
                "default": False
            },
            "wait_for_connection": {
                "type": "boolean",
                "description": "Wait for mod server connection after launch",
                "default": True
            },
            "topic": {
                "type": "string",
                "description": "Documentation search topic (search_docs only)"
            },
            "tokens": {
                "type": "integer",
                "description": "Max tokens to retrieve (search_docs, default: 5000)",
                "default": 5000
            }
        },
        "required": ["action"]
    }
)

# TOOL_HANDLERS is populated by get_tools() with a config-capturing closure.
TOOL_HANDLERS: Dict[str, Any] = {}


def get_tools(_tcp_client: TcpClient, config: Config) -> list[Tool]:
    """Register tools, capturing config in a closure so no module-level global is needed."""

    async def handle(arguments: Dict[str, Any], tcp_client_: TcpClient) -> list[TextContent]:
        action = arguments.get("action")
        try:
            if action == "launch":
                return await handle_s1_launch_game(arguments, tcp_client_, config)
            elif action == "close":
                return await handle_s1_close_game(arguments, tcp_client_, config)
            elif action == "process_info":
                return await handle_s1_get_game_process_info(arguments, tcp_client_, config)
            elif action == "search_docs":
                if not arguments.get("topic"):
                    return [TextContent(type="text", text="Error: topic is required for search_docs")]
                return await handle_s1_search_s1api_docs(arguments, tcp_client_)
            else:
                return [TextContent(type="text", text=f"Error: Unknown action '{action}'")]
        except Exception:
            logger.exception(f"Error in s1_game/{action}")
            return [TextContent(type="text", text="An internal error occurred while processing your request.")]

    TOOL_HANDLERS[TOOL_NAME] = handle
    return [TOOL]
