using System;
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

namespace S1MCPServer.Handlers.Debug;

/// <summary>
/// Utility class for resolving GameObjects from various sources (name, NPC ID, etc.).
/// </summary>
public static class GameObjectResolver
{
    /// <summary>
    /// Resolves a GameObject from request parameters (supports both object_name and npc_id).
    /// </summary>
    public static GameObject? ResolveGameObject(Request request, out string? objectName)
    {
        objectName = null;
        
        if (request.Params == null)
            return null;

        // Try npc_id first
        if (request.Params.TryGetValue("npc_id", out var npcIdObj))
        {
            string npcId = npcIdObj?.ToString() ?? string.Empty;
            var gameObject = GetGameObjectFromNPCId(npcId);
            if (gameObject != null)
            {
                objectName = gameObject.name;
                return gameObject;
            }
        }

        // Try object_name
        if (request.Params.TryGetValue("object_name", out var objectNameObj))
        {
            objectName = objectNameObj?.ToString() ?? string.Empty;
            return ReflectionHelper.FindGameObject(objectName);
        }

        return null;
    }

    /// <summary>
    /// Gets a GameObject from an NPC ID by looking up the NPC and accessing its gameObject property.
    /// </summary>
    public static GameObject? GetGameObjectFromNPCId(string npcId)
    {
        try
        {
            object? foundNPC = null;
            
            // Look up NPC in registry
            if (S1NPCManager.NPCRegistry != null)
            {
                foreach (var npc in S1NPCManager.NPCRegistry)
                {
                    if (npc == null) continue;
                    
                    // Try to get NPC ID using TryGetFieldOrProperty to handle Mono/IL2CPP differences
                    string currentNpcId = "";
                    try
                    {
                        // Try common ID property names
                        currentNpcId = ReflectionHelper.TryGetFieldOrProperty(npc, "ID")?.ToString() ?? 
                                      ReflectionHelper.TryGetFieldOrProperty(npc, "Id")?.ToString() ?? 
                                      ReflectionHelper.TryGetFieldOrProperty(npc, "NPCID")?.ToString() ?? "";
                    }
                    catch { }
                    
                    if (currentNpcId.Equals(npcId, StringComparison.OrdinalIgnoreCase))
                    {
                        foundNPC = npc;
                        break;
                    }
                }
            }

            if (foundNPC == null)
                return null;

            // Get GameObject from NPC using TryGetFieldOrProperty to handle Mono/IL2CPP differences
            var gameObject = ReflectionHelper.TryGetFieldOrProperty(foundNPC, "gameObject") as GameObject;
            if (gameObject != null)
            {
                return gameObject;
            }
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"Error getting GameObject from NPC ID {npcId}: {ex.Message}");
        }

        return null;
    }
}

