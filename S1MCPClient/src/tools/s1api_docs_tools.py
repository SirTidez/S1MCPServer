"""S1API documentation search tools using Context7."""

from typing import Any, Dict
from urllib.parse import quote_plus
from mcp.types import Tool, TextContent
import httpx

from ..tcp_client import TcpClient
from ..utils.logger import get_logger


logger = get_logger()

# Base URL for Context7 S1API documentation
CONTEXT7_BASE_URL = "https://context7.com/ifbars/s1api/llms.txt"


def get_s1api_docs_tools(tcp_client: TcpClient) -> list[Tool]:
    """
    Get all S1API documentation search MCP tools.
    
    Args:
        tcp_client: TCP client instance (unused, kept for consistency)
    
    Returns:
        List of MCP Tool definitions
    """
    return [
        Tool(
            name="s1_search_s1api_docs",
            description="Search S1API documentation using Context7's llms.txt endpoint. Retrieves relevant documentation snippets for a given topic.",
            inputSchema={
                "type": "object",
                "properties": {
                    "topic": {
                        "type": "string",
                        "description": "Search topic/keyword (e.g., 'Phone App Creation', 'NPC Creation', 'Quest System')"
                    },
                    "tokens": {
                        "type": "integer",
                        "description": "Maximum tokens to retrieve (default: 5000)",
                        "default": 5000
                    }
                },
                "required": ["topic"]
            }
        )
    ]


async def handle_s1_search_s1api_docs(arguments: Dict[str, Any], tcp_client: TcpClient) -> list[TextContent]:
    """
    Handle s1_search_s1api_docs tool call.
    
    Args:
        arguments: Tool arguments containing 'topic' and optionally 'tokens'
        tcp_client: TCP client instance (unused, kept for consistency)
    
    Returns:
        List of TextContent with documentation results
    """
    topic = arguments.get("topic")
    tokens = arguments.get("tokens", 5000)
    
    if not topic:
        return [TextContent(
            type="text",
            text="Error: topic parameter is required"
        )]
    
    # Validate tokens parameter
    try:
        tokens = int(tokens)
        if tokens < 1:
            return [TextContent(
                type="text",
                text="Error: tokens must be a positive integer"
            )]
    except (ValueError, TypeError):
        return [TextContent(
            type="text",
            text="Error: tokens must be a valid integer"
        )]
    
    # Construct URL with URL-encoded topic
    encoded_topic = quote_plus(topic)
    url = f"{CONTEXT7_BASE_URL}?topic={encoded_topic}&tokens={tokens}"
    
    logger.debug(f"Searching S1API docs for topic: '{topic}' (tokens: {tokens})")
    logger.debug(f"Request URL: {url}")
    
    try:
        # Make async HTTP GET request
        async with httpx.AsyncClient(timeout=30.0) as client:
            response = await client.get(url)
            response.raise_for_status()
            
            # Get the documentation content
            content = response.text
            
            logger.debug(f"Retrieved {len(content)} characters of documentation")
            
            if not content.strip():
                return [TextContent(
                    type="text",
                    text=f"No documentation found for topic: '{topic}'. Try a different search term."
                )]
            
            return [TextContent(
                type="text",
                text=content
            )]
            
    except httpx.TimeoutException:
        logger.error(f"Timeout while fetching S1API documentation for topic: '{topic}'")
        return [TextContent(
            type="text",
            text=f"Error: Request timed out while fetching documentation for topic '{topic}'. Please try again."
        )]
    except httpx.HTTPStatusError as e:
        logger.error(f"HTTP error {e.response.status_code} while fetching S1API documentation: {e}")
        return [TextContent(
            type="text",
            text=f"Error: HTTP {e.response.status_code} - Failed to fetch documentation. Please try again later."
        )]
    except httpx.RequestError as e:
        logger.error(f"Request error while fetching S1API documentation: {e}")
        return [TextContent(
            type="text",
            text=f"Error: Network error while fetching documentation - {str(e)}. Please check your internet connection."
        )]
    except Exception as e:
        logger.error(f"Unexpected error in s1_search_s1api_docs: {e}", exc_info=True)
        return [TextContent(
            type="text",
            text=f"Error: Unexpected error while fetching documentation - {str(e)}"
        )]


# Tool handler mapping
TOOL_HANDLERS = {
    "s1_search_s1api_docs": handle_s1_search_s1api_docs,
}

