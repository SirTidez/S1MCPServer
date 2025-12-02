using System.Linq;
using System.Reflection;
using UnityEngine;
#if !MONO
using Il2CppInterop.Runtime;
using Object = Il2CppSystem.Object;
#endif

namespace S1MCPServer.Utils;

/// <summary>
/// Provides reflection utilities for accessing Unity GameObjects and components.
/// Handles both Mono and IL2CPP backends using conditional compilation.
/// </summary>
public static class ReflectionHelper
{
    /// <summary>
    /// Finds a GameObject by name.
    /// </summary>
    /// <param name="name">The name of the GameObject to find.</param>
    /// <param name="includeDisabled">If true, includes inactive GameObjects in the search.</param>
    /// <returns>The GameObject if found, null otherwise.</returns>
    public static GameObject? FindGameObject(string name, bool includeDisabled = false)
    {
        if (!includeDisabled)
        {
            // Use GameObject.Find for active objects only
            return GameObject.Find(name);
        }
        else
        {
            // Find all objects in scene, including inactive ones
            var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (var obj in allObjects)
            {
                if (obj.name == name)
                {
                    return obj;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Gets a component of type T from a GameObject (handles both Mono and IL2CPP).
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    /// <param name="obj">The GameObject to get the component from.</param>
    /// <returns>The component if found, null otherwise.</returns>
    public static T? GetComponent<T>(GameObject obj) where T : Component
    {
        if (obj == null)
        {
            return null;
        }

        return obj.GetComponent<T>();
    }

    /// <summary>
    /// Gets a component of the specified type from a GameObject (non-generic version).
    /// </summary>
    /// <param name="obj">The GameObject to get the component from.</param>
    /// <param name="componentType">The type of component to get.</param>
    /// <returns>The component if found, null otherwise.</returns>
    public static Component? GetComponent(GameObject obj, Type componentType)
    {
        if (obj == null || componentType == null || !typeof(Component).IsAssignableFrom(componentType))
        {
            return null;
        }

        // Use reflection to call the generic GetComponent<T> method
        try
        {
            var method = typeof(ReflectionHelper).GetMethods()
                .FirstOrDefault(m => m.Name == "GetComponent" && 
                                    m.IsGenericMethod && 
                                    m.GetParameters().Length == 1 &&
                                    m.GetParameters()[0].ParameterType == typeof(GameObject));
            
            if (method != null)
            {
                var genericMethod = method.MakeGenericMethod(componentType);
                var component = genericMethod.Invoke(null, new object[] { obj });
                return component as Component;
            }
        }
        catch
        {
            // Fallback: Try Unity's non-generic GetComponent(Type) if available
            try
            {
                var unityMethod = typeof(GameObject).GetMethod("GetComponent", new[] { typeof(Type) });
                if (unityMethod != null)
                {
                    var component = unityMethod.Invoke(obj, new object[] { componentType });
                    return component as Component;
                }
            }
            catch
            {
                // Method not available
            }
        }

        return null;
    }

    /// <summary>
    /// Gets all components of type T from a GameObject and its children.
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    /// <param name="obj">The GameObject to search.</param>
    /// <returns>List of components found.</returns>
    public static List<T> GetAllComponents<T>(GameObject obj) where T : Component
    {
        var results = new List<T>();
        if (obj == null)
        {
            return results;
        }

        // Get component from this object
        var component = GetComponent<T>(obj);
        if (component != null)
        {
            results.Add(component);
        }

        // Get components from children
        for (int i = 0; i < obj.transform.childCount; i++)
        {
            var child = obj.transform.GetChild(i);
            results.AddRange(GetAllComponents<T>(child.gameObject));
        }

        return results;
    }

    /// <summary>
    /// Gets the value of a private field from an object using reflection.
    /// </summary>
    /// <param name="obj">The object to get the field from.</param>
    /// <param name="fieldName">The name of the field.</param>
    /// <returns>The field value, or null if not found.</returns>
    public static object? GetPrivateField(object obj, string fieldName)
    {
        if (obj == null)
        {
            return null;
        }

        var field = obj.GetType().GetField(
            fieldName,
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
        );

        return field?.GetValue(obj);
    }

    /// <summary>
    /// Gets the value of a private property from an object using reflection.
    /// Uses TryGetFieldOrProperty to handle Mono/IL2CPP differences.
    /// </summary>
    /// <param name="obj">The object to get the property from.</param>
    /// <param name="propertyName">The name of the property.</param>
    /// <returns>The property value, or null if not found.</returns>
    public static object? GetPrivateProperty(object obj, string propertyName)
    {
        // Use TryGetFieldOrProperty to handle Mono/IL2CPP differences
        return TryGetFieldOrProperty(obj, propertyName);
    }

    /// <summary>
    /// Gets the object hierarchy (children) of a GameObject.
    /// </summary>
    /// <param name="obj">The GameObject to get the hierarchy from.</param>
    /// <returns>List of child GameObject names.</returns>
    public static List<string> GetObjectHierarchy(GameObject obj)
    {
        var hierarchy = new List<string>();
        if (obj == null)
        {
            return hierarchy;
        }

        for (int i = 0; i < obj.transform.childCount; i++)
        {
            var child = obj.transform.GetChild(i);
            hierarchy.Add(child.name);
        }

        return hierarchy;
    }

    /// <summary>
    /// Attempts to get a field or property value from an object using reflection.
    /// Tries field first, then property. Handles both public and non-public members.
    /// Fields on Mono are typically properties on IL2CPP.
    /// </summary>
    /// <param name="target">The target object to get the member from.</param>
    /// <param name="memberName">The name of the field or property.</param>
    /// <returns>The value of the member, or null if not found or inaccessible.</returns>
    public static object? TryGetFieldOrProperty(object target, string memberName)
    {
        if (target == null) return null;
        var type = target.GetType();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        
        // Try field first
        var fi = type.GetField(memberName, flags);
        if (fi != null)
        {
            try
            {
                return fi.GetValue(target);
            }
            catch { }
        }
        
        // Try property
        var pi = type.GetProperty(memberName, flags);
        if (pi != null && pi.CanRead)
        {
            try
            {
                return pi.GetValue(target);
            }
            catch { }
        }
        
        return null;
    }

    /// <summary>
    /// Attempts to set a field or property on an object using reflection.
    /// Tries field first, then property. Handles both public and non-public members.
    /// Fields on Mono are typically properties on IL2CPP.
    /// </summary>
    /// <param name="target">The target object to set the member on.</param>
    /// <param name="memberName">The name of the field or property.</param>
    /// <param name="value">The value to set.</param>
    /// <returns>True if the member was successfully set; otherwise, false.</returns>
    public static bool TrySetFieldOrProperty(object target, string memberName, object? value)
    {
        if (target == null) return false;
        var type = target.GetType();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        
        // Try field first
        var fi = type.GetField(memberName, flags);
        if (fi != null)
        {
            try
            {
                if (value == null || fi.FieldType.IsInstanceOfType(value))
                {
                    fi.SetValue(target, value);
                    return true;
                }
            }
            catch { }
        }
        
        // Try property
        var pi = type.GetProperty(memberName, flags);
        if (pi != null && pi.CanWrite)
        {
            try
            {
                if (value == null || pi.PropertyType.IsInstanceOfType(value))
                {
                    pi.SetValue(target, value);
                    return true;
                }
            }
            catch { }
        }
        
        return false;
    }

    /// <summary>
    /// Attempts to get a static field or property value from a type using reflection.
    /// Tries field first, then property. Handles both public and non-public members.
    /// Fields on Mono are typically properties on IL2CPP.
    /// </summary>
    /// <param name="type">The type to get the static member from.</param>
    /// <param name="memberName">The name of the field or property.</param>
    /// <returns>The value of the member, or null if not found or inaccessible.</returns>
    public static object? TryGetStaticFieldOrProperty(Type type, string memberName)
    {
        if (type == null) return null;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        
        // Try field first
        var fi = type.GetField(memberName, flags);
        if (fi != null)
        {
            try
            {
                return fi.GetValue(null);
            }
            catch { }
        }
        
        // Try property
        var pi = type.GetProperty(memberName, flags);
        if (pi != null && pi.CanRead)
        {
            try
            {
                return pi.GetValue(null);
            }
            catch { }
        }
        
        return null;
    }

    /// <summary>
    /// Attempts to set a static field or property value on a type using reflection.
    /// Tries field first, then property. Handles both public and non-public members.
    /// Fields on Mono are typically properties on IL2CPP.
    /// </summary>
    /// <param name="type">The type to set the static member on.</param>
    /// <param name="memberName">The name of the field or property.</param>
    /// <param name="value">The value to set.</param>
    /// <returns>True if the member was successfully set; otherwise, false.</returns>
    public static bool TrySetStaticFieldOrProperty(Type type, string memberName, object? value)
    {
        if (type == null) return false;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        
        // Try field first
        var fi = type.GetField(memberName, flags);
        if (fi != null)
        {
            try
            {
                if (value == null || fi.FieldType.IsInstanceOfType(value))
                {
                    fi.SetValue(null, value);
                    return true;
                }
            }
            catch { }
        }
        
        // Try property
        var pi = type.GetProperty(memberName, flags);
        if (pi != null && pi.CanWrite)
        {
            try
            {
                if (value == null || pi.PropertyType.IsInstanceOfType(value))
                {
                    pi.SetValue(null, value);
                    return true;
                }
            }
            catch { }
        }
        
        return false;
    }

    /// <summary>
    /// Safely gets a field value, handling both Mono and IL2CPP types.
    /// Uses TryGetFieldOrProperty to handle Mono/IL2CPP differences.
    /// </summary>
    /// <param name="obj">The object to get the field from.</param>
    /// <param name="fieldName">The name of the field.</param>
    /// <returns>The field value, or null if not found.</returns>
    public static object? GetFieldValue(object obj, string fieldName)
    {
        // Use TryGetFieldOrProperty to handle Mono/IL2CPP differences
        return TryGetFieldOrProperty(obj, fieldName);
    }

    /// <summary>
    /// Safely sets a field value, handling both Mono and IL2CPP types.
    /// Uses TrySetFieldOrProperty to handle Mono/IL2CPP differences.
    /// </summary>
    /// <param name="obj">The object to set the field on.</param>
    /// <param name="fieldName">The name of the field.</param>
    /// <param name="value">The value to set.</param>
    /// <returns>True if the field was set successfully, false otherwise.</returns>
    public static bool SetFieldValue(object obj, string fieldName, object? value)
    {
        // Use TrySetFieldOrProperty to handle Mono/IL2CPP differences
        return TrySetFieldOrProperty(obj, fieldName, value);
    }

    /// <summary>
    /// Finds all GameObjects in the scene.
    /// </summary>
    /// <returns>List of all GameObjects in the scene.</returns>
    public static List<GameObject> FindAllGameObjects()
    {
        var results = new List<GameObject>();

        // Use Resources.FindObjectsOfTypeAll
        var objects = Resources.FindObjectsOfTypeAll<GameObject>();
        results.AddRange(objects);
        return results;
    }

    /// <summary>
    /// Finds GameObjects by type.
    /// </summary>
    /// <typeparam name="T">The component type to search for.</typeparam>
    /// <returns>List of GameObjects with the specified component type.</returns>
    public static List<GameObject> FindGameObjectsByType<T>() where T : Component
    {
        var results = new List<GameObject>();

        // Use standard Unity methods
#if MONO
        var components = UnityEngine.Object.FindObjectsOfType<T>();
#else
        // IL2CPP: Use UnityEngine.Object explicitly (not Il2CppSystem.Object)
        var components = UnityEngine.Object.FindObjectsOfType<T>();
#endif
        foreach (var component in components)
        {
            if (component != null && component.gameObject != null)
            {
                results.Add(component.gameObject);
            }
        }
        return results;
    }
}


