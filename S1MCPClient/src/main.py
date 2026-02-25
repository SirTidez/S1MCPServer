"""Main entry point for S1MCPClient MCP server."""

import asyncio
import sys
from mcp.server import Server
from mcp.server.models import InitializationOptions
from mcp.server.stdio import stdio_server
from mcp.types import Tool, ServerCapabilities, ToolsCapability, Prompt

from .tcp_client import TcpClient, TcpConnectionError
from .utils.config import Config
from .utils.logger import setup_logger, get_logger

# Consolidated tool modules (6 tools replacing the previous 47)
from .tools import player as player_tools
from .tools import npc as npc_tools
from .tools import item as item_tools
from .tools import world as world_tools
from .tools import inspect as inspect_tools
from .tools import game as game_tools


# Global TCP client instance
tcp_client: TcpClient | None = None
# Store instructions from handshake
server_instructions: str | None = None
# Track connection state
is_connected: bool = False
logger = get_logger()


# Tools that don't require an active game connection
LIFECYCLE_TOOLS = {"s1_game"}


def can_call_tool(tool_name: str) -> tuple[bool, str]:
    """
    Check if a tool can be called based on connection state.
    
    Args:
        tool_name: Name of the tool to check
    
    Returns:
        Tuple of (can_call, error_message)
    """
    global is_connected
    
    # Lifecycle tools can always be called
    if tool_name in LIFECYCLE_TOOLS:
        return True, ""
    
    # All other tools require game connection
    if not is_connected:
        return False, (
            "Error: Game is not connected. Please launch the game first using s1_launch_game.\n"
            "Once the game is running and connected, you can use other game tools."
        )
    
    return True, ""


def create_server(config: Config, tcp_client: TcpClient) -> Server:
    """
    Create and configure the MCP server.
    
    Args:
        config: Configuration instance
        tcp_client: TCP client instance
    
    Returns:
        Configured MCP server
    """
    server = Server("s1mcpclient")
    
    # Collect all tools
    all_tools: list[Tool] = []
    all_tool_handlers: dict[str, callable] = {}
    
    tool_modules = [
        ("player",  lambda: player_tools.get_tools(tcp_client),          player_tools.TOOL_HANDLERS),
        ("npc",     lambda: npc_tools.get_tools(tcp_client),              npc_tools.TOOL_HANDLERS),
        ("item",    lambda: item_tools.get_tools(tcp_client),             item_tools.TOOL_HANDLERS),
        ("world",   lambda: world_tools.get_tools(tcp_client),            world_tools.TOOL_HANDLERS),
        ("inspect", lambda: inspect_tools.get_tools(tcp_client),          inspect_tools.TOOL_HANDLERS),
        ("game",    lambda: game_tools.get_tools(tcp_client, config),     game_tools.TOOL_HANDLERS),
    ]

    for name, loader, handlers in tool_modules:
        try:
            tools = loader()
            all_tools.extend(tools)
            all_tool_handlers.update(handlers)
            logger.debug(f"Loaded tool: {name}")
        except Exception as e:
            logger.error(f"Error loading {name} tool: {e}", exc_info=True)
    
    # Log tool collection
    logger.info(f"Collected {len(all_tools)} tools: {[tool.name for tool in all_tools]}")
    logger.info(f"Collected {len(all_tool_handlers)} tool handlers: {list(all_tool_handlers.keys())}")
    
    # Register list_tools handler
    @server.list_tools()
    async def handle_list_tools() -> list[Tool]:
        """List all available tools."""
        logger.debug(f"list_tools called, returning {len(all_tools)} tools")
        return all_tools
    
    # Register list_prompts handler (if we want to expose prompts)
    # For now, instructions are passed via InitializationOptions which provides context to the LLM
    # Prompts would be a separate interactive feature
    @server.list_prompts()
    async def handle_list_prompts() -> list[Prompt]:
        """List available prompts."""
        # Currently no prompts - instructions are passed via InitializationOptions
        return []
    
    # Register call_tool handler
    @server.call_tool()
    async def handle_call_tool(name: str, arguments: dict) -> list:
        """
        Handle tool calls.
        
        Args:
            name: Tool name
            arguments: Tool arguments
        
        Returns:
            Tool result
        """
        from mcp.types import TextContent
        
        logger.debug(f"Tool call received: {name} with arguments: {arguments}")
        
        if name not in all_tool_handlers:
            logger.error(f"Unknown tool: {name}")
            logger.debug(f"Available tools: {list(all_tool_handlers.keys())}")
            return [TextContent(
                type="text",
                text=f"Error: Unknown tool '{name}'. Available tools: {', '.join(all_tool_handlers.keys())}"
            )]
        
        # Check if tool can be called based on connection state
        can_call, error_msg = can_call_tool(name)
        if not can_call:
            logger.warning(f"Tool {name} called but game not connected")
            return [TextContent(type="text", text=error_msg)]
        
        handler = all_tool_handlers[name]
        logger.debug(f"Found handler for {name}, invoking...")
        
        try:
            result = await handler(arguments, tcp_client)
            logger.debug(f"Tool {name} completed successfully, result type: {type(result)}")
            return result
        except TcpConnectionError as e:
            logger.error(f"Connection error in tool handler {name}: {e}", exc_info=True)
            return [TextContent(
                type="text",
                text=f"Error: Connection failed - {str(e)}. Please ensure the game is running with the mod loaded."
            )]
        except Exception as e:
            logger.error(f"Error in tool handler {name}: {e}", exc_info=True)
            # Return error as TextContent instead of raising to prevent TaskGroup errors
            return [TextContent(
                type="text",
                text=f"Error executing tool '{name}': {str(e)}"
            )]
    
    return server


async def main():
    """Main entry point."""
    global tcp_client
    
    # Prevent multiple instances - check if we're already running
    import atexit
    import os
    import tempfile
    import platform
    
    pid_file = os.path.join(tempfile.gettempdir(), "s1mcpclient.pid")
    if os.path.exists(pid_file):
        try:
            with open(pid_file, 'r') as f:
                old_pid = int(f.read().strip())
            # Check if the process is still running
            # On Windows, os.kill() doesn't support signal 0, so we use a different approach
            process_exists = False
            try:
                if platform.system() == "Windows":
                    # On Windows, try to open the process to check if it exists
                    import ctypes
                    kernel32 = ctypes.windll.kernel32
                    handle = kernel32.OpenProcess(0x1000, False, old_pid)  # PROCESS_QUERY_INFORMATION
                    if handle and handle != 0:
                        kernel32.CloseHandle(handle)
                        process_exists = True
                    # If handle is 0, the process doesn't exist or we don't have permission
                else:
                    # On Unix, use signal 0
                    os.kill(old_pid, 0)
                    process_exists = True
            except (OSError, ProcessLookupError, ValueError, AttributeError):
                # Process doesn't exist or error accessing it
                process_exists = False
            
            if process_exists:
                logger.warning(f"Another instance appears to be running (PID: {old_pid}). Continuing anyway...")
            else:
                # Process doesn't exist, remove stale PID file
                try:
                    os.remove(pid_file)
                except OSError:
                    pass  # Ignore errors removing stale PID file
        except (ValueError, IOError, OSError):
            # Invalid PID file, remove it
            try:
                os.remove(pid_file)
            except OSError:
                pass  # Ignore errors removing invalid PID file
    
    # Write our PID
    try:
        with open(pid_file, 'w') as f:
            f.write(str(os.getpid()))
        
        def cleanup_pid():
            try:
                if os.path.exists(pid_file):
                    os.remove(pid_file)
            except OSError:
                pass  # Ignore errors during cleanup
        
        atexit.register(cleanup_pid)
    except Exception as e:
        logger.debug(f"Could not create PID file: {e}")
    
    # Load configuration
    config = Config.from_file()
    
    # Setup logger
    setup_logger(level=config.log_level)
    logger.info("Starting S1MCPClient MCP server...")
    
    # Initialize TCP client
    try:
        tcp_client = TcpClient(
            host=config.host,
            port=config.port,
            timeout=config.connection_timeout,
            reconnect_delay=config.reconnect_delay
        )
        
        # Try to connect (optional - game might not be running yet)
        global is_connected
        try:
            logger.debug("Attempting initial connection to mod (optional)...")
            tcp_client.connect()
            logger.info("Connected to mod successfully")
            
            # Perform handshake to verify connection and get available methods
            try:
                logger.debug("Performing handshake with mod...")
                handshake_response = tcp_client.call("handshake", {})
                
                if handshake_response.error:
                    logger.warning(f"Handshake failed: {handshake_response.error.message}")
                else:
                    handshake_data = handshake_response.result
                    if isinstance(handshake_data, dict):
                        available_methods = handshake_data.get("available_methods", [])
                        total_methods = handshake_data.get("total_methods", 0)
                        server_name = handshake_data.get("server_name", "Unknown")
                        version = handshake_data.get("version", "Unknown")
                        
                        # Extract instructions for LLM prompt
                        global server_instructions
                        server_instructions = handshake_data.get("instructions")
                        if server_instructions:
                            logger.info(f"Received server instructions for LLM prompt ({len(server_instructions)} characters)")
                            logger.debug(f"Instructions preview: {server_instructions[:200]}...")
                        else:
                            logger.warning("No instructions provided in handshake response")
                        
                        logger.info(f"Handshake successful: {server_name} v{version}")
                        logger.info(f"Available methods: {total_methods}")
                        logger.debug(f"Methods: {', '.join(available_methods)}")
                        
                        # Mark as connected
                        is_connected = True
                        
                        # Log method categories if available
                        if "method_categories" in handshake_data:
                            categories = handshake_data["method_categories"]
                            for category, methods in categories.items():
                                if methods:
                                    logger.debug(f"  {category}: {len(methods)} methods")
                        
                        # Log integrations
                        if "integrations" in handshake_data:
                            integrations = handshake_data["integrations"]
                            logger.debug(f"Integrations: {integrations}")
                    else:
                        logger.warning("Handshake response format unexpected")
            except Exception as e:
                logger.warning(f"Handshake failed: {e}. Connection may still work.")
                logger.debug(f"Handshake error details: {e}", exc_info=True)
                
        except TcpConnectionError as e:
            logger.info(f"Game not running at startup: {e}")
            logger.info("MCP server will wait for game to be launched via s1_launch_game tool.")
            is_connected = False
        
    except Exception as e:
        logger.error(f"Failed to initialize TCP client: {e}")
        sys.exit(1)
    
    # Create server
    try:
        server = create_server(config, tcp_client)
        logger.info("MCP server created, registering tools...")
    except Exception as e:
        logger.error(f"Failed to create server: {e}")
        sys.exit(1)
    
    # Run server with stdio transport
    try:
        logger.info("Starting MCP server with stdio transport...")
        async with stdio_server() as (read_stream, write_stream):
            # Create initialization options with tools capability enabled
            # Use instructions from handshake if available
            logger.info(f"Creating InitializationOptions with instructions: {server_instructions is not None}")
            if server_instructions:
                logger.info(f"Instructions length: {len(server_instructions)} characters")
            init_options = InitializationOptions(
                server_name="s1mcpclient",
                server_version="0.1.0",
                capabilities=ServerCapabilities(
                    tools=ToolsCapability()
                ),
                instructions=server_instructions
            )
            if server_instructions:
                logger.info(f"InitializationOptions created with server-provided instructions ({len(server_instructions)} chars)")
            else:
                logger.warning("InitializationOptions created WITHOUT instructions - handshake may not have completed")
            await server.run(
                read_stream,
                write_stream,
                init_options
            )
    except KeyboardInterrupt:
        logger.info("Received interrupt signal, shutting down...")
    except Exception as e:
        logger.error(f"Server error: {e}", exc_info=True)
        # Don't exit immediately - let finally block clean up
        raise
    finally:
        # Cleanup
        if tcp_client:
            try:
                tcp_client.disconnect()
            except Exception as e:
                logger.warning(f"Error disconnecting TCP client: {e}")
        logger.info("MCP server stopped")


def entry_point():
    """Entry point for the application."""
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        try:
            logger.info("Interrupted by user")
        except:
            pass  # Logger might not be initialized
        sys.exit(0)
    except OSError as e:
        try:
            logger.error(f"Fatal OS error: {type(e).__name__}: {e}", exc_info=True)
        except:
            print(f"Fatal OS error: {type(e).__name__}: {e}", file=sys.stderr)
        sys.exit(1)
    except Exception as e:
        try:
            logger.error(f"Fatal error: {type(e).__name__}: {e}", exc_info=True)
        except:
            print(f"Fatal error: {type(e).__name__}: {e}", file=sys.stderr)
            import traceback
            traceback.print_exc()
        sys.exit(1)


if __name__ == "__main__":
    entry_point()

