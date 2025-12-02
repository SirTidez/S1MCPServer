"""Log capture MCP tools."""

from typing import Any, Dict
from mcp.types import Tool, TextContent

from ..tcp_client import TcpClient
from ..utils.logger import get_logger


logger = get_logger()


def get_log_tools(tcp_client: TcpClient) -> list[Tool]:
    """
    Get all Log Capture MCP tools.
    
    Args:
        tcp_client: TCP client instance
    
    Returns:
        List of MCP Tool definitions
    """
    return [
        Tool(
            name="s1_capture_logs",
            description=(
                "Capture and filter game logs from MelonLoader for debugging. "
                "Retrieves logs from the game's Latest.log file with optional filtering by keywords, "
                "timestamps, regex patterns, and line count limits. Useful for agentic debugging to "
                "diagnose issues, track errors, and understand game behavior."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "last_n_lines": {
                        "type": "integer",
                        "description": "Get the last N lines from the log file. Cannot be used with first_n_lines.",
                        "minimum": 1
                    },
                    "first_n_lines": {
                        "type": "integer",
                        "description": "Get the first N lines from the log file. Cannot be used with last_n_lines.",
                        "minimum": 1
                    },
                    "keyword": {
                        "type": "string",
                        "description": "Filter logs by keyword (case-insensitive search). Returns only lines containing this keyword."
                    },
                    "from_timestamp": {
                        "type": "string",
                        "description": "Filter logs from this timestamp onwards. Format: HH:mm:ss or HH:mm:ss.fff (e.g., '12:30:45' or '12:30:45.123')"
                    },
                    "to_timestamp": {
                        "type": "string",
                        "description": "Filter logs up to this timestamp. Format: HH:mm:ss or HH:mm:ss.fff (e.g., '12:35:00' or '12:35:00.999')"
                    },
                    "include_pattern": {
                        "type": "string",
                        "description": "Regex pattern to include matching lines (case-insensitive). Only lines matching this pattern will be returned."
                    },
                    "exclude_pattern": {
                        "type": "string",
                        "description": "Regex pattern to exclude matching lines (case-insensitive). Lines matching this pattern will be filtered out."
                    }
                },
                "required": []
            }
        )
    ]


async def handle_s1_capture_logs(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """Handle s1_capture_logs tool call."""
    try:
        # Build parameters from arguments
        params = {}
        
        if "last_n_lines" in arguments:
            params["last_n_lines"] = arguments["last_n_lines"]
        
        if "first_n_lines" in arguments:
            params["first_n_lines"] = arguments["first_n_lines"]
        
        if "keyword" in arguments:
            params["keyword"] = arguments["keyword"]
        
        if "from_timestamp" in arguments:
            params["from_timestamp"] = arguments["from_timestamp"]
        
        if "to_timestamp" in arguments:
            params["to_timestamp"] = arguments["to_timestamp"]
        
        if "include_pattern" in arguments:
            params["include_pattern"] = arguments["include_pattern"]
        
        if "exclude_pattern" in arguments:
            params["exclude_pattern"] = arguments["exclude_pattern"]
        
        logger.debug(f"Capturing logs with params: {params}")
        
        response = tcp_client.call_with_retry("capture_logs", params)
        
        if response.error:
            return [TextContent(
                type="text",
                text=f"Error: {response.error.message} (code: {response.error.code})"
            )]
        
        # Format the response for better readability
        result = response.result
        
        if not isinstance(result, dict):
            return [TextContent(type="text", text=str(result))]
        
        lines = result.get("lines", [])
        total_lines = result.get("total_lines_in_file", 0)
        filtered_count = result.get("filtered_count", 0)
        filters_applied = result.get("filters_applied", [])
        warning = result.get("warning")
        
        # Build formatted output
        output_lines = []
        
        if warning:
            output_lines.append(f"⚠️ Warning: {warning}\n")
        
        output_lines.append(f"📊 Log Summary:")
        output_lines.append(f"  • Total lines in file: {total_lines}")
        output_lines.append(f"  • Lines after filtering: {filtered_count}")
        output_lines.append(f"  • Lines returned: {len(lines)}")
        
        if filters_applied:
            output_lines.append(f"\n🔍 Filters Applied:")
            for filter_desc in filters_applied:
                output_lines.append(f"  • {filter_desc}")
        
        if lines:
            output_lines.append(f"\n📝 Log Lines:\n")
            for line_obj in lines:
                line_num = line_obj.get("line_number", "?")
                timestamp = line_obj.get("timestamp", "")
                content = line_obj.get("content", "")
                
                if timestamp:
                    output_lines.append(f"[Line {line_num}] [{timestamp}] {content}")
                else:
                    output_lines.append(f"[Line {line_num}] {content}")
        else:
            output_lines.append(f"\n(No log lines matched the filters)")
        
        return [TextContent(type="text", text="\n".join(output_lines))]
        
    except Exception as e:
        logger.error(f"Error in s1_capture_logs: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]


# Tool handler mapping
TOOL_HANDLERS = {
    "s1_capture_logs": handle_s1_capture_logs,
}

