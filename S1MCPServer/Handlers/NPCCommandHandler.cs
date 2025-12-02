using System;
using System.Collections.Generic;
using System.Reflection;
using S1MCPServer.Core;
using S1MCPServer.Models;
using S1MCPServer.Utils;
using UnityEngine;
#if MONO
using ScheduleOne;
using S1NPC = ScheduleOne.NPCs.NPC;
using S1NPCManager = ScheduleOne.NPCs.NPCManager;
#else
using Il2CppScheduleOne;
using S1NPC = Il2CppScheduleOne.NPCs.NPC;
using S1NPCManager = Il2CppScheduleOne.NPCs.NPCManager;
#endif

namespace S1MCPServer.Handlers;

/// <summary>
/// Handles NPC-related commands.
/// </summary>
public class NPCCommandHandler : ICommandHandler
{
    private readonly ResponseQueue _responseQueue;

    public NPCCommandHandler(ResponseQueue responseQueue)
    {
        _responseQueue = responseQueue;
    }

    public void Handle(Request request)
    {
        switch (request.Method)
        {
            case "get_npc":
                HandleGetNPC(request);
                break;
            case "list_npcs":
                HandleListNPCs(request);
                break;
            case "get_npc_position":
                HandleGetNPCPosition(request);
                break;
            case "teleport_npc":
                HandleTeleportNPC(request);
                break;
            case "set_npc_health":
                HandleSetNPCHealth(request);
                break;
            case "inspect_npc_dealer":
                HandleInspectNPCDealer(request);
                break;
            default:
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32601, // Method not found
                    $"Unknown NPC method: {request.Method}"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                break;
        }
    }

    private void HandleGetNPC(Request request)
    {
        try
        {
            if (request.Params == null || !request.Params.TryGetValue("npc_id", out var npcIdObj))
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602, // Invalid params
                    "npc_id parameter is required"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            string npcId = npcIdObj?.ToString() ?? string.Empty;
            ModLogger.Debug($"Getting NPC: {npcId}");

            // Validate NPC ID
            if (!ValidationHelper.ValidateNPCID(npcId, out string? validationError))
            {
                var errorResponse = ValidationHelper.CreateValidationErrorResponse(request.Id, validationError ?? "Invalid NPC ID");
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            // Look up NPC in registry
            S1NPC? foundNPC = null;
            if (S1NPCManager.NPCRegistry != null)
            {
                foreach (var npc in S1NPCManager.NPCRegistry)
                {
                    if (npc == null) continue;
                    
                    string currentNpcId = GetNPCId(npc);
                    if (currentNpcId.Equals(npcId, StringComparison.OrdinalIgnoreCase))
                    {
                        foundNPC = npc;
                        break;
                    }
                }
            }

            // Check if NPC was found
            if (foundNPC == null)
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32000, // Game error
                    "NPC not found",
                    new { npc_id = npcId }
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            // Extract serializable data from the NPC
            var npcData = ExtractNPCSerializableData(foundNPC, npcId);
            if (npcData == null)
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32000, // Game error
                    "Failed to extract NPC data",
                    new { npc_id = npcId }
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, npcData);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleGetNPC: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000, // Game error
                "Failed to get NPC",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }

    private void HandleListNPCs(Request request)
    {
        try
        {
            string? filter = null;
            if (request.Params != null && request.Params.TryGetValue("filter", out var filterObj))
            {
                filter = filterObj?.ToString();
            }

            ModLogger.Debug($"Listing NPCs with filter: {filter ?? "none"}");

            // TODO: Implement NPC listing using native game classes (reference S1API NPC enumeration patterns)
            // For now, return empty list

            // Extract serializable data from NPC registry to avoid Unity object serialization issues
            var npcList = new List<Dictionary<string, object>>();

            if (S1NPCManager.NPCRegistry != null)
            {
                foreach (var npc in S1NPCManager.NPCRegistry)
                {
                    try
                    {
                        if (npc == null) continue;

                        // Get NPC ID from the NPC object
                        string npcId = GetNPCId(npc);
                        if (string.IsNullOrEmpty(npcId))
                        {
                            ModLogger.Debug("Skipping NPC with null or empty ID");
                            continue;
                        }

                        var npcData = ExtractNPCSerializableData(npc, npcId);
                        if (npcData != null)
                        {
                            // Apply filter if specified
                            if (filter == null || MatchesFilter(npcData, filter))
                            {
                                npcList.Add(npcData);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Debug($"Error extracting data for NPC: {ex.Message}");
                        // Continue with other NPCs
                    }
                }
            }

            var result = new Dictionary<string, object>
            {
                ["npcs"] = npcList,
                ["count"] = npcList.Count
            };

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleListNPCs: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000, // Game error
                "Failed to list NPCs",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }

    private void HandleGetNPCPosition(Request request)
    {
        try
        {
            if (request.Params == null || !request.Params.TryGetValue("npc_id", out var npcIdObj))
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602, // Invalid params
                    "npc_id parameter is required"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            string npcId = npcIdObj?.ToString() ?? string.Empty;
            ModLogger.Debug($"Getting NPC position: {npcId}");

            // TODO: Implement NPC position lookup using native game classes (reference S1API NPC state access patterns)
            var result = new Dictionary<string, object>
            {
                ["npc_id"] = npcId,
                ["position"] = new { x = 0.0f, y = 0.0f, z = 0.0f }
            };

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleGetNPCPosition: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000, // Game error
                "Failed to get NPC position",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }

    private void HandleTeleportNPC(Request request)
    {
        try
        {
            if (request.Params == null)
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602, // Invalid params
                    "Parameters are required"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            if (!request.Params.TryGetValue("npc_id", out var npcIdObj))
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602, // Invalid params
                    "npc_id parameter is required"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            if (!request.Params.TryGetValue("position", out var positionObj))
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602, // Invalid params
                    "position parameter is required"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            string npcId = npcIdObj?.ToString() ?? string.Empty;

            // Validate NPC ID
            if (!ValidationHelper.ValidateNPCID(npcId, out string? npcError))
            {
                var errorResponse = ValidationHelper.CreateValidationErrorResponse(request.Id, npcError ?? "Invalid NPC ID");
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            // Parse position
            Position? position = null;
            try
            {
                // Try to deserialize position from dictionary
                if (positionObj is Dictionary<string, object> posDict)
                {
                    position = new Position
                    {
                        X = Convert.ToSingle(posDict.GetValueOrDefault("x", 0.0f)),
                        Y = Convert.ToSingle(posDict.GetValueOrDefault("y", 0.0f)),
                        Z = Convert.ToSingle(posDict.GetValueOrDefault("z", 0.0f))
                    };
                }
                else if (positionObj is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    position = new Position
                    {
                        X = jsonElement.TryGetProperty("x", out var xProp) ? (float)xProp.GetDouble() : 0.0f,
                        Y = jsonElement.TryGetProperty("y", out var yProp) ? (float)yProp.GetDouble() : 0.0f,
                        Z = jsonElement.TryGetProperty("z", out var zProp) ? (float)zProp.GetDouble() : 0.0f
                    };
                }
            }
            catch
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602, // Invalid params
                    "Invalid position format"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            if (position == null)
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602, // Invalid params
                    "Failed to parse position"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            // Validate position
            if (!ValidationHelper.ValidatePosition(position, out string? posError))
            {
                var errorResponse = ValidationHelper.CreateValidationErrorResponse(request.Id, posError ?? "Invalid position");
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            ModLogger.Debug($"Teleporting NPC {npcId} to position ({position.X}, {position.Y}, {position.Z})");

            // TODO: Implement NPC teleportation using native game classes (reference S1API NPC manipulation patterns)
            // For now, return success
            var result = new Dictionary<string, object>
            {
                ["success"] = true,
                ["npc_id"] = npcId,
                ["new_position"] = new { position.X, position.Y, position.Z }
            };

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleTeleportNPC: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000, // Game error
                "Failed to teleport NPC",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }

    private void HandleSetNPCHealth(Request request)
    {
        try
        {
            if (request.Params == null)
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602, // Invalid params
                    "Parameters are required"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            if (!request.Params.TryGetValue("npc_id", out var npcIdObj))
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602, // Invalid params
                    "npc_id parameter is required"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            if (!request.Params.TryGetValue("health", out var healthObj))
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602, // Invalid params
                    "health parameter is required"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            string npcId = npcIdObj?.ToString() ?? string.Empty;

            // Validate NPC ID
            if (!ValidationHelper.ValidateNPCID(npcId, out string? npcError))
            {
                var errorResponse = ValidationHelper.CreateValidationErrorResponse(request.Id, npcError ?? "Invalid NPC ID");
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            // Parse health
            float health;
            try
            {
                health = Convert.ToSingle(healthObj);
                if (health < 0.0f || health > 1000.0f)
                {
                    var errorResponse = ValidationHelper.CreateValidationErrorResponse(
                        request.Id,
                        "Health must be between 0 and 1000"
                    );
                    _responseQueue.EnqueueResponse(errorResponse);
                    return;
                }
            }
            catch
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602, // Invalid params
                    "Invalid health value"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            ModLogger.Debug($"Setting NPC {npcId} health to {health}");

            // TODO: Implement NPC health modification using native game classes (reference S1API NPC state modification patterns)
            // For now, return success
            var result = new Dictionary<string, object>
            {
                ["success"] = true,
                ["npc_id"] = npcId,
                ["old_health"] = 100.0f, // TODO: Get actual old health
                ["new_health"] = health
            };

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleSetNPCHealth: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000, // Game error
                "Failed to set NPC health",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }
    
    /// <summary>
    /// Gets the NPC ID from an NPC object.
    /// </summary>
    private string GetNPCId(S1NPC npc)
    {
        if (npc == null) return string.Empty;
        
        try
        {
            // Try common property names for NPC ID
            var idProperty = npc.GetType().GetProperty("ID") ?? 
                           npc.GetType().GetProperty("Id") ?? 
                           npc.GetType().GetProperty("NPCID") ?? 
                           npc.GetType().GetProperty("NpcId") ??
                           npc.GetType().GetProperty("Identifier") ??
                           npc.GetType().GetProperty("Name");
            
            if (idProperty != null)
            {
                var idValue = idProperty.GetValue(npc);
                if (idValue != null)
                {
                    return idValue.ToString() ?? string.Empty;
                }
            }
            
            // Fallback to ToString() if no ID property found
            return npc.ToString() ?? "Unknown";
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"Error getting NPC ID: {ex.Message}");
            return "Unknown";
        }
    }
    
    /// <summary>
    /// Extracts only serializable data from an NPC object, avoiding Unity-specific objects.
    /// </summary>
    private Dictionary<string, object>? ExtractNPCSerializableData(S1NPC npc, string npcId)
    {
        try
        {
            var data = new Dictionary<string, object>
            {
                ["npc_id"] = npcId
            };
            
            // Extract basic properties (avoid Unity objects like Sprites, Textures, etc.)
            try
            {
                if (npc != null)
                {
                    // Get name if available
                    var nameProperty = npc.GetType().GetProperty("Name") ?? npc.GetType().GetProperty("name");
                    if (nameProperty != null)
                    {
                        var nameValue = nameProperty.GetValue(npc);
                        if (nameValue != null)
                        {
                            data["name"] = nameValue.ToString() ?? "Unknown";
                        }
                    }
                    
                    // Get position if available (extract Vector3 components)
                    var positionProperty = npc.GetType().GetProperty("Position") ?? 
                                          npc.GetType().GetProperty("position") ??
                                          npc.GetType().GetProperty("transform");
                    if (positionProperty != null)
                    {
                        var posValue = positionProperty.GetValue(npc);
                        if (posValue != null)
                        {
                            if (posValue is Transform transform)
                            {
                                var pos = transform.position;
                                data["position"] = new { x = pos.x, y = pos.y, z = pos.z };
                            }
                            else if (posValue is Vector3 vector3)
                            {
                                data["position"] = new { x = vector3.x, y = vector3.y, z = vector3.z };
                            }
                        }
                    }
                    
                    // Get health if available
                    var healthProperty = npc.GetType().GetProperty("Health") ?? 
                                       npc.GetType().GetProperty("health") ??
                                       npc.GetType().GetProperty("CurrentHealth");
                    if (healthProperty != null)
                    {
                        var healthValue = healthProperty.GetValue(npc);
                        if (healthValue != null)
                        {
                            data["health"] = Convert.ToSingle(healthValue);
                        }
                    }
                    
                    // Get conscious state if available
                    var consciousProperty = npc.GetType().GetProperty("IsConscious") ?? 
                                          npc.GetType().GetProperty("isConscious") ??
                                          npc.GetType().GetProperty("Conscious");
                    if (consciousProperty != null)
                    {
                        var consciousValue = consciousProperty.GetValue(npc);
                        if (consciousValue != null)
                        {
                            data["is_conscious"] = Convert.ToBoolean(consciousValue);
                        }
                    }

                    // Get GameObject name - critical for component inspection
                    try
                    {
                        var gameObjectProperty = npc.GetType().GetProperty("gameObject");
                        if (gameObjectProperty != null)
                        {
                            var gameObject = gameObjectProperty.GetValue(npc) as GameObject;
                            if (gameObject != null)
                            {
                                data["game_object_name"] = gameObject.name;
                            }
                        }
                    }
                    catch
                    {
                        // GameObject property might not be accessible
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"Error extracting NPC properties: {ex.Message}");
                // Continue with basic data
            }
            
            return data;
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"Error in ExtractNPCSerializableData: {ex.Message}");
            return null;
        }
    }
    
    private void HandleInspectNPCDealer(Request request)
    {
        try
        {
            if (request.Params == null || !request.Params.TryGetValue("npc_id", out var npcIdObj))
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602, // Invalid params
                    "npc_id parameter is required"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            string npcId = npcIdObj?.ToString() ?? string.Empty;
            ModLogger.Debug($"Inspecting dealer for NPC: {npcId}");

            // Validate NPC ID
            if (!ValidationHelper.ValidateNPCID(npcId, out string? validationError))
            {
                var errorResponse = ValidationHelper.CreateValidationErrorResponse(request.Id, validationError ?? "Invalid NPC ID");
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            // Look up NPC in registry
            S1NPC? foundNPC = null;
            if (S1NPCManager.NPCRegistry != null)
            {
                foreach (var npc in S1NPCManager.NPCRegistry)
                {
                    if (npc == null) continue;
                    
                    string currentNpcId = GetNPCId(npc);
                    if (currentNpcId.Equals(npcId, StringComparison.OrdinalIgnoreCase))
                    {
                        foundNPC = npc;
                        break;
                    }
                }
            }

            if (foundNPC == null)
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32000, // Game error
                    "NPC not found",
                    new { npc_id = npcId }
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            var result = new Dictionary<string, object>
            {
                ["npc_id"] = npcId,
                ["has_dealer_component"] = false,
                ["home_building_name"] = null,
                ["home_building_resolved"] = false,
                ["dealer_home_building_name_stored"] = null
            };

            // Try to get Dealer component
#if MONO
            var dealerComponent = foundNPC.GetComponent<ScheduleOne.Economy.Dealer>();
#else
            var dealerComponent = foundNPC.GetComponent<Il2CppScheduleOne.Economy.Dealer>();
#endif
            if (dealerComponent != null)
            {
                result["has_dealer_component"] = true;

                // Try to get Home property/field
                try
                {
                    var homeProperty = dealerComponent.GetType().GetProperty("Home");
                    var homeField = dealerComponent.GetType().GetField("Home", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    
                    object? homeValue = null;
                    if (homeProperty != null)
                    {
                        homeValue = homeProperty.GetValue(dealerComponent);
                    }
                    else if (homeField != null)
                    {
                        homeValue = homeField.GetValue(dealerComponent);
                    }

                    if (homeValue != null)
                    {
                        // Try to get BuildingName from the home building
                        var buildingNameProp = homeValue.GetType().GetProperty("BuildingName");
                        if (buildingNameProp != null)
                        {
                            var buildingName = buildingNameProp.GetValue(homeValue)?.ToString();
                            result["home_building_name"] = buildingName ?? "null";
                            result["home_building_resolved"] = !string.IsNullOrEmpty(buildingName);
                        }
                        else
                        {
                            result["home_building_name"] = homeValue.ToString() ?? "null";
                            result["home_building_resolved"] = true;
                        }
                    }
                    else
                    {
                        result["home_building_name"] = "null";
                        result["home_building_resolved"] = false;
                    }
                }
                catch (Exception ex)
                {
                    result["home_error"] = ex.Message;
                }
            }

            // Check NPCPrefabIdentity for stored building name
            try
            {
                // Try to find NPCPrefabIdentity component by searching all components
                var allComponents = foundNPC.gameObject.GetComponents<Component>();
                Component? identityComponent = null;
                foreach (var comp in allComponents)
                {
                    if (comp != null && comp.GetType().FullName?.Contains("NPCPrefabIdentity") == true)
                    {
                        identityComponent = comp;
                        break;
                    }
                }
                
                if (identityComponent != null)
                {
                    var dealerHomeNameField = identityComponent.GetType().GetField("DealerHomeBuildingName", BindingFlags.Public | BindingFlags.Instance);
                    if (dealerHomeNameField != null)
                    {
                        var storedName = dealerHomeNameField.GetValue(identityComponent)?.ToString();
                        result["dealer_home_building_name_stored"] = storedName ?? "null";
                    }
                    else
                    {
                        result["dealer_home_building_name_stored"] = "field_not_found";
                    }
                }
                else
                {
                    result["dealer_home_building_name_stored"] = "component_not_found";
                }
            }
            catch (Exception ex)
            {
                result["identity_error"] = ex.Message;
            }

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleInspectNPCDealer: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000, // Game error
                "Failed to inspect NPC dealer",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }

    /// <summary>
    /// Checks if NPC data matches the specified filter.
    /// </summary>
    private bool MatchesFilter(Dictionary<string, object> npcData, string filter)
    {
        if (string.IsNullOrEmpty(filter)) return true;
        
        filter = filter.ToLowerInvariant();
        
        switch (filter)
        {
            case "conscious":
                return npcData.TryGetValue("is_conscious", out var conscious) && 
                       conscious is bool isConscious && isConscious;
            case "unconscious":
                return npcData.TryGetValue("is_conscious", out var unconscious) && 
                       unconscious is bool isUnconscious && !isUnconscious;
            default:
                return true; // Unknown filter, include all
        }
    }
}

