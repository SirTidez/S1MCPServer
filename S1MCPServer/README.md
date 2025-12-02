# S1MCPServer

A MelonLoader mod for Schedule I that enables agentic LLM access to game objects and scripting through a Model Context Protocol (MCP) server. This mod provides real-time inspection and manipulation capabilities for development and troubleshooting.

## Features

- **Named Pipe Server**: Efficient Windows IPC communication with external MCP server
- **Thread-Safe Command Queue**: Marshals operations between background and main threads
- **Game Object Access**: Read/write access to NPCs, Player, Items, Buildings, Vehicles
- **Reflection Utilities**: Cross-backend GameObject inspection (Mono/IL2CPP)
- **JSON-RPC Protocol**: Standard protocol for command/response communication
- **Cross-Backend Support**: Works with both IL2CPP and Mono builds

## Architecture

The mod implements a Named Pipe server that communicates with an external MCP server:

```
[LLM Agent] → [MCP Server] → [Named Pipe] → [S1MCPServer Mod] → [Schedule I Game]
```

### Components

- **NamedPipeServer**: Background thread listener for pipe connections
- **CommandQueue**: Thread-safe queue for incoming commands
- **ResponseQueue**: Thread-safe queue for outgoing responses
- **CommandRouter**: Routes commands to appropriate handlers
- **Command Handlers**: Process game operations (NPC, Player, Item, Building, Vehicle, Debug)

## Installation

1. Build the mod for your target backend (IL2CPP or Mono)
2. Place the compiled `.dll` in your Schedule I `Mods` folder
3. Start the game - the named pipe server will start automatically
4. Connect your MCP server to `\\.\pipe\S1MCPServer`

## Usage

### Starting the Server

The mod automatically starts the named pipe server when initialized. You'll see a log message:
```
S1MCPServer ready - waiting for Main scene
```

Once the Main scene loads:
```
Main scene loaded - S1MCPServer is active
```

### Available Commands

#### NPC Commands
- `get_npc` - Get NPC information by ID
- `list_npcs` - List all NPCs (optional filter)
- `get_npc_position` - Get NPC's current position
- `teleport_npc` - Teleport NPC to position
- `set_npc_health` - Modify NPC health

#### Player Commands
- `get_player` - Get player information
- `get_player_inventory` - Get player's inventory
- `teleport_player` - Teleport player to position
- `add_item_to_player` - Add item to player inventory

#### Item Commands
- `list_items` - List all item definitions
- `get_item` - Get item information by ID
- `spawn_item` - Spawn item in world at position

#### Building Commands
- `list_buildings` - List all buildings
- `get_building` - Get building information

#### Vehicle Commands
- `list_vehicles` - List all vehicles
- `get_vehicle` - Get vehicle information

#### Game State Commands
- `get_game_state` - Get current game state (scene, network status, mods)

#### Log Commands
- `capture_logs` - Capture and filter game logs for debugging
  - Supports keyword search, timestamp filtering, regex patterns
  - Line count limiting (first/last N lines)
  - Reads from MelonLoader's Latest.log file

#### Debug Commands
- `inspect_object` - Inspect Unity GameObject using reflection

### Protocol

The mod uses JSON-RPC 2.0-like protocol over Windows Named Pipes:

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
    "position": {"x": 10.5, "y": 1.0, "z": 20.3},
    "health": 100.0,
    "is_conscious": true
  },
  "error": null
}
```

## Development

### Building

The project supports both Mono and IL2CPP builds:
- **Debug Mono**: Builds for Mono backend with debug symbols
- **Release Mono**: Release build for Mono
- **Debug IL2CPP**: Builds for IL2CPP backend with debug symbols
- **Release IL2CPP**: Release build for IL2CPP

### Project Structure

```
S1MCPServer/
├── MainMod.cs              # Main mod entry point
├── Models/                 # Data models (Request, Response, etc.)
├── Core/                   # Core systems (queues, protocol, router)
├── Server/                 # Named Pipe server
├── Handlers/               # Command handlers
├── Utils/                  # Reflection utilities
└── Helpers/                # Helper utilities (existing)
```

## Dependencies

- MelonLoader
- **No S1API dependency** - Custom implementation based on S1API patterns as reference
- System.IO.Pipes (Named Pipes)
- System.Text.Json (JSON serialization)
- System.Collections.Concurrent (Thread-safe queues)
- Il2CppInterop (for IL2CPP support)
- Direct access to Schedule One native classes (ScheduleOne.* / Il2CppScheduleOne.*)

## MCP Server Integration

To use this mod with an MCP server:

1. Create an MCP server that connects to `\\.\pipe\S1MCPServer`
2. Implement the JSON-RPC protocol (see API specification)
3. Translate MCP tools to game API calls
4. Handle responses and return to LLM

See `assets/plan/api-specification.md` for complete API documentation.

## Companion Tools

- **UniverseLib**: Recommended dependency for reflection utilities (https://github.com/yukieiji/UniverseLib)
- **UnityExplorer**: Optional companion tool for manual GameObject inspection (https://github.com/yukieiji/UnityExplorer/). Can optionally use UnityExplorer's `InspectorManager` API if UnityExplorer is loaded.

## Troubleshooting

### Server Not Starting
- Check MelonLoader logs for initialization errors
- Ensure you're on Windows (Named Pipes are Windows-only)
- Verify the mod loaded correctly

### Commands Not Working
- Check that the Main scene has loaded
- Verify player/game objects are ready
- Check logs for error messages

### Connection Issues
- Ensure only one MCP server is connected at a time
- Check that the pipe name matches: `\\.\pipe\S1MCPServer`
- Verify the MCP server is using the correct protocol format

## License

[Add your license here]

## Credits

- Built on the Schedule I MelonLoader Mod Template
- Custom implementation based on S1API patterns (no S1API dependency)
- Inspired by UnityExplorer's reflection techniques
- Uses native Schedule One game classes directly
