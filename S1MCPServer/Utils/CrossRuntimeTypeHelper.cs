using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
#if !MONO
using Il2CppInterop.Runtime;
using Object = Il2CppSystem.Object;
#endif

namespace S1MCPServer.Utils;

/// <summary>
/// Provides cross-runtime type utilities.
/// Handles type resolution, casting, and compatibility between Mono and IL2CPP.
/// </summary>
public static class CrossRuntimeTypeHelper
{
    /// <summary>
    /// Resolves a type name to a Type object, handling both Mono and IL2CPP naming conventions.
    /// </summary>
    /// <param name="typeName">The full type name to resolve.</param>
    /// <returns>The resolved Type, or null if not found.</returns>
    public static Type? ResolveType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return null;

        // Try standard type resolution
        Type? type = Type.GetType(typeName);
        if (type != null)
            return type;

        // Search all loaded assemblies
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                type = assembly.GetType(typeName);
                if (type != null)
                    return type;

                // Try with IL2CPP namespace prefix if in IL2CPP mode
#if !MONO
                var il2CppTypeName = "Il2Cpp" + typeName;
                type = assembly.GetType(il2CppTypeName);
                if (type != null)
                    return type;
#endif
            }
            catch
            {
                // Continue searching
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the runtime-appropriate type name (handles Mono vs IL2CPP naming).
    /// </summary>
    /// <param name="type">The type to get the name for.</param>
    /// <returns>The type name appropriate for the current runtime.</returns>
    public static string GetRuntimeTypeName(Type type)
    {
        if (type == null)
            return "Unknown";

        var fullName = type.FullName ?? type.Name;

#if !MONO
        // In IL2CPP, remove Il2Cpp prefix for consistency
        if (fullName.StartsWith("Il2Cpp"))
        {
            fullName = fullName.Substring(6); // Remove "Il2Cpp" prefix
        }
#endif

        return fullName;
    }

    /// <summary>
    /// Safely casts an object to type T, handling both Mono and IL2CPP.
    /// </summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="obj">The object to cast.</param>
    /// <param name="result">The cast object if successful.</param>
    /// <returns>True if the cast was successful, false otherwise.</returns>
    public static bool TryCast<T>(object? obj, out T? result)
#if !MONO
        where T : Object
#else
        where T : class
#endif
    {
        result = null;

        if (obj == null)
            return false;

#if !MONO
        // IL2CPP: Use Il2CppInterop for casting
        if (obj is Object il2CppObj)
        {
            // Try direct TryCast - it handles type checking internally
            try
            {
                result = il2CppObj.TryCast<T>();
                return result != null;
            }
            catch
            {
                // Casting failed
            }
        }
#else
        // Mono: Use standard C# pattern matching
        if (obj is T t)
        {
            result = t;
            return true;
        }

        // Fallback: Try explicit cast
        try
        {
            result = (T)obj;
            return true;
        }
        catch
        {
            // Casting failed
        }
#endif

        return false;
    }

    /// <summary>
    /// Checks if a type is assignable from another type, handling IL2CPP compatibility.
    /// </summary>
    /// <param name="targetType">The target type.</param>
    /// <param name="sourceType">The source type.</param>
    /// <returns>True if sourceType can be assigned to targetType.</returns>
    public static bool IsAssignableFrom(Type targetType, Type sourceType)
    {
        if (targetType == null || sourceType == null)
            return false;

        // Standard type checking
        if (targetType.IsAssignableFrom(sourceType))
            return true;

#if !MONO
        // IL2CPP-specific type checking: use Il2CppType.Of to get Il2CppType objects
        try
        {
            // Use reflection to call Il2CppType.Of<T>() for both types
            var ofMethod = typeof(Il2CppType).GetMethod("Of", BindingFlags.Public | BindingFlags.Static);
            if (ofMethod != null)
            {
                var targetGenericMethod = ofMethod.MakeGenericMethod(targetType);
                var sourceGenericMethod = ofMethod.MakeGenericMethod(sourceType);
                
                var targetIl2CppType = targetGenericMethod.Invoke(null, new object[] { false }) as Type;
                var sourceIl2CppType = sourceGenericMethod.Invoke(null, new object[] { false }) as Type;
                
                if (targetIl2CppType != null && sourceIl2CppType != null)
                {
                    return targetIl2CppType.IsAssignableFrom(sourceIl2CppType);
                }
            }
        }
        catch
        {
            // Fallback to standard check
        }
#endif

        return false;
    }

    /// <summary>
    /// Gets generic type arguments from a type.
    /// </summary>
    /// <param name="type">The type to extract arguments from.</param>
    /// <returns>Array of generic type arguments.</returns>
    public static Type[] GetGenericTypeArguments(Type type)
    {
        if (type == null || !type.IsGenericType)
            return Array.Empty<Type>();

        return type.GetGenericArguments();
    }

    /// <summary>
    /// Finds a type in all loaded assemblies by name (partial match).
    /// </summary>
    /// <param name="typeName">The type name to search for (can be partial).</param>
    /// <returns>List of matching types.</returns>
    public static List<Type> FindTypesByName(string typeName)
    {
        var results = new List<Type>();

        if (string.IsNullOrEmpty(typeName))
            return results;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var types = assembly.GetTypes();
                foreach (var type in types)
                {
                    if (type.Name.Contains(typeName, StringComparison.OrdinalIgnoreCase) ||
                        (type.FullName != null && type.FullName.Contains(typeName, StringComparison.OrdinalIgnoreCase)))
                    {
                        results.Add(type);
                    }
                }
            }
            catch
            {
                // Skip assemblies that can't be queried
            }
        }

        return results;
    }

    /// <summary>
    /// Gets all types from a namespace.
    /// </summary>
    /// <param name="namespaceName">The namespace to search in.</param>
    /// <returns>List of types in the namespace.</returns>
    public static List<Type> GetTypesInNamespace(string namespaceName)
    {
        var results = new List<Type>();

        if (string.IsNullOrEmpty(namespaceName))
            return results;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var types = assembly.GetTypes()
                    .Where(t => t.Namespace == namespaceName)
                    .ToList();
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
    /// Checks if a type is a Unity Object type (handles both Mono and IL2CPP).
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>True if the type is a Unity Object.</returns>
    public static bool IsUnityObjectType(Type type)
    {
        if (type == null)
            return false;

        return typeof(UnityEngine.Object).IsAssignableFrom(type);
    }

    /// <summary>
    /// Gets the base type chain for a type.
    /// </summary>
    /// <param name="type">The type to get the chain for.</param>
    /// <returns>List of base types from most derived to least derived.</returns>
    public static List<Type> GetBaseTypeChain(Type type)
    {
        var chain = new List<Type>();

        if (type == null)
            return chain;

        var current = type.BaseType;
        while (current != null)
        {
            chain.Add(current);
            current = current.BaseType;
        }

        return chain;
    }
}

