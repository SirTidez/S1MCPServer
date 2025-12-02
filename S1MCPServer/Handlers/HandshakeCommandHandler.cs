using System.Collections.Generic;
using S1MCPServer.Core;
using S1MCPServer.Models;
using S1MCPServer.Utils;

namespace S1MCPServer.Handlers;

/// <summary>
/// Handles handshake and system-level commands.
/// </summary>
public class HandshakeCommandHandler : ICommandHandler
{
    private readonly ResponseQueue _responseQueue;
    private readonly CommandRouter _commandRouter;

    public HandshakeCommandHandler(ResponseQueue responseQueue, CommandRouter commandRouter)
    {
        _responseQueue = responseQueue;
        _commandRouter = commandRouter;
    }

    public void Handle(Request request)
    {
        switch (request.Method)
        {
            case "handshake":
            case "list_methods":
                HandleHandshake(request);
                break;
            case "heartbeat":
                HandleHeartbeat(request);
                break;
            default:
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32601, // Method not found
                    $"Unknown handshake method: {request.Method}"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                break;
        }
    }

    private void HandleHandshake(Request request)
    {
        try
        {
            ModLogger.Debug("Handling handshake request");
            
            var availableMethods = _commandRouter.GetAvailableMethods();
            
            // Categorize methods by type
            var methodCategories = new Dictionary<string, List<string>>
            {
                ["npc"] = new List<string>(),
                ["player"] = new List<string>(),
                ["item"] = new List<string>(),
                ["property"] = new List<string>(),
                ["vehicle"] = new List<string>(),
                ["game_state"] = new List<string>(),
                ["debug"] = new List<string>(),
                ["system"] = new List<string>()
            };

            foreach (var method in availableMethods)
            {
                if (method.StartsWith("get_npc") || method.StartsWith("list_npc") || method.StartsWith("teleport_npc") || method.StartsWith("set_npc"))
                {
                    methodCategories["npc"].Add(method);
                }
                else if (method.StartsWith("get_player") || method.StartsWith("teleport_player") || method.StartsWith("add_item_to_player"))
                {
                    methodCategories["player"].Add(method);
                }
                else if (method.StartsWith("get_item") || method.StartsWith("list_item") || method.StartsWith("spawn_item"))
                {
                    methodCategories["item"].Add(method);
                }
                else if (method.StartsWith("get_property") || method.StartsWith("list_property"))
                {
                    methodCategories["property"].Add(method);
                }
                else if (method.StartsWith("get_vehicle") || method.StartsWith("list_vehicle"))
                {
                    methodCategories["vehicle"].Add(method);
                }
                else if (method.StartsWith("get_game_state"))
                {
                    methodCategories["game_state"].Add(method);
                }
                else if (method.StartsWith("inspect") || method.StartsWith("find_objects") || method.StartsWith("get_scene"))
                {
                    methodCategories["debug"].Add(method);
                }
                else if (method == "handshake" || method == "list_methods")
                {
                    methodCategories["system"].Add(method);
                }
            }

            // Generate comprehensive instructions for LLMs
            var instructions = GenerateInstructions(availableMethods, methodCategories);
            
            var result = new Dictionary<string, object>
            {
                ["status"] = "connected",
                ["server_name"] = "S1MCPServer",
                ["version"] = "1.0.0",
                ["total_methods"] = availableMethods.Count,
                ["available_methods"] = availableMethods,
                ["method_categories"] = methodCategories,
                ["instructions"] = instructions
            };

            ModLogger.Debug($"Handshake response: {availableMethods.Count} methods available");
            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleHandshake: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32603, // Internal error
                "Failed to process handshake",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }

    private string GenerateInstructions(List<string> availableMethods, Dictionary<string, List<string>> methodCategories)
    {
        var instructions = @"You are connected to the Schedule I game mod (S1MCPServer). This mod provides real-time access to game state and allows you to interact with the game world.

## Available Capabilities

### NPC Management
- Query NPC information (position, health, consciousness, relationships)
- List all NPCs with optional filtering (conscious, unconscious, in building, in vehicle)
- Teleport NPCs to specific locations
- Modify NPC health values

### Player Management
- Get current player information (position, health, money, network status)
- View player inventory items
- Add items to player inventory
- Teleport player to specific locations

### Item Management
- List all item definitions in the game
- Get detailed information about specific items
- Spawn items in the world at specific positions

### Property Management
- List all properties (buildings) in the game
- Get detailed information about properties including NPCs inside

### Vehicle Management
- List all vehicles in the game
- Get vehicle information including occupants

### Game State
- Query current game state (scene, time, network status, loaded mods)

### Debug Tools
- Inspect Unity GameObjects and components using reflection
- Find objects in the scene

## Important Notes

1. **Game Context**: All operations happen in real-time within the running game. Changes are immediately reflected in the game world.

2. **Position Coordinates**: The game uses a 3D coordinate system (x, y, z). The y-axis typically represents height/elevation.

3. **NPC IDs**: NPCs are identified by unique string IDs (e.g., ""kyle_cooley""). Use list_npcs to discover available NPCs.

4. **Item IDs**: Items are identified by unique string IDs (e.g., ""cuke"" for cucumber). Use list_items to discover available items.

5. **Property IDs**: Properties (buildings) can be identified by ID or name. Use list_properties to discover available properties.

6. **Thread Safety**: All operations are thread-safe and executed on the game's main thread to ensure stability.

7. **Error Handling**: If an operation fails, you'll receive an error response with details about what went wrong.

8. **Network Status**: The game can run in singleplayer, host, or client mode. Some operations may behave differently in multiplayer.

## Best Practices

- Always check game state before making significant changes
- Use list operations to discover available entities before querying specific ones
- Be mindful of teleporting NPCs or players to avoid placing them in invalid locations
- When adding items, respect stack limits and inventory capacity
- Use debug tools sparingly and only when necessary for troubleshooting

You can use the available tools to interact with the game, query information, and make changes as needed. All operations are performed safely within the game's threading model.";

        return instructions;
    }

    private void HandleHeartbeat(Request request)
    {
        try
        {
            ModLogger.Debug($"Handling heartbeat request (ID: {request.Id})");
            
            // Heartbeat is a simple acknowledgment - just respond with success
            var result = new Dictionary<string, object>
            {
                ["status"] = "alive",
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
            ModLogger.Debug($"Heartbeat response sent (ID: {request.Id})");
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleHeartbeat: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32603, // Internal error
                "Failed to process heartbeat",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }
}

