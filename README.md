# S1MCP: Model Context Protocol for Schedule I

A comprehensive system enabling LLM agents to interact with the Schedule I game through the Model Context Protocol (MCP). This repository contains both the game mod (S1MCPServer) and the MCP server client (S1MCPClient) that work together to provide agentic debugging and game state inspection capabilities.

## Overview

S1MCP bridges LLM agents (like Claude, GPT) with the Schedule I Unity game, enabling:

- **Real-time game state inspection** - Query NPCs, items, buildings, vehicles, and player data
- **Game object manipulation** - Teleport entities, modify health, spawn items
- **Agentic debugging** - Allow LLMs to diagnose mod issues and suggest fixes
- **Development tooling** - Powerful debugging and development tools for modders

### Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    LLM Agent (Claude/GPT)                     │
│                    (Claude Desktop, Cline, etc.)              │
└────────────────────────────┬──────────────────────────────────┘
                             │ MCP Protocol (JSON-RPC over stdio)
                             │
┌────────────────────────────▼──────────────────────────────────┐
│              S1MCPClient (Python MCP Server)                 │
│  - Handles MCP protocol (JSON-RPC over stdio)                  │
│  - Translates MCP tools → Game API calls                     │
│  - Manages LLM interactions                                  │
│  - Language: Python 3.10+                                    │
└────────────────────────────┬──────────────────────────────────┘
                             │ TCP/IP Communication
                             │ (localhost:8765)
┌────────────────────────────▼──────────────────────────────────┐
│              S1MCPServer (MelonLoader Mod)                   │
│  ┌────────────────────────────────────────────────────────┐  │
│  │  TCP Server (Background Thread)                      │  │
│  │  - Listens for commands from MCP server (port 8765)   │  │
│  │  - Receives JSON-RPC requests                         │  │
│  └──────────────┬───────────────────────────────────────────┘  │
│                 │ Enqueue Commands                            │
│  ┌──────────────▼───────────────────────────────────────────┐  │
│  │  Command Queue (Thread-Safe)                            │  │
│  │  - Queues game operations                               │  │
│  └──────────────┬───────────────────────────────────────────┘  │
│                 │ Process on Main Thread                      │
│  ┌──────────────▼───────────────────────────────────────────┐  │
│  │  Command Handlers (Main Thread)                         │  │
│  │  - Accesses Unity game objects                          │  │
│  │  - Executes game operations safely                      │  │
│  └──────────────┬───────────────────────────────────────────┘  │
│                 │ Results                                     │
│  ┌──────────────▼───────────────────────────────────────────┐  │
│  │  Response Queue (Thread-Safe)                          │  │
│  │  - Queues operation results                            │  │
│  └──────────────┬───────────────────────────────────────────┘  │
│                 │ Send via TCP                                │
│                 └──────────────────────────────────────────────┘
└────────────────────────────┬──────────────────────────────────┘
                             │
┌────────────────────────────▼──────────────────────────────────┐
│              Schedule I Game (Unity)                          │
│  - Game objects (NPCs, Items, Vehicles, Buildings)           │
│  - Player state and inventory                                 │
│  - Network state (multiplayer)                                │
│  - Game systems (native Unity/Schedule One classes)         │
└───────────────────────────────────────────────────────────────┘
```

## Repository Structure

```
S1MCPServer/
├── S1MCPServer/          # C# MelonLoader mod (game-side)
│   ├── Core/             # Core systems (queues, protocol, router)
│   ├── Handlers/         # Command handlers (NPC, Player, Item, etc.)
│   ├── Server/           # TCP server implementation
│   ├── Models/           # Data models
│   ├── Utils/            # Utilities (reflection, logging)
│   └── MainMod.cs        # Main mod entry point
│
├── S1MCPClient/          # Python MCP server (client-side)
│   ├── src/
│   │   ├── main.py       # MCP server entry point
│   │   ├── tcp_client.py # TCP client for mod communication
│   │   ├── tools/        # MCP tool definitions
│   │   ├── models/       # Data models
│   │   └── utils/        # Utilities (logger, config)
│   └── requirements.txt  # Python dependencies
│
└── README.md             # This file
```

## Projects

### S1MCPServer

A MelonLoader mod that runs inside Schedule I and provides game API access via TCP/IP. Built with C# (.NET 6) and supports both IL2CPP and Mono backends.

**Key Features:**
- TCP server for external communication (port 8765)
- Thread-safe command/response queueing system
- Cross-runtime support (Mono/IL2CPP)
- Direct access to Schedule One native classes
- Reflection utilities using UniverseLib
- Optional UnityExplorer integration

**See:** [S1MCPServer/README.md](S1MCPServer/README.md) for detailed documentation.

### S1MCPClient

A Python-based MCP server that connects to the S1MCPServer mod and exposes game operations as MCP tools for LLM agents. Implements the official MCP protocol.

**Key Features:**
- MCP protocol implementation (official SDK)
- TCP client for mod communication
- Comprehensive tool set for game operations
- Automatic reconnection and error handling
- Configurable via JSON config file

**See:** [S1MCPClient/README.md](S1MCPClient/README.md) for detailed documentation.

## Quick Start

### Prerequisites

1. **Schedule I** game installed
2. **MelonLoader** installed for Schedule I
3. **Python 3.10+** installed
4. **Windows** (required for game, TCP works cross-platform)

### Installation

#### 1. Install S1MCPServer Mod

1. Build the mod:
   ```bash
   cd S1MCPServer
   # Open S1MCPServer.sln in Visual Studio or Rider
   # Build for your target backend (IL2CPP or Mono)
   # Configuration: Debug IL2CPP or Release IL2CPP
   ```

2. Install the compiled DLL:
   - Copy the compiled `.dll` from `bin/Debug IL2CPP/net6/` (or `Debug Mono`)
   - Place it in your Schedule I `Mods` folder
   - The mod will automatically start the TCP server on port 8765

#### 2. Install S1MCPClient

1. Navigate to the client directory:
   ```bash
   cd S1MCPClient
   ```

2. Install Python dependencies:
   ```bash
   pip install -r requirements.txt
   ```

3. (Optional) Create a configuration file:
   ```bash
   # Create config.json in S1MCPClient/
   {
     "host": "localhost",
     "port": 8765,
     "log_level": "INFO",
     "connection_timeout": 5.0,
     "reconnect_delay": 1.0
   }
   ```

#### 3. Configure MCP Client

Configure your MCP client (e.g., Claude Desktop) to use S1MCPClient:

**Claude Desktop** (`claude_desktop_config.json`):
```json
{
  "mcpServers": {
    "s1mcp": {
      "command": "python",
      "args": ["-m", "src.main"],
      "cwd": "C:\\path\\to\\S1MCPServer\\S1MCPClient"
    }
  }
}
```

**Cline or other MCP clients:**
- Command: `python -m src.main`
- Working Directory: Path to `S1MCPClient` folder

### Running

1. **Start Schedule I** with the mod loaded
2. **Wait for Main scene** to load (you'll see log: "Main scene loaded - S1MCPServer is active")
3. **Start your MCP client** (Claude Desktop, Cline, etc.) - it will automatically connect to the mod

## Available Tools

S1MCPClient exposes a comprehensive set of tools for game interaction:

### NPC Tools
- `s1_get_npc` - Get NPC information by ID
- `s1_list_npcs` - List all NPCs (optional filter)
- `s1_get_npc_position` - Get NPC position
- `s1_teleport_npc` - Teleport NPC to position
- `s1_set_npc_health` - Modify NPC health

### Player Tools
- `s1_get_player` - Get player information
- `s1_get_player_inventory` - Get player inventory
- `s1_teleport_player` - Teleport player
- `s1_add_item_to_player` - Add item to inventory

### Item Tools
- `s1_list_items` - List all item definitions
- `s1_get_item` - Get item information by ID
- `s1_spawn_item` - Spawn item in world

### Building Tools
- `s1_list_buildings` - List all buildings
- `s1_get_building` - Get building information

### Vehicle Tools
- `s1_list_vehicles` - List all vehicles
- `s1_get_vehicle` - Get vehicle information

### Game State Tools
- `s1_get_game_state` - Get current game state (scene, network, mods, version)

### Log Tools
- `s1_capture_logs` - Capture and filter game logs from MelonLoader for debugging
  - Filter by keyword, timestamp range, regex patterns
  - Get first/last N lines
  - Essential for agentic debugging

### Debug Tools
- `s1_inspect_object` - Inspect Unity GameObject using reflection

See [S1MCPClient/README.md](S1MCPClient/README.md) for detailed tool documentation.

## Protocol

The system uses JSON-RPC 2.0 over TCP/IP for communication between S1MCPClient and S1MCPServer:

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
    "position": {"x": 10.5, "y": 1.0, "z": 20.3},
    "health": 100.0,
    "is_conscious": true
  },
  "error": null
}
```

## Development

### Building S1MCPServer

The project supports multiple build configurations:

- **Debug Mono** - Debug build for Mono backend
- **Release Mono** - Release build for Mono
- **Debug IL2CPP** - Debug build for IL2CPP backend (recommended for most users)
- **Release IL2CPP** - Release build for IL2CPP (production)

Open `S1MCPServer.sln` in Visual Studio, Rider, or your preferred IDE and build for your target configuration.

### Project Dependencies

**S1MCPServer:**
- MelonLoader
- UniverseLib (for reflection utilities)
- .NET 6.0
- Direct access to Schedule One native classes

**S1MCPClient:**
- Python 3.10+
- `mcp>=0.9.0` (Official MCP SDK)
- `pywin32>=306` (Windows Named Pipes support - legacy, now uses TCP)
- `pydantic>=2.0.0` (Data validation)

### Adding New Tools

1. **Add command handler** in `S1MCPServer/Handlers/`
2. **Register handler** in `CommandRouter.cs`
3. **Add tool definition** in `S1MCPClient/src/tools/`
4. **Register tool** in `S1MCPClient/src/main.py`

See the individual project READMEs for detailed development guides.

## Troubleshooting

### Connection Issues

**Problem:** Cannot connect to mod

**Solutions:**
- Ensure Schedule I is running with the mod loaded
- Check that the Main scene has loaded (wait for log message)
- Verify TCP server is listening on port 8765 (check mod logs)
- Check firewall settings for localhost connections
- Verify the port matches in both mod and client config (default: 8765)

**Problem:** Connection lost during operation

**Solutions:**
- Check mod logs for errors
- Ensure the game hasn't crashed
- The client automatically retries on connection errors
- Check network connectivity (though localhost should always work)

### Mod Not Loading

**Problem:** Mod doesn't appear in MelonLoader

**Solutions:**
- Verify the DLL is in the correct `Mods` folder
- Check MelonLoader logs for loading errors
- Ensure you built for the correct backend (IL2CPP vs Mono)
- Verify all dependencies are present (UniverseLib, etc.)

### Tool Errors

**Problem:** Tool returns error from mod

**Solutions:**
- Check the error message and code in the response
- Verify parameters match the API specification
- Check mod logs for detailed error information
- Ensure game objects exist (e.g., NPC ID is valid)
- Wait for Main scene to fully load before making calls

### Debugging

Enable DEBUG logging in `S1MCPClient/config.json`:
```json
{
  "log_level": "DEBUG"
}
```

Check mod logs in the Schedule I game directory for detailed server-side logs.

## Use Cases

### 1. Agentic Debugging

Allow LLM agents to diagnose mod issues:
- Inspect game state when a mod isn't working
- Check if NPCs are spawning correctly
- Verify item registration
- Inspect mod interactions
- Capture and analyze game logs to track errors

**Example:**
```
User: "My mod isn't spawning NPCs correctly. Can you check what NPCs are currently in the game?"

LLM: [Calls s1_list_npcs] I found 15 NPCs. Here are the ones that might be relevant...

User: "The mod crashed at startup. Can you check the logs?"

LLM: [Calls s1_capture_logs with keyword="error"] I found several errors in the logs around the startup time. 
The issue appears to be...
```

### 2. Game State Inspection

Understand current game state:
- List all NPCs and their states
- Check player inventory
- Inspect building occupancy
- Review relationships

### 3. Testing Scenarios

Create test scenarios:
- Spawn NPCs at specific locations
- Add items to inventory
- Modify relationships
- Trigger events

### 4. Development Assistance

Aid mod development:
- Inspect available game objects
- Test game API functionality
- Verify mod integration
- Debug mod behavior

## Architecture Details

### Threading Model

The mod uses a sophisticated threading model to safely bridge Unity's single-threaded nature with external communication:

```
Background Thread (TCP Server)
    ↓ Enqueue Request
Command Queue (Thread-Safe)
    ↓ Process on Main Thread (Unity Update)
Command Handlers (Main Thread)
    ↓ Enqueue Result
Response Queue (Thread-Safe)
    ↓ Send via TCP
Background Thread (TCP Server)
```

This ensures:
- All Unity operations happen on the main thread
- External communication doesn't block game execution
- Thread-safe message passing between threads

### Security Considerations

- **Localhost Only**: TCP server binds to `127.0.0.1` only (not accessible from network)
- **Single Client**: Only one MCP client can connect at a time
- **Validation**: All parameters are validated before execution
- **Error Handling**: Graceful error handling prevents game crashes
- **Rate Limiting**: Operations are naturally rate-limited by game frame rate

## Related Documentation

- [S1MCPServer README](S1MCPServer/README.md) - Detailed mod documentation
- [S1MCPClient README](S1MCPClient/README.md) - Detailed client documentation
- [API Specification](S1MCPServer/assets/plan/api-specification.md) - Complete API reference
- [Vision Document](S1MCPServer/assets/plan/vision-document.md) - Architecture and design decisions

## Contributing

Contributions are welcome! Please:

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Test thoroughly
5. Submit a pull request

## License

[Add your license here]

## Credits

- **Tyler** - Project creator and maintainer
- Built for the Schedule I modding community
- Uses the official Model Context Protocol SDK
- Inspired by UnityExplorer's reflection techniques
- Uses UniverseLib for cross-runtime compatibility

## Support

For issues and questions:

- Check the troubleshooting section above
- Review individual project READMEs
- Check mod logs in Schedule I
- Enable DEBUG logging for detailed information
- Review API specification for tool usage

---

**Version:** 1.0.0  
**Last Updated:** 2025-01-27  
**Author:** Tyler

