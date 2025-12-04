using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using S1MCPServer.Core;
using S1MCPServer.Models;
using S1MCPServer.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

#if MONO
using ScheduleOne.Persistence;
using S1LoadManager = ScheduleOne.Persistence.LoadManager;
#else
using Il2CppScheduleOne.Persistence;
using S1LoadManager = Il2CppScheduleOne.Persistence.LoadManager;
#endif

namespace S1MCPServer.Handlers;

/// <summary>
/// Handles LoadManager-related commands for save game management.
/// </summary>
public class LoadManagerCommandHandler : ICommandHandler
{
    private readonly ResponseQueue _responseQueue;
    private const string MENU_SCENE_NAME = "Menu";

    public LoadManagerCommandHandler(ResponseQueue responseQueue)
    {
        _responseQueue = responseQueue;
    }

    public void Handle(Request request)
    {
        switch (request.Method)
        {
            case "list_saves":
                HandleListSaves(request);
                break;
            case "load_save":
                HandleLoadSave(request);
                break;
            default:
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32601, // Method not found
                    $"Unknown LoadManager method: {request.Method}"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                break;
        }
    }

    private void HandleListSaves(Request request)
    {
        try
        {
            ModLogger.Debug("Listing save games");

            // Access SaveGames as a static property
            var loadManagerType = typeof(S1LoadManager);
            var saveGamesProperty = loadManagerType.GetProperty("SaveGames", 
                BindingFlags.Public | BindingFlags.Static);
            
            if (saveGamesProperty == null)
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32000,
                    "SaveGames property not found on LoadManager"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            // Get SaveGames array (static property)
            var saveGamesArray = saveGamesProperty.GetValue(null);
            if (saveGamesArray == null)
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32000,
                    "SaveGames array is not available or empty"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            var saves = new List<Dictionary<string, object>>();
            
            // Handle both array and IEnumerable types
            if (saveGamesArray is IEnumerable enumerable)
            {
                int index = 0;
                foreach (var saveGame in enumerable)
                {
                    if (saveGame == null)
                    {
                        index++;
                        continue;
                    }

                    var saveInfo = SerializeSaveGame(saveGame, index);
                    saves.Add(saveInfo);
                    index++;
                }
            }

            var result = new Dictionary<string, object>
            {
                ["saves"] = saves,
                ["count"] = saves.Count
            };

            ModLogger.Info($"Found {saves.Count} save games");
            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleListSaves: {ex.Message}");
            ModLogger.Debug($"Stack trace: {ex.StackTrace}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000,
                "Failed to list save games",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }

    private void HandleLoadSave(Request request)
    {
        try
        {
            // Get slot_index parameter
            if (request.Params == null || !request.Params.TryGetValue("slot_index", out var slotIndexObj))
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602, // Invalid params
                    "slot_index parameter is required"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            int slotIndex;
            try
            {
                // Handle different numeric types from JSON deserialization
                if (slotIndexObj is int intValue)
                {
                    slotIndex = intValue;
                }
                else if (slotIndexObj is long longValue)
                {
                    slotIndex = (int)longValue;
                }
                else if (slotIndexObj is double doubleValue)
                {
                    slotIndex = (int)doubleValue;
                }
                else if (slotIndexObj is string strValue && int.TryParse(strValue, out int parsedValue))
                {
                    slotIndex = parsedValue;
                }
                else
                {
                    // Try using ToString and parsing as last resort
                    string valueStr = slotIndexObj?.ToString() ?? "";
                    if (!int.TryParse(valueStr, out slotIndex))
                    {
                        throw new InvalidCastException($"Cannot convert {slotIndexObj?.GetType().Name} to int");
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"Failed to parse slot_index: {ex.Message}, Type: {slotIndexObj?.GetType().Name}");
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602,
                    "slot_index must be a valid integer"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            ModLogger.Debug($"Loading save game at slot index: {slotIndex}");

            // Get LoadManager Instance for calling StartGame
            var loadManagerInstance = GetLoadManagerInstance();
            if (loadManagerInstance == null)
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32000,
                    "LoadManager.Instance is not available"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            // Access SaveGames as a static property
            var loadManagerType = typeof(S1LoadManager);
            var saveGamesProperty = loadManagerType.GetProperty("SaveGames", 
                BindingFlags.Public | BindingFlags.Static);
            
            if (saveGamesProperty == null)
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32000,
                    "SaveGames property not found on LoadManager"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            var saveGamesArray = saveGamesProperty.GetValue(null);
            if (saveGamesArray == null)
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32000,
                    "SaveGames array is not available or empty"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            // Get the save game at the specified index
            object? targetSaveGame = null;
            int currentIndex = 0;
            int totalCount = 0;

            if (saveGamesArray is IEnumerable enumerable)
            {
                foreach (var saveGame in enumerable)
                {
                    totalCount++;
                    if (currentIndex == slotIndex)
                    {
                        targetSaveGame = saveGame;
                        break;
                    }
                    currentIndex++;
                }
            }
            else if (saveGamesArray is Array array)
            {
                totalCount = array.Length;
                if (slotIndex >= 0 && slotIndex < array.Length)
                {
                    targetSaveGame = array.GetValue(slotIndex);
                }
            }

            if (targetSaveGame == null)
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32000,
                    $"Save slot {slotIndex} doesn't exist (total saves: {totalCount})"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            // Check current scene
            string currentScene = SceneManager.GetActiveScene().name;
            ModLogger.Debug($"Current scene: {currentScene}");

            // If not in menu scene, load menu first
            if (currentScene != MENU_SCENE_NAME)
            {
                ModLogger.Info($"Not in menu scene. Loading {MENU_SCENE_NAME} scene first...");
                try
                {
                    SceneManager.LoadScene(MENU_SCENE_NAME);
                    ModLogger.Info($"Successfully loaded {MENU_SCENE_NAME} scene");
                }
                catch (Exception ex)
                {
                    ModLogger.Error($"Failed to load menu scene: {ex.Message}");
                    var errorResponse = ProtocolHandler.CreateErrorResponse(
                        request.Id,
                        -32000,
                        $"Failed to load menu scene: {ex.Message}"
                    );
                    _responseQueue.EnqueueResponse(errorResponse);
                    return;
                }
            }

            // Load the save game
            try
            {
                // Get the StartGame method from LoadManager
                var startGameMethod = loadManagerInstance.GetType().GetMethod("StartGame", 
                    BindingFlags.Public | BindingFlags.Instance);
                
                if (startGameMethod == null)
                {
                    var errorResponse = ProtocolHandler.CreateErrorResponse(
                        request.Id,
                        -32000,
                        "StartGame method not found on LoadManager"
                    );
                    _responseQueue.EnqueueResponse(errorResponse);
                    return;
                }

                ModLogger.Info($"Starting game with save at slot {slotIndex}");
                // StartGame has optional parameters: StartGame(SaveInfo info, bool allowLoadStacking = true, bool allowSaveBackup = true)
                // We need to pass all parameters explicitly for reflection
                startGameMethod.Invoke(loadManagerInstance, new object[] { targetSaveGame, true, true });

                var result = new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["slot_index"] = slotIndex,
                    ["message"] = $"Successfully loaded save from slot {slotIndex}",
                    ["returned_to_menu"] = currentScene != MENU_SCENE_NAME
                };

                var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
                _responseQueue.EnqueueResponse(response);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to start game: {ex.Message}");
                ModLogger.Debug($"Stack trace: {ex.StackTrace}");
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32000,
                    $"Failed to start game: {ex.Message}",
                    new { details = ex.Message }
                );
                _responseQueue.EnqueueResponse(errorResponse);
            }
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleLoadSave: {ex.Message}");
            ModLogger.Debug($"Stack trace: {ex.StackTrace}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000,
                "Failed to load save game",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }

    /// <summary>
    /// Gets the LoadManager Instance using reflection.
    /// </summary>
    private object? GetLoadManagerInstance()
    {
        try
        {
            // Try to get Instance property/field from LoadManager (static singleton)
            var loadManagerType = typeof(S1LoadManager);
            
            // Try Instance property
            var instanceProperty = loadManagerType.GetProperty("Instance", 
                BindingFlags.Public | BindingFlags.Static);
            
            if (instanceProperty != null)
            {
                var instance = instanceProperty.GetValue(null);
                if (instance != null)
                    return instance;
            }

            // Try Instance field
            var instanceField = loadManagerType.GetField("Instance", 
                BindingFlags.Public | BindingFlags.Static);
            
            if (instanceField != null)
            {
                var instance = instanceField.GetValue(null);
                if (instance != null)
                    return instance;
            }

            // Try FindObjectOfType as fallback (for PersistentSingleton that hasn't initialized)
            ModLogger.Debug("Instance is null, trying FindObjectOfType");
            var findMethod = typeof(UnityEngine.Object).GetMethod("FindObjectOfType", 
                BindingFlags.Public | BindingFlags.Static,
                null,
                new Type[] { },
                null);
            
            if (findMethod != null)
            {
                var genericMethod = findMethod.MakeGenericMethod(loadManagerType);
                var foundInstance = genericMethod.Invoke(null, null);
                if (foundInstance != null)
                {
                    ModLogger.Debug("Found LoadManager using FindObjectOfType");
                    return foundInstance;
                }
            }

            ModLogger.Error("LoadManager.Instance not found and FindObjectOfType returned null");
            return null;
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error getting LoadManager.Instance: {ex.Message}");
            ModLogger.Debug($"Stack trace: {ex.StackTrace}");
            return null;
        }
    }

    /// <summary>
    /// Serializes a SaveGame object to a dictionary with all its properties.
    /// </summary>
    private Dictionary<string, object> SerializeSaveGame(object saveGame, int index)
    {
        var result = new Dictionary<string, object>
        {
            ["slot_index"] = index
        };

        try
        {
            var type = saveGame.GetType();
            
            // Get all public properties
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                if (!prop.CanRead) continue;

                try
                {
                    var value = prop.GetValue(saveGame);
                    result[ToCamelCase(prop.Name)] = FormatPropertyValue(value);
                }
                catch (Exception ex)
                {
                    ModLogger.Debug($"Could not read property {prop.Name}: {ex.Message}");
                    result[ToCamelCase(prop.Name)] = $"[Error reading: {ex.Message}]";
                }
            }

            // Get all public fields
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var field in fields)
            {
                try
                {
                    var value = field.GetValue(saveGame);
                    result[ToCamelCase(field.Name)] = FormatPropertyValue(value);
                }
                catch (Exception ex)
                {
                    ModLogger.Debug($"Could not read field {field.Name}: {ex.Message}");
                    result[ToCamelCase(field.Name)] = $"[Error reading: {ex.Message}]";
                }
            }
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error serializing SaveGame: {ex.Message}");
            result["error"] = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Formats a property value for JSON serialization.
    /// </summary>
    private object FormatPropertyValue(object? value)
    {
        if (value == null)
            return "null";

        var type = value.GetType();

        // Handle primitives and strings
        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
            return value;

        // Handle DateTime
        if (value is DateTime dateTime)
            return dateTime.ToString("o"); // ISO 8601 format

        // Handle enums
        if (type.IsEnum)
            return value.ToString() ?? "null";

        // Handle Unity types
        if (value is Vector3 v3)
            return new Dictionary<string, object> { ["x"] = v3.x, ["y"] = v3.y, ["z"] = v3.z };
        
        if (value is Vector2 v2)
            return new Dictionary<string, object> { ["x"] = v2.x, ["y"] = v2.y };

        // Handle collections
        if (value is IEnumerable enumerable && !(value is string))
        {
            var items = new List<object>();
            int count = 0;
            foreach (var item in enumerable)
            {
                if (count++ >= 50) // Limit collection size
                {
                    items.Add("[... truncated]");
                    break;
                }
                items.Add(FormatPropertyValue(item));
            }
            return items;
        }

        // Default: try ToString
        try
        {
            return value.ToString() ?? "null";
        }
        catch
        {
            return $"[{type.Name}]";
        }
    }

    /// <summary>
    /// Converts PascalCase to camelCase for JSON property names.
    /// </summary>
    private string ToCamelCase(string str)
    {
        if (string.IsNullOrEmpty(str) || char.IsLower(str[0]))
            return str;

        return char.ToLowerInvariant(str[0]) + str.Substring(1);
    }
}

