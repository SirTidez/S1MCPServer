using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace S1MCPServer.Utils;

/// <summary>
/// Serializes Unity objects to JSON-serializable dictionaries.
/// Handles Unity types, collections, and circular references.
/// </summary>
public static class ObjectSerializer
{
    /// <summary>
    /// Serializes an object to a dictionary representation.
    /// </summary>
    /// <param name="obj">The object to serialize.</param>
    /// <param name="maxDepth">Maximum depth for nested objects (default: 3).</param>
    /// <returns>A dictionary representation of the object.</returns>
    public static Dictionary<string, object> SerializeObject(object? obj, int maxDepth = 3)
    {
        var visited = new HashSet<object>();
        return SerializeObjectInternal(obj, maxDepth, 0, visited);
    }

    private static Dictionary<string, object> SerializeObjectInternal(object? obj, int maxDepth, int currentDepth, HashSet<object> visited)
    {
        var result = new Dictionary<string, object>();

        if (obj == null)
        {
            result["value"] = "null";
            result["type"] = "null";
            return result;
        }

        var type = obj.GetType();

        // Check for circular reference
        if (visited.Contains(obj))
        {
            result["value"] = "[Circular Reference]";
            result["type"] = type.Name;
            return result;
        }

        // Check depth limit
        if (currentDepth >= maxDepth)
        {
            result["value"] = $"[Max Depth Reached: {type.Name}]";
            result["type"] = type.Name;
            return result;
        }

        visited.Add(obj);

        try
        {
            // Handle Unity types
            if (obj is Vector3 vector3)
            {
                result["value"] = new Dictionary<string, object>
                {
                    ["x"] = vector3.x,
                    ["y"] = vector3.y,
                    ["z"] = vector3.z
                };
                result["type"] = "Vector3";
                return result;
            }

            if (obj is Vector2 vector2)
            {
                result["value"] = new Dictionary<string, object>
                {
                    ["x"] = vector2.x,
                    ["y"] = vector2.y
                };
                result["type"] = "Vector2";
                return result;
            }

            if (obj is Quaternion quaternion)
            {
                result["value"] = new Dictionary<string, object>
                {
                    ["x"] = quaternion.x,
                    ["y"] = quaternion.y,
                    ["z"] = quaternion.z,
                    ["w"] = quaternion.w
                };
                result["type"] = "Quaternion";
                return result;
            }

            if (obj is Color color)
            {
                result["value"] = new Dictionary<string, object>
                {
                    ["r"] = color.r,
                    ["g"] = color.g,
                    ["b"] = color.b,
                    ["a"] = color.a
                };
                result["type"] = "Color";
                return result;
            }

            if (obj is Rect rect)
            {
                result["value"] = new Dictionary<string, object>
                {
                    ["x"] = rect.x,
                    ["y"] = rect.y,
                    ["width"] = rect.width,
                    ["height"] = rect.height
                };
                result["type"] = "Rect";
                return result;
            }

            // Handle Unity Object types
            if (obj is UnityEngine.Object unityObj)
            {
                result["value"] = new Dictionary<string, object>
                {
                    ["name"] = unityObj.name ?? "null",
                    ["type"] = unityObj.GetType().Name,
                    ["active"] = unityObj is GameObject go ? go.activeSelf : (unityObj is Component comp ? comp.gameObject.activeSelf : false)
                };
                result["type"] = unityObj.GetType().Name;
                return result;
            }

            // Handle collections
            if (obj is IEnumerable enumerable && !(obj is string))
            {
                var items = new List<object>();
                int count = 0;
                foreach (var item in enumerable)
                {
                    if (count++ >= 100) // Limit collection size
                    {
                        items.Add("[Collection truncated at 100 items]");
                        break;
                    }
                    items.Add(SerializeValue(item, maxDepth, currentDepth + 1, visited));
                }
                result["value"] = items;
                result["type"] = type.Name;
                result["count"] = count;
                return result;
            }

            // Handle dictionaries
            if (obj is IDictionary dictionary)
            {
                var dictResult = new Dictionary<string, object>();
                int count = 0;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (count++ >= 100) // Limit dictionary size
                    {
                        dictResult["[truncated]"] = "[Dictionary truncated at 100 items]";
                        break;
                    }
                    var key = entry.Key?.ToString() ?? "null";
                    dictResult[key] = SerializeValue(entry.Value, maxDepth, currentDepth + 1, visited);
                }
                result["value"] = dictResult;
                result["type"] = type.Name;
                result["count"] = count;
                return result;
            }

            // Handle primitives and strings
            if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
            {
                result["value"] = obj;
                result["type"] = type.Name;
                return result;
            }

            // Handle enums
            if (type.IsEnum)
            {
                result["value"] = obj.ToString();
                result["type"] = type.Name;
                return result;
            }

            // Default: try to convert to string
            try
            {
                result["value"] = obj.ToString() ?? "null";
                result["type"] = type.Name;
            }
            catch
            {
                result["value"] = $"[Unable to serialize: {type.Name}]";
                result["type"] = type.Name;
            }
        }
        finally
        {
            visited.Remove(obj);
        }

        return result;
    }

    private static object SerializeValue(object? obj, int maxDepth, int currentDepth, HashSet<object> visited)
    {
        if (obj == null)
            return "null";

        var type = obj.GetType();

        // Primitives and strings
        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
            return obj;

        // Unity types (simplified representation)
        if (obj is Vector3 v3)
            return $"Vector3({v3.x}, {v3.y}, {v3.z})";
        if (obj is Vector2 v2)
            return $"Vector2({v2.x}, {v2.y})";
        if (obj is Quaternion q)
            return $"Quaternion({q.x}, {q.y}, {q.z}, {q.w})";
        if (obj is Color c)
            return $"Color({c.r}, {c.g}, {c.b}, {c.a})";

        // Unity Objects
        if (obj is UnityEngine.Object uo)
            return $"{uo.GetType().Name}:{uo.name}";

        // Collections - serialize as array
        if (obj is IEnumerable enumerable && !(obj is string))
        {
            var items = new List<object>();
            int count = 0;
            foreach (var item in enumerable)
            {
                if (count++ >= 50) // Limit for nested collections
                {
                    items.Add("[...]");
                    break;
                }
                items.Add(SerializeValue(item, maxDepth, currentDepth + 1, visited));
            }
            return items;
        }

        // Default: string representation
        try
        {
            return obj.ToString() ?? "null";
        }
        catch
        {
            return $"[{type.Name}]";
        }
    }

    /// <summary>
    /// Serializes a GameObject to a dictionary representation.
    /// </summary>
    /// <param name="gameObject">The GameObject to serialize.</param>
    /// <param name="maxDepth">Maximum depth for nested objects.</param>
    /// <returns>A dictionary representation of the GameObject.</returns>
    public static Dictionary<string, object> SerializeGameObject(GameObject gameObject, int maxDepth = 3)
    {
        var result = new Dictionary<string, object>
        {
            ["name"] = gameObject.name,
            ["active"] = gameObject.activeSelf,
            ["activeInHierarchy"] = gameObject.activeInHierarchy,
            ["layer"] = gameObject.layer,
            ["tag"] = gameObject.tag,
            ["scene"] = gameObject.scene.name
        };

        // Get transform info
        var transform = gameObject.transform;
        result["position"] = SerializeObject(transform.position, maxDepth);
        result["rotation"] = SerializeObject(transform.rotation, maxDepth);
        result["scale"] = SerializeObject(transform.localScale, maxDepth);

        // Get component types (names only to avoid deep serialization)
        var components = new List<string>();
        foreach (var component in gameObject.GetComponents<Component>())
        {
            if (component != null)
                components.Add(component.GetType().Name);
        }
        result["components"] = components;
        result["component_count"] = components.Count;

        return result;
    }

    /// <summary>
    /// Serializes a Component to a dictionary representation.
    /// </summary>
    /// <param name="component">The Component to serialize.</param>
    /// <param name="maxDepth">Maximum depth for nested objects.</param>
    /// <returns>A dictionary representation of the Component.</returns>
    public static Dictionary<string, object> SerializeComponent(Component component, int maxDepth = 3)
    {
        var result = new Dictionary<string, object>
        {
            ["type"] = component.GetType().Name,
            ["full_type"] = component.GetType().FullName ?? "Unknown",
            ["gameObject"] = component.gameObject.name,
            ["enabled"] = component is MonoBehaviour mb ? mb.enabled : true
        };

        return result;
    }

    /// <summary>
    /// Serializes a Type to a dictionary representation.
    /// </summary>
    /// <param name="type">The Type to serialize.</param>
    /// <returns>A dictionary representation of the Type.</returns>
    public static Dictionary<string, object> SerializeType(Type type)
    {
        var result = new Dictionary<string, object>
        {
            ["name"] = type.Name,
            ["full_name"] = type.FullName ?? "Unknown",
            ["namespace"] = type.Namespace ?? "Unknown",
            ["is_class"] = type.IsClass,
            ["is_interface"] = type.IsInterface,
            ["is_enum"] = type.IsEnum,
            ["is_abstract"] = type.IsAbstract,
            ["is_sealed"] = type.IsSealed,
            ["base_type"] = type.BaseType?.FullName ?? "None"
        };

        // Get interfaces
        var interfaces = type.GetInterfaces().Select(i => i.FullName ?? i.Name).ToList();
        result["interfaces"] = interfaces;

        return result;
    }

    /// <summary>
    /// Formats a value for display (simplified version for quick formatting).
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <returns>A formatted string representation.</returns>
    public static string FormatValue(object? value)
    {
        if (value == null)
            return "null";

        var type = value.GetType();

        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
            return value.ToString() ?? "null";

        if (value is Vector3 v3)
            return $"({v3.x:F2}, {v3.y:F2}, {v3.z:F2})";
        if (value is Vector2 v2)
            return $"({v2.x:F2}, {v2.y:F2})";
        if (value is Quaternion q)
            return $"({q.x:F2}, {q.y:F2}, {q.z:F2}, {q.w:F2})";
        if (value is Color c)
            return $"RGBA({c.r:F2}, {c.g:F2}, {c.b:F2}, {c.a:F2})";

        if (value is UnityEngine.Object uo)
            return $"{uo.GetType().Name}:{uo.name}";

        if (value is IEnumerable enumerable && !(value is string))
        {
            var count = 0;
            foreach (var _ in enumerable)
            {
                count++;
                if (count > 10)
                    return $"[Collection with {count}+ items]";
            }
            return $"[Collection with {count} items]";
        }

        return value.ToString() ?? "null";
    }
}

