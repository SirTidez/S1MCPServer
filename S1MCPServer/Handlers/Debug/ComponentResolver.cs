using System;
using System.Linq;
using System.Reflection;
using S1MCPServer.Utils;
using UnityEngine;

namespace S1MCPServer.Handlers.Debug;

/// <summary>
/// Utility class for resolving and finding components by type name.
/// </summary>
public static class ComponentResolver
{
    /// <summary>
    /// Finds a component by type name (supports partial matching) on a GameObject.
    /// Uses multiple strategies: direct type resolution, GetComponent<T>, and reflection-based search.
    /// </summary>
    public static Component? FindComponentByTypeName(GameObject gameObject, string typeName)
    {
        if (gameObject == null || string.IsNullOrEmpty(typeName))
            return null;

        // Strategy 1: Try to resolve the type and use ReflectionHelper.GetComponent
        try
        {
            Type? resolvedType = ResolveComponentType(typeName);
            if (resolvedType != null && typeof(Component).IsAssignableFrom(resolvedType))
            {
                // Use ReflectionHelper's non-generic GetComponent method
                try
                {
                    var component = ReflectionHelper.GetComponent(gameObject, resolvedType);
                    if (component != null)
                        return component;
                }
                catch
                {
                    // GetComponent failed, try alternative approach
                }
            }
        }
        catch
        {
            // Type resolution failed, continue to next strategy
        }

        // Strategy 2: Search all components by name matching (most reliable)
        Component? bestMatch = null;
        int bestMatchScore = 0;

        try
        {
            var allComponents = gameObject.GetComponents<Component>();
            foreach (var component in allComponents)
            {
                if (component == null) continue;

                var componentType = component.GetType();
                var fullTypeName = componentType.FullName ?? "";
                var shortTypeName = componentType.Name;
                var namespaceName = componentType.Namespace ?? "";

                int matchScore = 0;

                // Exact full name match (highest priority - return immediately)
                if (string.Equals(fullTypeName, typeName, StringComparison.OrdinalIgnoreCase))
                {
                    return component;
                }

                // Exact short name match (high priority)
                if (string.Equals(shortTypeName, typeName, StringComparison.OrdinalIgnoreCase))
                {
                    matchScore = 100;
                }
                // Full name ends with typeName (e.g., "ScheduleOne.NPCs.NPC" matches "NPC")
                else if (fullTypeName.EndsWith("." + typeName, StringComparison.OrdinalIgnoreCase))
                {
                    matchScore = 90;
                }
                // Full name contains (medium priority)
                else if (fullTypeName.Contains(typeName, StringComparison.OrdinalIgnoreCase))
                {
                    matchScore = 50;
                }
                // Short name contains (lower priority)
                else if (shortTypeName.Contains(typeName, StringComparison.OrdinalIgnoreCase))
                {
                    matchScore = 25;
                }
                // Namespace contains (lowest priority)
                else if (namespaceName.Contains(typeName, StringComparison.OrdinalIgnoreCase))
                {
                    matchScore = 10;
                }

                // Keep track of best match
                if (matchScore > bestMatchScore)
                {
                    bestMatch = component;
                    bestMatchScore = matchScore;
                }
            }
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"Error searching components on {gameObject.name}: {ex.Message}");
        }

        return bestMatch;
    }

    /// <summary>
    /// Resolves a component type by name, searching through all loaded assemblies.
    /// Uses TypeResolver if available, otherwise falls back to manual search.
    /// </summary>
    public static Type? ResolveComponentType(string typeName)
    {
        // Use TypeResolver first (it has caching and fuzzy matching)
        var resolved = TypeResolver.ResolveComponentType(typeName);
        if (resolved != null)
            return resolved;

        // Fallback to manual search (for backward compatibility)
        if (string.IsNullOrEmpty(typeName))
            return null;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                // Try exact full name match first
                var type = assembly.GetType(typeName, false, true);
                if (type != null && typeof(Component).IsAssignableFrom(type))
                    return type;

                // Try with case-insensitive search
                type = assembly.GetType(typeName, false, false);
                if (type != null && typeof(Component).IsAssignableFrom(type))
                    return type;

                // Search all types in assembly for partial match
                try
                {
                    var types = assembly.GetTypes();
                    foreach (var t in types)
                    {
                        if (!typeof(Component).IsAssignableFrom(t))
                            continue;

                        // Check if name matches
                        if (string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(t.FullName, typeName, StringComparison.OrdinalIgnoreCase) ||
                            (t.FullName != null && t.FullName.Contains(typeName, StringComparison.OrdinalIgnoreCase)))
                        {
                            return t;
                        }
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                    // Some types might not be loadable, continue searching
                }
            }
            catch
            {
                // Assembly might not be accessible, continue
            }
        }

        return null;
    }

    /// <summary>
    /// Finds a component by type name in the entire scene.
    /// </summary>
    public static Component? FindComponentByTypeNameInScene(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return null;

        // Search all GameObjects in the scene
        var allObjects = ReflectionHelper.FindAllGameObjects();
        foreach (var gameObject in allObjects)
        {
            var component = FindComponentByTypeName(gameObject, typeName);
            if (component != null)
                return component;
        }

        return null;
    }
}

