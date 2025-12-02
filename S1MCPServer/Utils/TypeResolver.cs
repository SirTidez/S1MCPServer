using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using S1MCPServer.Utils;

namespace S1MCPServer.Utils;

/// <summary>
/// Type discovery and resolution utility.
/// Scans assemblies for Component types and provides fuzzy matching.
/// </summary>
public static class TypeResolver
{
    private static readonly List<Type> _componentTypes = new();
    private static readonly Dictionary<string, Type> _typeNameCache = new();
    private static bool _initialized = false;

    /// <summary>
    /// Initializes the type resolver by scanning all loaded assemblies for Component types.
    /// Should be called once on startup.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
            return;

        ModLogger.Debug("Initializing TypeResolver - scanning assemblies for Component types...");

        _componentTypes.Clear();
        _typeNameCache.Clear();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var types = assembly.GetTypes();
                foreach (var type in types)
                {
                    try
                    {
                        // Check if type is a Component (or can be assigned to Component)
                        if (typeof(Component).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface)
                        {
                            _componentTypes.Add(type);
                            
                            // Cache by name (without namespace)
                            var name = type.Name;
                            if (!_typeNameCache.ContainsKey(name))
                                _typeNameCache[name] = type;
                            
                            // Also cache by full name
                            var fullName = type.FullName;
                            if (!string.IsNullOrEmpty(fullName) && !_typeNameCache.ContainsKey(fullName))
                                _typeNameCache[fullName] = type;
                        }
                    }
                    catch
                    {
                        // Skip types that can't be checked
                    }
                }
            }
            catch
            {
                // Skip assemblies that can't be queried
            }
        }

        _initialized = true;
        ModLogger.Debug($"TypeResolver initialized - found {_componentTypes.Count} Component types");
    }

    /// <summary>
    /// Gets all cached Component types.
    /// </summary>
    /// <returns>List of Component types.</returns>
    public static List<Type> GetComponentTypes()
    {
        if (!_initialized)
            Initialize();

        return new List<Type>(_componentTypes);
    }

    /// <summary>
    /// Searches for types by name pattern (partial matching).
    /// </summary>
    /// <param name="pattern">The pattern to search for (case-insensitive).</param>
    /// <param name="componentTypesOnly">If true, only searches Component types.</param>
    /// <returns>List of matching types.</returns>
    public static List<Type> SearchTypes(string pattern, bool componentTypesOnly = false)
    {
        if (string.IsNullOrEmpty(pattern))
            return new List<Type>();

        var results = new List<Type>();
        var searchIn = componentTypesOnly ? GetComponentTypes() : GetAllTypes();

        foreach (var type in searchIn)
        {
            if (type.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase) ||
                (type.FullName != null && type.FullName.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
            {
                results.Add(type);
            }
        }

        return results;
    }

    /// <summary>
    /// Resolves a Component type by name with fuzzy matching.
    /// </summary>
    /// <param name="typeName">The type name to resolve (can be partial).</param>
    /// <returns>The resolved Type, or null if not found.</returns>
    public static Type? ResolveComponentType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return null;

        if (!_initialized)
            Initialize();

        // Try exact match first
        if (_typeNameCache.TryGetValue(typeName, out var exactMatch))
            return exactMatch;

        // Try with full namespace using CrossRuntimeTypeHelper
        var resolved = CrossRuntimeTypeHelper.ResolveType(typeName);
        if (resolved != null && typeof(Component).IsAssignableFrom(resolved))
            return resolved;

        // Try fuzzy matching on Component types
        var matches = SearchTypes(typeName, componentTypesOnly: true);
        if (matches.Count == 1)
            return matches[0];
        
        if (matches.Count > 1)
        {
            // Multiple matches - prefer exact name match
            var exactNameMatch = matches.FirstOrDefault(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
            if (exactNameMatch != null)
                return exactNameMatch;
            
            // Return first match
            return matches[0];
        }

        return null;
    }

    /// <summary>
    /// Gets all types from all loaded assemblies.
    /// </summary>
    /// <returns>List of all types.</returns>
    private static List<Type> GetAllTypes()
    {
        var results = new List<Type>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var types = assembly.GetTypes();
                results.AddRange(types);
            }
            catch
            {
                // Skip assemblies that can't be queried
            }
        }

        return results;
    }

    /// <summary>
    /// Gets types from a specific namespace.
    /// </summary>
    /// <param name="namespaceName">The namespace to search in.</param>
    /// <param name="componentTypesOnly">If true, only returns Component types.</param>
    /// <returns>List of types in the namespace.</returns>
    public static List<Type> GetTypesInNamespace(string namespaceName, bool componentTypesOnly = false)
    {
        var results = new List<Type>();
        var searchIn = componentTypesOnly ? GetComponentTypes() : GetAllTypes();

        foreach (var type in searchIn)
        {
            if (type.Namespace == namespaceName)
                results.Add(type);
        }

        return results;
    }

    /// <summary>
    /// Gets suggested type names when a type is not found.
    /// </summary>
    /// <param name="typeName">The type name that was not found.</param>
    /// <param name="maxSuggestions">Maximum number of suggestions to return.</param>
    /// <returns>List of suggested type names.</returns>
    public static List<string> GetSuggestions(string typeName, int maxSuggestions = 5)
    {
        if (string.IsNullOrEmpty(typeName))
            return new List<string>();

        if (!_initialized)
            Initialize();

        // Calculate similarity scores
        var suggestions = new List<(Type type, int score)>();

        foreach (var type in _componentTypes)
        {
            int score = 0;
            var name = type.Name;
            var fullName = type.FullName ?? "";

            // Exact name match gets highest score
            if (name.Equals(typeName, StringComparison.OrdinalIgnoreCase))
                score = 1000;
            // Contains match gets medium score
            else if (name.Contains(typeName, StringComparison.OrdinalIgnoreCase))
                score = 100;
            // Full name contains match gets lower score
            else if (fullName.Contains(typeName, StringComparison.OrdinalIgnoreCase))
                score = 50;
            // Starts with match gets bonus
            if (name.StartsWith(typeName, StringComparison.OrdinalIgnoreCase))
                score += 20;

            if (score > 0)
                suggestions.Add((type, score));
        }

        // Sort by score descending and return top matches
        return suggestions
            .OrderByDescending(s => s.score)
            .Take(maxSuggestions)
            .Select(s => s.type.Name)
            .ToList();
    }

    /// <summary>
    /// Clears the cache and reinitializes. Useful if new assemblies are loaded.
    /// </summary>
    public static void Refresh()
    {
        _initialized = false;
        Initialize();
    }
}

