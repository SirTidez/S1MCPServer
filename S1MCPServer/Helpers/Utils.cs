#nullable enable
using System.Collections;
using MelonLoader;
using S1MCPServer.Utils;
using UnityEngine;
#if MONO
using ScheduleOne;
using ScheduleOne.DevUtilities;
using ScheduleOne.ItemFramework;
using ScheduleOne.PlayerScripts;
using FishNet;
#else
using Il2CppInterop.Runtime;
using Il2CppScheduleOne;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppFishNet;
using Object = Il2CppSystem.Object;
#endif

namespace S1MCPServer.Helpers;

/// <summary>
/// Provides extension methods for converting between C# and Il2Cpp lists.
/// /// </summary>
public static class Il2CppListExtensions
{
    /// <summary>
    /// Converts a C# <see cref="List{T}"/> to an <see cref="IEnumerable{T}"/>.
    /// It's a no-op for C# lists. Nonetheless, it is useful for Il2Cpp lists and it is recommended to use this method for consistency.
    /// </summary>
    /// <typeparam name="T">The type of the objects in the collection.</typeparam>
    /// <param name="source">The source enumerable to convert.</param>
    /// <returns>A C# <see cref="List{T}"/> containing the elements of the source.</returns>
    public static IEnumerable<T> AsEnumerable<T>(this List<T> list)
    {
        return list ?? [];
    }

#if !MONO
    /// <summary>
    /// Converts an <see cref="IEnumerable{T}"/> to an Il2Cpp <see cref="Il2CppSystem.Collections.Generic.List{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of the objects in the collection.</typeparam>
    /// <param name="source">The source enumerable to convert.</param>
    /// <returns>An Il2Cpp <see cref="Il2CppSystem.Collections.Generic.List{T}"/> containing the elements of the source.</returns>
    public static Il2CppSystem.Collections.Generic.List<T> ToIl2CppList<T>(
        this IEnumerable<T> source
    )
    {
        var il2CppList = new Il2CppSystem.Collections.Generic.List<T>();
        foreach (var item in source)
            il2CppList.Add(item);
        return il2CppList;
    }

    /// <summary>
    /// Converts an Il2Cpp <see cref="Il2CppSystem.Collections.Generic.List{T}"/> to a C# <see cref="List{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of the objects in the list.</typeparam>
    /// <param name="il2CppList">The Il2Cpp list to convert.</param>
    /// <returns>A C# <see cref="List{T}"/> containing the elements from the Il2Cpp list.</returns>
    public static List<T> ConvertToList<T>(Il2CppSystem.Collections.Generic.List<T> il2CppList)
    {
        List<T> csharpList = new List<T>();
        T[] array = il2CppList.ToArray();
        csharpList.AddRange(array);
        return csharpList;
    }

    /// <summary>
    /// Converts an Il2Cpp <see cref="Il2CppSystem.Collections.Generic.List{T}"/> to a C# <see cref="IEnumerable{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of the objects in the list.</typeparam>
    /// <param name="list">The Il2Cpp list to convert.</param>
    /// <returns>A C# <see cref="IEnumerable{T}"/> containing the elements from the Il2Cpp list.</returns>
    public static IEnumerable<T> AsEnumerable<T>(this Il2CppSystem.Collections.Generic.List<T> list)
    {
        return list == null ? [] : list._items.Take(list._size);
    }
#endif
}

/// <summary>
/// Common utility functions for the mod.
/// </summary>
public static class Utils
{

    /// <summary>
    /// Searches all loaded objects of type <typeparamref name="T"/> and returns the first one matching the given name.
    /// </summary>
    /// <typeparam name="T">The type of UnityEngine.Object to search for (e.g., Sprite, AudioClip).</typeparam>
    /// <param name="objectName">The name of the object to find.</param>
    /// <returns>The first matching object of type <typeparamref name="T"/> if found; otherwise, null.</returns>
    /// <example>
    /// <code>
    /// // Example usage for finding a Sprite by name
    /// var sprite = FindObjectByName&lt;‌Sprite‌&gt;("Dan_Mugshot");
    /// </code>
    /// </example>
    public static T FindObjectByName<T>(string objectName)
        where T : UnityEngine.Object
    {
        try
        {
            foreach (var obj in Resources.FindObjectsOfTypeAll<T>())
            {
                if (obj.name != objectName)
                    continue;
                ModLogger.Debug($"Found {typeof(T).Name} '{objectName}' directly in loaded objects");
                return obj;
            }

            return null;
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error finding {typeof(T).Name} '{objectName}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets all components of type <typeparamref name="T"/> in the given GameObject and its children recursively.
    /// </summary>
    /// <param name="obj">The GameObject to search in.</param>
    /// <typeparam name="T">The type of component to search for.</typeparam>
    /// <returns>A list of all components of type <typeparamref name="T"/> found in the GameObject and its children.</returns>
    /// <example>
    /// <code>
    /// // Example usage for getting all colliders in a GameObject
    /// List&lt;‌Collider‌&gt; colliders = GetAllComponentsInChildrenRecursive&lt;‌Collider‌&gt;(someGameObject);
    /// </code>
    /// </example>
    public static List<T> GetAllComponentsInChildrenRecursive<T>(GameObject obj)
        where T : Component
    {
        var results = new List<T>();
        if (obj == null)
            return results;

        T[] components = obj.GetComponents<T>();
        if (components.Length > 0)
        {
            results.AddRange(components);
        }

        for (var i = 0; i < obj.transform.childCount; i++)
        {
            var child = obj.transform.GetChild(i);
            results.AddRange(GetAllComponentsInChildrenRecursive<T>(child.gameObject));
        }

        return results;
    }

    /// <summary>
    /// Checks if the given object is of type <typeparamref name="T"/> and casts it to that type.
    /// Provides cross-runtime type checking.
    /// </summary>
    /// <param name="obj">The object to check.</param>
    /// <param name="result">The cast object if the check is successful; otherwise, null.</param>
    /// <typeparam name="T">The type to check against.</typeparam>
    /// <returns>True if the object is of type <typeparamref name="T"/>; otherwise, false.</returns>
    /// <remarks>
    /// Method adapted from S1API (https://github.com/KaBooMa/S1API/blob/stable/S1API/Internal/Utils/CrossType.cs)
    /// Provides IL2CPP support
    /// </remarks>
    /// <example>
    /// <code>
    /// // Example usage for checking if an object is of type GameObject
    /// if (Is&lt;‌GameObject‌&gt;(someObject, out GameObject result))
    /// {
    ///     // Do something with result
    /// }
    /// </code>
    /// </example>
    public static bool Is<T>(object obj, out T result)
#if !MONO
        where T : Object
#else
        where T : class
#endif
    {
#if !MONO
        if (obj is Object il2CppObj)
        {
            // Try direct TryCast - it handles type checking internally
            try
            {
                result = il2CppObj.TryCast<T>()!;
                return result != null;
            }
            catch
            {
                // Casting failed
            }
        }
#else
        if (obj is T t)
        {
            result = t;
            return true;
        }
#endif

        result = null!;
        return false;
    }

    /// <summary>
    /// Gets all storable item definitions from the item registry.
    /// </summary>
    /// <returns>A list of all storable item definitions.</returns>
    public static List<StorableItemDefinition> GetAllStorableItemDefinitions()
    {
#if !MONO
        var itemRegistry = Il2CppListExtensions.ConvertToList(Registry.Instance.ItemRegistry);
#else
        var itemRegistry = Registry.Instance.ItemRegistry.ToList();
#endif
        var itemDefinitions = new List<StorableItemDefinition>();

        foreach (var item in itemRegistry)
        {
            if (Utils.Is<StorableItemDefinition>(item.Definition, out var definition))
            {
                itemDefinitions.Add(definition);
            }
            else
            {
                ModLogger.Warn(
                    $"Definition {item.Definition?.GetType().FullName} is not a StorableItemDefinition"
                );
            }
        }

        return itemDefinitions.ToList();
    }

    /// <summary>
    /// Waits for the player to be ready before starting the given coroutine.
    /// </summary>
    /// <param name="routine">Coroutine to start when player is ready</param>
    /// <returns>An enumerator that waits for the player to be ready.</returns>
    public static IEnumerator WaitForPlayer(IEnumerator routine)
    {
        while (Player.Local == null || Player.Local.gameObject == null)
            yield return null;

        // player is ready, start the coroutine
        MelonCoroutines.Start(routine);
    }

    /// <summary>
    /// Waits for the network to be ready before starting the given coroutine.
    /// </summary>
    /// <param name="routine">Coroutine to start when network is ready</param>
    /// <returns>An enumerator that waits for the network to be ready.</returns>
    public static IEnumerator WaitForNetwork(IEnumerator routine)
    {
        while (InstanceFinder.IsServer == false && InstanceFinder.IsClient == false)
            yield return null;
        // network is ready, start the coroutine
        MelonCoroutines.Start(routine);
    }

    /// <summary>
    /// Waits for the given object to be not null.
    /// </summary>
    /// <param name="obj">The object to check.</param>
    /// <param name="timeout">The timeout in seconds. If NaN, no timeout is applied.</param>
    /// <param name="onTimeout">Action to execute when the timeout is reached.</param>
    /// <param name="onFinish">Action to execute when the object is not null.</param>
    /// <returns>An enumerator that waits for the object to be not null.</returns>
    public static IEnumerator WaitForNotNull(
        object? obj,
        float timeout = Single.NaN,
        Action onTimeout = null,
        Action onFinish = null
    )
    {
        float startTime = Time.time;

        while (obj == null)
        {
            if (!float.IsNaN(timeout) && Time.time - startTime > timeout)
            {
                onTimeout?.Invoke();
                yield break;
            }

            yield return null;
        }

        onFinish?.Invoke();
    }

    /// <summary>
    /// Waits for the given NetworkSingleton to be ready before starting the given coroutine.
    /// </summary>
    /// <typeparam name="T">The type of the NetworkSingleton.</typeparam>
    /// <param name="coroutine">The coroutine to start when the NetworkSingleton is ready.</param>
    /// <returns>An enumerator that waits for the NetworkSingleton to be ready.</returns>
    public static IEnumerator WaitForNetworkSingleton<T>(IEnumerator coroutine)
        where T : NetworkSingleton<T>
    {
        while (!NetworkSingleton<T>.InstanceExists)
            yield return null;

        yield return coroutine;
    }
}