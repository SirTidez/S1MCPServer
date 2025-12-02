using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using S1MCPServer.Utils;
#if !MONO
using Il2CppInterop.Runtime;
using Object = Il2CppSystem.Object;
#endif

namespace S1MCPServer.Utils;

/// <summary>
/// Centralized reflection engine with caching for performance.
/// Provides type resolution, member access, and method invocation capabilities.
/// </summary>
public static class ReflectionEngine
{
    private static readonly Dictionary<string, Type> _typeCache = new();
    private static readonly Dictionary<Type, MemberInfo[]> _memberCache = new();
    private static readonly Dictionary<Type, FieldInfo[]> _fieldCache = new();
    private static readonly Dictionary<Type, PropertyInfo[]> _propertyCache = new();
    private static readonly Dictionary<Type, MethodInfo[]> _methodCache = new();

    /// <summary>
    /// Resolves a type name to a Type object with fuzzy matching support.
    /// Uses caching to avoid repeated lookups.
    /// </summary>
    /// <param name="typeName">The type name to resolve (can be partial).</param>
    /// <param name="fuzzyMatch">If true, performs partial name matching.</param>
    /// <returns>The resolved Type, or null if not found.</returns>
    public static Type? ResolveType(string typeName, bool fuzzyMatch = true)
    {
        if (string.IsNullOrEmpty(typeName))
            return null;

        // Check cache first
        if (_typeCache.TryGetValue(typeName, out var cachedType))
            return cachedType;

        // Try exact match first using CrossRuntimeTypeHelper
        var type = CrossRuntimeTypeHelper.ResolveType(typeName);
        if (type != null)
        {
            _typeCache[typeName] = type;
            return type;
        }

        // Try fuzzy matching if enabled
        if (fuzzyMatch)
        {
            var matches = CrossRuntimeTypeHelper.FindTypesByName(typeName);
            if (matches.Count == 1)
            {
                type = matches[0];
                _typeCache[typeName] = type;
                return type;
            }
            else if (matches.Count > 1)
            {
                // Multiple matches - prefer Component types
                var componentMatch = matches.FirstOrDefault(t => typeof(Component).IsAssignableFrom(t));
                if (componentMatch != null)
                {
                    type = componentMatch;
                    _typeCache[typeName] = type;
                    return type;
                }
                // Return first match if no Component found
                type = matches[0];
                _typeCache[typeName] = type;
                return type;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets all members (fields, properties, methods) for a type with caching.
    /// </summary>
    /// <param name="type">The type to get members for.</param>
    /// <param name="includePrivate">Whether to include private members.</param>
    /// <returns>Array of MemberInfo objects.</returns>
    public static MemberInfo[] GetMembers(Type type, bool includePrivate = false)
    {
        if (type == null)
            return Array.Empty<MemberInfo>();

        var cacheKey = $"{type.FullName}_{includePrivate}";
        if (_memberCache.TryGetValue(type, out var cached))
            return cached;

        var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
        if (includePrivate)
            flags |= BindingFlags.NonPublic;

        var members = type.GetMembers(flags);
        _memberCache[type] = members;
        return members;
    }

    /// <summary>
    /// Gets all fields for a type with caching.
    /// </summary>
    /// <param name="type">The type to get fields for.</param>
    /// <param name="includePrivate">Whether to include private fields.</param>
    /// <returns>Array of FieldInfo objects.</returns>
    public static FieldInfo[] GetFields(Type type, bool includePrivate = false)
    {
        if (type == null)
            return Array.Empty<FieldInfo>();

        var cacheKey = $"{type.FullName}_{includePrivate}";
        if (_fieldCache.TryGetValue(type, out var cached))
            return cached;

        var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
        if (includePrivate)
            flags |= BindingFlags.NonPublic;

        var fields = type.GetFields(flags);
        _fieldCache[type] = fields;
        return fields;
    }

    /// <summary>
    /// Gets all properties for a type with caching.
    /// </summary>
    /// <param name="type">The type to get properties for.</param>
    /// <param name="includePrivate">Whether to include private properties.</param>
    /// <returns>Array of PropertyInfo objects.</returns>
    public static PropertyInfo[] GetProperties(Type type, bool includePrivate = false)
    {
        if (type == null)
            return Array.Empty<PropertyInfo>();

        var cacheKey = $"{type.FullName}_{includePrivate}";
        if (_propertyCache.TryGetValue(type, out var cached))
            return cached;

        var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
        if (includePrivate)
            flags |= BindingFlags.NonPublic;

        var properties = type.GetProperties(flags);
        _propertyCache[type] = properties;
        return properties;
    }

    /// <summary>
    /// Gets all methods for a type with caching.
    /// </summary>
    /// <param name="type">The type to get methods for.</param>
    /// <param name="includePrivate">Whether to include private methods.</param>
    /// <returns>Array of MethodInfo objects.</returns>
    public static MethodInfo[] GetMethods(Type type, bool includePrivate = false)
    {
        if (type == null)
            return Array.Empty<MethodInfo>();

        var cacheKey = $"{type.FullName}_{includePrivate}";
        if (_methodCache.TryGetValue(type, out var cached))
            return cached;

        var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
        if (includePrivate)
            flags |= BindingFlags.NonPublic;

        var methods = type.GetMethods(flags);
        _methodCache[type] = methods;
        return methods;
    }

    /// <summary>
    /// Gets a field or property value from an object.
    /// Uses TryGetFieldOrProperty to handle Mono/IL2CPP differences (fields on Mono are typically properties on IL2CPP).
    /// </summary>
    /// <param name="obj">The object to get the member from.</param>
    /// <param name="memberName">The name of the field or property.</param>
    /// <returns>The member value, or null if not found.</returns>
    public static object? GetFieldValue(object obj, string memberName)
    {
        return ReflectionHelper.TryGetFieldOrProperty(obj, memberName);
    }

    /// <summary>
    /// Sets a field or property value on an object.
    /// Uses TrySetFieldOrProperty to handle Mono/IL2CPP differences (fields on Mono are typically properties on IL2CPP).
    /// </summary>
    /// <param name="obj">The object to set the member on.</param>
    /// <param name="memberName">The name of the field or property.</param>
    /// <param name="value">The value to set.</param>
    /// <returns>True if the member was set successfully, false otherwise.</returns>
    public static bool SetFieldValue(object obj, string memberName, object? value)
    {
        return ReflectionHelper.TrySetFieldOrProperty(obj, memberName, value);
    }

    /// <summary>
    /// Gets a field or property value from an object.
    /// Uses TryGetFieldOrProperty to handle Mono/IL2CPP differences (fields on Mono are typically properties on IL2CPP).
    /// </summary>
    /// <param name="obj">The object to get the member from.</param>
    /// <param name="memberName">The name of the field or property.</param>
    /// <returns>The member value, or null if not found.</returns>
    public static object? GetPropertyValue(object obj, string memberName)
    {
        return ReflectionHelper.TryGetFieldOrProperty(obj, memberName);
    }

    /// <summary>
    /// Sets a field or property value on an object.
    /// Uses TrySetFieldOrProperty to handle Mono/IL2CPP differences (fields on Mono are typically properties on IL2CPP).
    /// </summary>
    /// <param name="obj">The object to set the member on.</param>
    /// <param name="memberName">The name of the field or property.</param>
    /// <param name="value">The value to set.</param>
    /// <returns>True if the member was set successfully, false otherwise.</returns>
    public static bool SetPropertyValue(object obj, string memberName, object? value)
    {
        return ReflectionHelper.TrySetFieldOrProperty(obj, memberName, value);
    }

    /// <summary>
    /// Invokes a method on an object.
    /// </summary>
    /// <param name="obj">The object to invoke the method on (null for static methods).</param>
    /// <param name="methodName">The name of the method.</param>
    /// <param name="parameters">The method parameters.</param>
    /// <returns>The method return value, or null if void or failed.</returns>
    public static object? InvokeMethod(object? obj, string methodName, object[]? parameters = null)
    {
        if (string.IsNullOrEmpty(methodName))
            return null;

        parameters ??= Array.Empty<object>();

        Type? type;
        if (obj != null)
            type = obj.GetType();
        else
            return null; // Static methods require type resolution, use InvokeStaticMethod instead

        try
        {
            // Find method by name and parameter count
            var methods = GetMethods(type, true);
            var matchingMethods = methods.Where(m => 
                m.Name == methodName && 
                m.GetParameters().Length == parameters.Length
            ).ToList();

            if (matchingMethods.Count == 0)
                return null;

            // Try to find best match by parameter types
            MethodInfo? bestMatch = null;
            foreach (var method in matchingMethods)
            {
                var paramTypes = method.GetParameters().Select(p => p.ParameterType).ToArray();
                bool matches = true;
                for (int i = 0; i < paramTypes.Length; i++)
                {
                    if (parameters[i] != null && !paramTypes[i].IsAssignableFrom(parameters[i].GetType()))
                    {
                        matches = false;
                        break;
                    }
                }
                if (matches)
                {
                    bestMatch = method;
                    break;
                }
            }

            // Use first match if no perfect match found
            bestMatch ??= matchingMethods[0];

            return bestMatch.Invoke(obj, parameters);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Invokes a static method on a type.
    /// </summary>
    /// <param name="typeName">The name of the type.</param>
    /// <param name="methodName">The name of the method.</param>
    /// <param name="parameters">The method parameters.</param>
    /// <returns>The method return value, or null if void or failed.</returns>
    public static object? InvokeStaticMethod(string typeName, string methodName, object[]? parameters = null)
    {
        var type = ResolveType(typeName);
        if (type == null)
            return null;

        return InvokeMethod(null, methodName, parameters);
    }

    /// <summary>
    /// Finds a method by name and parameter types.
    /// </summary>
    /// <param name="type">The type to search.</param>
    /// <param name="methodName">The method name.</param>
    /// <param name="parameterTypes">The parameter types (null to match any).</param>
    /// <returns>The MethodInfo if found, null otherwise.</returns>
    public static MethodInfo? FindMethod(Type type, string methodName, Type[]? parameterTypes = null)
    {
        if (type == null || string.IsNullOrEmpty(methodName))
            return null;

        var methods = GetMethods(type, true);
        foreach (var method in methods)
        {
            if (method.Name != methodName)
                continue;

            if (parameterTypes == null)
                return method;

            var methodParams = method.GetParameters();
            if (methodParams.Length != parameterTypes.Length)
                continue;

            bool matches = true;
            for (int i = 0; i < parameterTypes.Length; i++)
            {
                if (methodParams[i].ParameterType != parameterTypes[i])
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return method;
        }

        return null;
    }

    /// <summary>
    /// Clears all caches. Useful if types are dynamically loaded.
    /// </summary>
    public static void ClearCache()
    {
        _typeCache.Clear();
        _memberCache.Clear();
        _fieldCache.Clear();
        _propertyCache.Clear();
        _methodCache.Clear();
    }
}

