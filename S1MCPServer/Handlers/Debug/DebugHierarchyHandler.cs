using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using S1MCPServer.Core;
using S1MCPServer.Models;
using S1MCPServer.Utils;
using UnityEngine;

namespace S1MCPServer.Handlers.Debug;

/// <summary>
/// Handles hierarchy and scene-related debug commands (getting hierarchy, finding objects by type, getting scene objects).
/// </summary>
public class DebugHierarchyHandler : DebugHandlerBase
{
    public DebugHierarchyHandler(ResponseQueue responseQueue) : base(responseQueue)
    {
    }

    public override void Handle(Request request)
    {
        switch (request.Method)
        {
            case "get_hierarchy":
                HandleGetHierarchy(request);
                break;
            case "find_objects_by_type":
                HandleFindObjectsByType(request);
                break;
            case "get_scene_objects":
                HandleGetSceneObjects(request);
                break;
            default:
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32601,
                    $"Unknown hierarchy method: {request.Method}"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                break;
        }
    }

    private void HandleGetHierarchy(Request request)
    {
        try
        {
            if (request.Params == null || !request.Params.TryGetValue("object_name", out var objectNameObj))
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602,
                    "object_name parameter is required"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            string objectName = objectNameObj?.ToString() ?? string.Empty;
            var gameObject = ReflectionHelper.FindGameObject(objectName);

            if (gameObject == null)
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32000,
                    "GameObject not found",
                    new { object_name = objectName }
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            var result = new Dictionary<string, object>
            {
                ["name"] = gameObject.name,
                ["parent"] = gameObject.transform.parent != null ? gameObject.transform.parent.name : null,
                ["children"] = new List<string>()
            };

            var children = new List<string>();
            for (int i = 0; i < gameObject.transform.childCount; i++)
            {
                children.Add(gameObject.transform.GetChild(i).name);
            }
            result["children"] = children;
            result["child_count"] = children.Count;

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleGetHierarchy: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000,
                "Failed to get hierarchy",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }

    private void HandleFindObjectsByType(Request request)
    {
        try
        {
            if (request.Params == null || !request.Params.TryGetValue("component_type", out var componentTypeObj))
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602,
                    "component_type parameter is required"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            string componentTypeName = componentTypeObj?.ToString() ?? string.Empty;
            ModLogger.Debug($"Finding objects by type: {componentTypeName}");

            Type? componentType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    componentType = assembly.GetType(componentTypeName);
                    if (componentType != null && typeof(Component).IsAssignableFrom(componentType))
                        break;
                }
                catch { }
            }

            if (componentType == null)
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32000,
                    "Component type not found or is not a Component",
                    new { component_type = componentTypeName }
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            var method = typeof(ReflectionHelper).GetMethod("FindGameObjectsByType", BindingFlags.Public | BindingFlags.Static);
            List<GameObject>? gameObjectsList = null;
            
            if (method != null)
            {
                var genericMethod = method.MakeGenericMethod(componentType);
                gameObjectsList = genericMethod.Invoke(null, null) as List<GameObject>;
            }

            if (gameObjectsList == null)
            {
                gameObjectsList = new List<GameObject>();
            }
            
            var objectResults = gameObjectsList.Select(go => new Dictionary<string, object>
            {
                ["name"] = go.name,
                ["path"] = GetGameObjectPath(go),
                ["position"] = new { x = go.transform.position.x, y = go.transform.position.y, z = go.transform.position.z }
            }).ToList();

            var finalResult = new Dictionary<string, object>
            {
                ["component_type"] = componentTypeName,
                ["objects"] = objectResults,
                ["count"] = objectResults.Count,
            };

            var finalResponse = ProtocolHandler.CreateSuccessResponse(request.Id, finalResult);
            _responseQueue.EnqueueResponse(finalResponse);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleFindObjectsByType: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000,
                "Failed to find objects by type",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }

    private void HandleGetSceneObjects(Request request)
    {
        try
        {
            ModLogger.Debug("Getting scene objects");

            var allObjects = ReflectionHelper.FindAllGameObjects();
            
            var sceneObjects = new Dictionary<string, List<Dictionary<string, object>>>();
            foreach (var obj in allObjects)
            {
                var sceneName = obj.scene.name;
                if (!sceneObjects.ContainsKey(sceneName))
                {
                    sceneObjects[sceneName] = new List<Dictionary<string, object>>();
                }

                sceneObjects[sceneName].Add(new Dictionary<string, object>
                {
                    ["name"] = obj.name,
                    ["path"] = GetGameObjectPath(obj),
                    ["active"] = obj.activeSelf
                });
            }

            var result = new Dictionary<string, object>
            {
                ["scenes"] = sceneObjects.Select(kvp => new Dictionary<string, object>
                {
                    ["scene_name"] = kvp.Key,
                    ["objects"] = kvp.Value,
                    ["count"] = kvp.Value.Count
                }).ToList(),
                ["total_objects"] = allObjects.Count,
            };

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleGetSceneObjects: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000,
                "Failed to get scene objects",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }
}

