using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace S1MCPServer.Handlers.Debug;

/// <summary>
/// Utility class for formatting values for display and serialization.
/// </summary>
public static class ValueFormatter
{
    /// <summary>
    /// Checks if a type is IntPtr or UIntPtr (not serializable and can generally be ignored in IL2CPP).
    /// </summary>
    public static bool IsIntPtrType(Type type)
    {
        return type == typeof(IntPtr) || type == typeof(UIntPtr);
    }

    /// <summary>
    /// Checks if a member name contains "backingfield" (case-insensitive).
    /// Used to filter out compiler-generated backing fields.
    /// </summary>
    public static bool IsBackingField(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        return name.Contains("backingfield", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Formats a simple value for display (primitives, strings, Unity objects).
    /// </summary>
    public static object? FormatValue(object? value)
    {
        if (value == null)
            return "null";

        var type = value.GetType();
        
        // Skip IntPtr values - they're not serializable
        if (IsIntPtrType(type))
            return null; // Return null to signal this should be skipped

        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
            return value;

        if (value is UnityEngine.Object unityObj)
            return $"{unityObj.GetType().Name}:{unityObj.name}";

        // For other types, try to convert to string but be safe
        try
        {
            return value.ToString() ?? "null";
        }
        catch
        {
            // If ToString() fails, skip this value
            return null;
        }
    }

    /// <summary>
    /// Enhanced value formatting that handles arrays, lists, dictionaries, and nested objects.
    /// </summary>
    public static object? FormatValueDeep(object? value, int remainingDepth)
    {
        if (value == null)
            return "null";

        if (remainingDepth <= 0)
            return "[max depth reached]";

        var type = value.GetType();

        // Skip IntPtr values
        if (IsIntPtrType(type))
            return null;

        // Primitives and strings
        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
            return value;

        // Unity Objects
        if (value is UnityEngine.Object unityObj)
            return $"{unityObj.GetType().Name}:{unityObj.name}";

        // Arrays
        if (type.IsArray)
        {
            var array = value as Array;
            if (array != null)
            {
                var items = new List<object?>();
                for (int i = 0; i < Math.Min(array.Length, 50); i++) // Limit to 50 items
                {
                    items.Add(FormatValueDeep(array.GetValue(i), remainingDepth - 1));
                }
                return new Dictionary<string, object>
                {
                    ["type"] = "array",
                    ["element_type"] = type.GetElementType()?.Name ?? "unknown",
                    ["length"] = array.Length,
                    ["items"] = items,
                    ["truncated"] = array.Length > 50
                };
            }
        }

        // Lists (IList)
        if (value is System.Collections.IList list)
        {
            var items = new List<object?>();
            for (int i = 0; i < Math.Min(list.Count, 50); i++) // Limit to 50 items
            {
                items.Add(FormatValueDeep(list[i], remainingDepth - 1));
            }
            return new Dictionary<string, object>
            {
                ["type"] = "list",
                ["count"] = list.Count,
                ["items"] = items,
                ["truncated"] = list.Count > 50
            };
        }

        // Dictionaries (IDictionary)
        if (value is System.Collections.IDictionary dict)
        {
            var entries = new Dictionary<string, object?>();
            int count = 0;
            foreach (System.Collections.DictionaryEntry entry in dict)
            {
                if (count >= 50) break; // Limit to 50 entries
                var keyStr = entry.Key?.ToString() ?? "null";
                entries[keyStr] = FormatValueDeep(entry.Value, remainingDepth - 1);
                count++;
            }
            return new Dictionary<string, object>
            {
                ["type"] = "dictionary",
                ["count"] = dict.Count,
                ["entries"] = entries,
                ["truncated"] = dict.Count > 50
            };
        }

        // Enums
        if (type.IsEnum)
            return value.ToString();

        // Complex objects - try to extract key properties/fields
        try
        {
            var objData = new Dictionary<string, object>
            {
                ["type"] = type.Name,
                ["full_type"] = type.FullName ?? "Unknown"
            };

            // Get a few key properties
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var keyProps = new Dictionary<string, object?>();
            int propCount = 0;
            foreach (var prop in props)
            {
                if (propCount >= 10) break; // Limit to 10 properties
                if (prop.CanRead && prop.GetIndexParameters().Length == 0 && !IsIntPtrType(prop.PropertyType))
                {
                    try
                    {
                        var propValue = prop.GetValue(value);
                        keyProps[prop.Name] = FormatValueDeep(propValue, remainingDepth - 1);
                        propCount++;
                    }
                    catch { }
                }
            }
            if (keyProps.Count > 0)
                objData["properties"] = keyProps;

            // Get a few key fields
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            var keyFields = new Dictionary<string, object?>();
            int fieldCount = 0;
            foreach (var field in fields)
            {
                if (fieldCount >= 10) break; // Limit to 10 fields
                if (!IsIntPtrType(field.FieldType))
                {
                    try
                    {
                        var fieldValue = field.GetValue(value);
                        keyFields[field.Name] = FormatValueDeep(fieldValue, remainingDepth - 1);
                        fieldCount++;
                    }
                    catch { }
                }
            }
            if (keyFields.Count > 0)
                objData["fields"] = keyFields;

            return objData;
        }
        catch
        {
            // Fallback to ToString()
            try
            {
                return value.ToString() ?? "null";
            }
            catch
            {
                return "[unable to format]";
            }
        }
    }
}

