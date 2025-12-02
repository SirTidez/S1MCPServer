using System;
using System.Collections.Generic;
using System.Linq;
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

namespace S1MCPServer.Handlers.Debug;

/// <summary>
/// Base class for debug handlers with shared utility methods.
/// </summary>
public abstract class DebugHandlerBase : ICommandHandler
{
    protected readonly ResponseQueue _responseQueue;

    protected DebugHandlerBase(ResponseQueue responseQueue)
    {
        _responseQueue = responseQueue;
    }

    public abstract void Handle(Request request);

    /// <summary>
    /// Finds a component by type name (supports partial matching) on a GameObject.
    /// Delegates to ComponentResolver utility class.
    /// </summary>
    protected Component? FindComponentByTypeName(GameObject gameObject, string typeName)
    {
        return ComponentResolver.FindComponentByTypeName(gameObject, typeName);
    }

    /// <summary>
    /// Gets a GameObject from an NPC ID by looking up the NPC and accessing its gameObject property.
    /// Delegates to GameObjectResolver utility class.
    /// </summary>
    protected GameObject? GetGameObjectFromNPCId(string npcId)
    {
        return GameObjectResolver.GetGameObjectFromNPCId(npcId);
    }

    /// <summary>
    /// Resolves a component type by name, searching through all loaded assemblies.
    /// Delegates to ComponentResolver utility class.
    /// </summary>
    protected Type? ResolveComponentType(string typeName)
    {
        return ComponentResolver.ResolveComponentType(typeName);
    }

    /// <summary>
    /// Finds a component by type name in the entire scene.
    /// Delegates to ComponentResolver utility class.
    /// </summary>
    protected Component? FindComponentByTypeNameInScene(string typeName)
    {
        return ComponentResolver.FindComponentByTypeNameInScene(typeName);
    }

    /// <summary>
    /// Gets the GameObject path (hierarchy path) for a GameObject.
    /// </summary>
    protected string GetGameObjectPath(GameObject obj)
    {
        var path = obj.name;
        var current = obj.transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }
        return path;
    }

    /// <summary>
    /// Calculates the Levenshtein distance between two strings for fuzzy matching.
    /// </summary>
    protected static int LevenshteinDistance(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1))
            return string.IsNullOrEmpty(s2) ? 0 : s2.Length;
        if (string.IsNullOrEmpty(s2))
            return s1.Length;

        int[,] distance = new int[s1.Length + 1, s2.Length + 1];

        for (int i = 0; i <= s1.Length; i++)
            distance[i, 0] = i;
        for (int j = 0; j <= s2.Length; j++)
            distance[0, j] = j;

        for (int i = 1; i <= s1.Length; i++)
        {
            for (int j = 1; j <= s2.Length; j++)
            {
                int cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;

                distance[i, j] = Math.Min(
                    Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                    distance[i - 1, j - 1] + cost
                );
            }
        }

        return distance[s1.Length, s2.Length];
    }

    /// <summary>
    /// Checks if a type is IntPtr or UIntPtr (not serializable and can generally be ignored in IL2CPP).
    /// Delegates to ValueFormatter utility class.
    /// </summary>
    protected static bool IsIntPtrType(Type type)
    {
        return ValueFormatter.IsIntPtrType(type);
    }

    /// <summary>
    /// Checks if a member name contains "backingfield" (case-insensitive).
    /// Used to filter out compiler-generated backing fields.
    /// Delegates to ValueFormatter utility class.
    /// </summary>
    protected static bool IsBackingField(string name)
    {
        return ValueFormatter.IsBackingField(name);
    }

    /// <summary>
    /// Resolves a GameObject from request parameters (supports both object_name and npc_id).
    /// Delegates to GameObjectResolver utility class.
    /// </summary>
    protected GameObject? ResolveGameObject(Request request, out string? objectName)
    {
        return GameObjectResolver.ResolveGameObject(request, out objectName);
    }

    /// <summary>
    /// Formats a simple value for display (primitives, strings, Unity objects).
    /// Delegates to ValueFormatter utility class.
    /// </summary>
    protected object? FormatValue(object? value)
    {
        return ValueFormatter.FormatValue(value);
    }

    /// <summary>
    /// Enhanced value formatting that handles arrays, lists, dictionaries, and nested objects.
    /// Delegates to ValueFormatter utility class.
    /// </summary>
    protected object? FormatValueDeep(object? value, int remainingDepth)
    {
        return ValueFormatter.FormatValueDeep(value, remainingDepth);
    }
}

