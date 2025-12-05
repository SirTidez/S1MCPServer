using System.Collections.Generic;
using System.Linq;
using S1MCPServer.Handlers;
using S1MCPServer.Handlers.Debug;
using S1MCPServer.Models;
using S1MCPServer.Utils;

namespace S1MCPServer.Core;

/// <summary>
/// Routes commands to appropriate handlers based on method name.
/// </summary>
public class CommandRouter
{
    private readonly ResponseQueue _responseQueue;
    private readonly Dictionary<string, ICommandHandler> _handlers = new();

    public CommandRouter(ResponseQueue responseQueue)
    {
        _responseQueue = responseQueue;
        InitializeHandlers();
    }

    /// <summary>
    /// Initializes all command handlers.
    /// </summary>
    private void InitializeHandlers()
    {
        // Handshake/system handlers (must be first)
        _handlers["handshake"] = new HandshakeCommandHandler(_responseQueue, this);
        _handlers["list_methods"] = new HandshakeCommandHandler(_responseQueue, this);
        _handlers["heartbeat"] = new HandshakeCommandHandler(_responseQueue, this);
        
        // Phase 2 handlers
        _handlers["get_npc"] = new NPCCommandHandler(_responseQueue);
        _handlers["list_npcs"] = new NPCCommandHandler(_responseQueue);
        _handlers["get_npc_position"] = new NPCCommandHandler(_responseQueue);
        _handlers["get_player"] = new PlayerCommandHandler(_responseQueue);
        _handlers["get_player_inventory"] = new PlayerCommandHandler(_responseQueue);
        _handlers["list_items"] = new ItemCommandHandler(_responseQueue);
        _handlers["get_item"] = new ItemCommandHandler(_responseQueue);
        _handlers["list_properties"] = new PropertyCommandHandler(_responseQueue);
        _handlers["get_property"] = new PropertyCommandHandler(_responseQueue); // Game properties (buildings)
        _handlers["get_game_state"] = new GameStateCommandHandler(_responseQueue);

        // Phase 3 handlers (write operations)
        _handlers["teleport_npc"] = new NPCCommandHandler(_responseQueue);
        _handlers["set_npc_health"] = new NPCCommandHandler(_responseQueue);
        _handlers["teleport_player"] = new PlayerCommandHandler(_responseQueue);
        _handlers["add_item_to_player"] = new PlayerCommandHandler(_responseQueue);
        _handlers["spawn_item"] = new ItemCommandHandler(_responseQueue);

        // Phase 4 handlers
        _handlers["list_vehicles"] = new VehicleCommandHandler(_responseQueue);
        _handlers["get_vehicle"] = new VehicleCommandHandler(_responseQueue);
        
        // Log handlers
        _handlers["capture_logs"] = new LogCommandHandler(_responseQueue);
        
        // LoadManager handlers
        _handlers["list_saves"] = new LoadManagerCommandHandler(_responseQueue);
        _handlers["load_save"] = new LoadManagerCommandHandler(_responseQueue);
        
        // Debug handlers - modularized into separate handlers
        var discoveryHandler = new DebugDiscoveryHandler(_responseQueue);
        _handlers["find_gameobjects"] = discoveryHandler;
        _handlers["search_types"] = discoveryHandler;
        _handlers["get_scene_hierarchy"] = discoveryHandler;
        _handlers["list_scenes"] = discoveryHandler;
        
        var objectInspectionHandler = new DebugObjectInspectionHandler(_responseQueue);
        _handlers["inspect_object"] = objectInspectionHandler;
        
        var componentInspectionHandler = new DebugComponentInspectionHandler(_responseQueue);
        _handlers["inspect_component"] = componentInspectionHandler;
        _handlers["list_components"] = componentInspectionHandler;
        _handlers["get_component_by_type"] = componentInspectionHandler;
        _handlers["get_member_value"] = componentInspectionHandler;
        
        var typeInspectionHandler = new DebugTypeInspectionHandler(_responseQueue);
        _handlers["inspect_type"] = typeInspectionHandler;
        _handlers["list_members"] = typeInspectionHandler;
        
        var hierarchyHandler = new DebugHierarchyHandler(_responseQueue);
        _handlers["get_hierarchy"] = hierarchyHandler;
        _handlers["find_objects_by_type"] = hierarchyHandler;
        _handlers["get_scene_objects"] = hierarchyHandler;
        
        var accessHandler = new DebugAccessHandler(_responseQueue);
        _handlers["get_field"] = accessHandler;
        _handlers["get_component_property"] = accessHandler; // Component properties (not game properties)
        _handlers["is_active"] = accessHandler;
        _handlers["get_transform"] = accessHandler;
        
        var modificationHandler = new DebugModificationHandler(_responseQueue);
        _handlers["set_field"] = modificationHandler;
        _handlers["set_component_property"] = modificationHandler; // Component properties
        _handlers["call_method"] = modificationHandler;
        _handlers["set_active"] = modificationHandler;
        _handlers["set_transform"] = modificationHandler;

        ModLogger.Info($"Initialized {_handlers.Count} command handlers");
    }
    
    /// <summary>
    /// Gets all available method names.
    /// </summary>
    public List<string> GetAvailableMethods()
    {
        return _handlers.Keys.ToList();
    }

    /// <summary>
    /// Routes a command to the appropriate handler.
    /// </summary>
    /// <param name="request">The request to route.</param>
    public void RouteCommand(Request request)
    {
        ModLogger.Debug($"RouteCommand: Routing request ID={request.Id}, Method={request.Method}");
        
        if (string.IsNullOrEmpty(request.Method))
        {
            ModLogger.Error("RouteCommand: Method name is empty or null");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32600, // Invalid Request
                "Method name is required"
            );
            _responseQueue.EnqueueResponse(errorResponse);
            ModLogger.Debug($"RouteCommand: Enqueued error response for ID={request.Id}");
            return;
        }

        if (!_handlers.TryGetValue(request.Method, out ICommandHandler? handler))
        {
            ModLogger.Error($"RouteCommand: Method '{request.Method}' not found in handlers");
            ModLogger.Debug($"RouteCommand: Available methods: {string.Join(", ", _handlers.Keys)}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32601, // Method not found
                $"Method '{request.Method}' not found",
                new { availableMethods = _handlers.Keys.ToList() }
            );
            _responseQueue.EnqueueResponse(errorResponse);
            ModLogger.Debug($"RouteCommand: Enqueued method not found error response for ID={request.Id}");
            return;
        }

        ModLogger.Debug($"RouteCommand: Found handler for method '{request.Method}', invoking...");
        try
        {
            handler.Handle(request);
            ModLogger.Debug($"RouteCommand: Handler for '{request.Method}' completed successfully");
        }
        catch (Exception ex)
        {
            ModLogger.Error($"RouteCommand: Handler error for {request.Method}: {ex.Message}");
            ModLogger.Debug($"RouteCommand: Exception type: {ex.GetType().Name}");
            ModLogger.Debug($"RouteCommand: Stack trace: {ex}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32603, // Internal error
                "Internal error in command handler",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
            ModLogger.Debug($"RouteCommand: Enqueued internal error response for ID={request.Id}");
        }
    }
}

/// <summary>
/// Interface for command handlers.
/// </summary>
public interface ICommandHandler
{
    /// <summary>
    /// Handles a command request.
    /// </summary>
    /// <param name="request">The request to handle.</param>
    void Handle(Request request);
}

