# S1MCPClient

MCP (Model Context Protocol) server for Schedule I game mod integration. This Python-based server bridges LLM agents with the S1MCPServer mod, enabling agentic debugging and game state inspection.

## Overview

S1MCPClient is the external MCP server component that:
- Connects to the S1MCPServer mod via Windows Named Pipes
- Implements the MCP protocol using the official SDK
- Exposes game operations as MCP tools for LLM agents
- Handles JSON-RPC communication with the mod

## Architecture

```
[LLM Agent] → [S1MCPClient] → [Named Pipe] → [S1MCPServer Mod] → [Schedule I Game]
```

## Requirements

- Python 3.10 or higher
- Windows (Named Pipes are Windows-only)
- S1MCPServer mod installed and running in Schedule I

## Installation

1. Clone or download this repository
2. Install dependencies:
   ```bash
   pip install -r requirements.txt
   ```

## Configuration

Optional configuration file (`config.json` in project root):

```json
{
  "pipe_name": "S1MCPServer",
  "log_level": "INFO",
  "connection_timeout": 5.0,
  "reconnect_delay": 1.0
}
```

### Configuration Options

- `pipe_name`: Named pipe name to connect to (default: "S1MCPServer")
- `log_level`: Logging level - DEBUG, INFO, WARNING, ERROR (default: "INFO")
- `connection_timeout`: Connection timeout in seconds (default: 5.0)
- `reconnect_delay`: Delay before reconnection attempts in seconds (default: 1.0)

## Usage

### Running the Server

The server communicates via stdio (standard input/output) as required by the MCP protocol:

```bash
python -m src.main
```

Or if installed as a package:

```bash
s1mcpclient
```

### Integration with MCP Clients

Configure your MCP client (e.g., Claude Desktop, Cline) to use this server:

**Claude Desktop** (`claude_desktop_config.json`):
```json
{
  "mcpServers": {
    "s1mcpclient": {
      "command": "python",
      "args": ["-m", "src.main"],
      "cwd": "C:\\path\\to\\S1MCPClient"
    }
  }
}
```

## Available Tools

### NPC Tools

#### `s1_get_npc`
Get detailed information about a specific NPC by ID.

**Parameters:**
- `npc_id` (string, required): The unique identifier of the NPC

**Example:**
```json
{
  "npc_id": "kyle_cooley"
}
```

#### `s1_list_npcs`
List all NPCs in the game, optionally filtered by state.

**Parameters:**
- `filter` (string, optional): Filter by state - "conscious", "unconscious", "in_building", "in_vehicle"

**Example:**
```json
{
  "filter": "conscious"
}
```

#### `s1_get_npc_position`
Get the current position of an NPC.

**Parameters:**
- `npc_id` (string, required): The unique identifier of the NPC

#### `s1_teleport_npc`
Teleport an NPC to a specific position.

**Parameters:**
- `npc_id` (string, required): The unique identifier of the NPC
- `position` (object, required): Target position with x, y, z coordinates

**Example:**
```json
{
  "npc_id": "kyle_cooley",
  "position": {"x": 0.0, "y": 1.0, "z": 0.0}
}
```

#### `s1_set_npc_health`
Modify an NPC's health value.

**Parameters:**
- `npc_id` (string, required): The unique identifier of the NPC
- `health` (number, required): New health value

### Player Tools

#### `s1_get_player`
Get current player information including position, health, money, and network status.

**Parameters:** None

#### `s1_get_player_inventory`
Get the player's inventory items.

**Parameters:** None

#### `s1_teleport_player`
Teleport the player to a specific position.

**Parameters:**
- `position` (object, required): Target position with x, y, z coordinates

#### `s1_add_item_to_player`
Add item(s) to the player's inventory.

**Parameters:**
- `item_id` (string, required): The unique identifier of the item
- `quantity` (number, optional): Number of items to add (default: 1)

### Item Tools

#### `s1_list_items`
List all item definitions in the game, optionally filtered by category.

**Parameters:**
- `category` (string, optional): Filter by item category

#### `s1_get_item`
Get detailed information about an item definition by ID.

**Parameters:**
- `item_id` (string, required): The unique identifier of the item

#### `s1_spawn_item`
Spawn an item in the world at a specific position.

**Parameters:**
- `item_id` (string, required): The unique identifier of the item
- `position` (object, required): Spawn position with x, y, z coordinates
- `quantity` (number, optional): Number of items to spawn (default: 1)

### Building Tools

#### `s1_list_buildings`
List all buildings in the game.

**Parameters:** None

#### `s1_get_building`
Get detailed information about a building by ID or name.

**Parameters:**
- `building_id` (string, optional): The unique identifier of the building
- `building_name` (string, optional): The name of the building (alternative to building_id)

**Note:** Either `building_id` or `building_name` must be provided.

### Vehicle Tools

#### `s1_list_vehicles`
List all vehicles in the game.

**Parameters:** None

#### `s1_get_vehicle`
Get detailed information about a vehicle by ID.

**Parameters:**
- `vehicle_id` (string, required): The unique identifier of the vehicle

### Game State Tools

#### `s1_get_game_state`
Get current game state information including scene, game time, network status, game version, and loaded mods.

**Parameters:** None

### Log Tools

#### `s1_capture_logs`
Capture and filter game logs from MelonLoader for debugging. Retrieves logs from the game's Latest.log file with optional filtering by keywords, timestamps, regex patterns, and line count limits. Essential for agentic debugging to diagnose issues, track errors, and understand game behavior.

**Parameters:**
- `last_n_lines` (integer, optional): Get the last N lines from the log file. Cannot be used with first_n_lines.
- `first_n_lines` (integer, optional): Get the first N lines from the log file. Cannot be used with last_n_lines.
- `keyword` (string, optional): Filter logs by keyword (case-insensitive search). Returns only lines containing this keyword.
- `from_timestamp` (string, optional): Filter logs from this timestamp onwards. Format: HH:mm:ss or HH:mm:ss.fff (e.g., '12:30:45' or '12:30:45.123')
- `to_timestamp` (string, optional): Filter logs up to this timestamp. Format: HH:mm:ss or HH:mm:ss.fff (e.g., '12:35:00' or '12:35:00.999')
- `include_pattern` (string, optional): Regex pattern to include matching lines (case-insensitive). Only lines matching this pattern will be returned.
- `exclude_pattern` (string, optional): Regex pattern to exclude matching lines (case-insensitive). Lines matching this pattern will be filtered out.

**Example:**
```json
{
  "last_n_lines": 100,
  "keyword": "error"
}
```

**Common Use Cases:**
- Find all recent errors: `{"keyword": "error", "last_n_lines": 50}`
- Debug specific time window: `{"from_timestamp": "12:00:00", "to_timestamp": "12:05:00", "keyword": "S1MCPServer"}`
- Filter by pattern: `{"include_pattern": "ERROR|WARN", "exclude_pattern": "DEBUG", "last_n_lines": 200}`

### Debug Tools

#### `s1_inspect_object`
Inspect a Unity GameObject or component using reflection. Useful for debugging and discovering game object properties.

**Parameters:**
- `object_name` (string, required): The name of the GameObject to inspect
- `object_type` (string, optional): The type of object to inspect (default: "GameObject")

## Protocol

The server communicates with the mod using JSON-RPC 2.0 over Windows Named Pipes:

**Message Format:**
- 4-byte length prefix (little-endian int32)
- UTF-8 encoded JSON payload

**Request Format:**
```json
{
  "id": 1,
  "method": "get_npc",
  "params": {
    "npc_id": "kyle_cooley"
  }
}
```

**Response Format:**
```json
{
  "id": 1,
  "result": {
    "npc_id": "kyle_cooley",
    "name": "Kyle Cooley",
    "position": {"x": 10.5, "y": 1.0, "z": 20.3}
  },
  "error": null
}
```

## Troubleshooting

### Connection Issues

**Problem:** Cannot connect to named pipe

**Solutions:**
- Ensure the S1MCPServer mod is loaded and running in Schedule I
- Check that the game has loaded the Main scene (mod waits for Main scene)
- Verify the pipe name matches in both mod and client configuration
- Check Windows permissions for named pipe access

**Problem:** Connection lost during operation

**Solutions:**
- The client automatically retries on connection errors
- Check mod logs for errors
- Ensure the game hasn't crashed or been closed

### Tool Call Errors

**Problem:** Tool returns error from mod

**Solutions:**
- Check the error message and code in the response
- Verify parameters match the API specification
- Check mod logs for detailed error information
- Ensure game objects exist (e.g., NPC ID is valid)

### Logging

Set log level to DEBUG in `config.json` for detailed logging:

```json
{
  "log_level": "DEBUG"
}
```

Logs are written to stderr (standard error output).

## Development

### Project Structure

```
S1MCPClient/
├── src/
│   ├── main.py              # Entry point and MCP server setup
│   ├── pipe_client.py       # Named pipe client
│   ├── protocol.py          # JSON-RPC protocol handling
│   ├── tools/               # MCP tool definitions
│   ├── models/              # Data models
│   └── utils/               # Utilities (logger, config)
├── tests/                   # Unit tests
├── requirements.txt         # Python dependencies
└── README.md                # This file
```

### Adding New Tools

1. Create a new tool file in `src/tools/` or add to existing file
2. Define tool schema using `Tool` class from `mcp.types`
3. Implement handler function with signature: `async def handle_tool_name(arguments: Dict[str, Any], pipe_client: NamedPipeClient) -> list[TextContent]`
4. Add tool to `TOOL_HANDLERS` dictionary
5. Register tool in `main.py` by importing and adding to tool collection

### Testing

Run unit tests (when implemented):

```bash
python -m pytest tests/
```

## Dependencies

- `mcp>=0.9.0`: Official MCP SDK for Python
- `pywin32>=306`: Windows Named Pipes support
- `pydantic>=2.0.0`: Data validation and models

## License

[Add your license here]

## Credits

- Built for Schedule I game modding community
- Uses official Model Context Protocol SDK
- Integrates with S1MCPServer mod

## Related Projects

- **S1MCPServer**: The MelonLoader mod that this client connects to
- **Schedule I**: The Unity game being modded

## Support

For issues and questions:
- Check mod logs in Schedule I
- Review API specification in `S1MCPServer/assets/plan/api-specification.md`
- Enable DEBUG logging for detailed information

