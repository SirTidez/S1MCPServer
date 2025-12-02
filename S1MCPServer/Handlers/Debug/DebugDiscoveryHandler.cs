using System;
using System.Collections.Generic;
using System.Linq;
using S1MCPServer.Core;
using S1MCPServer.Models;
using S1MCPServer.Utils;
using UnityEngine;

namespace S1MCPServer.Handlers.Debug;

/// <summary>
/// Handles discovery-related debug commands (finding GameObjects, searching types, scene hierarchy).
/// </summary>
public class DebugDiscoveryHandler : DebugHandlerBase
{
    public DebugDiscoveryHandler(ResponseQueue responseQueue) : base(responseQueue)
    {
    }

    public override void Handle(Request request)
    {
        switch (request.Method)
        {
            case "find_gameobjects":
                HandleFindGameObjects(request);
                break;
            case "search_types":
                HandleSearchTypes(request);
                break;
            case "get_scene_hierarchy":
                HandleGetSceneHierarchy(request);
                break;
            case "list_scenes":
                HandleListScenes(request);
                break;
            default:
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32601,
                    $"Unknown discovery method: {request.Method}"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                break;
        }
    }

    private void HandleFindGameObjects(Request request)
    {
        try
        {
            var allObjects = ReflectionHelper.FindAllGameObjects();
            var results = new List<Dictionary<string, object>>();
            int maxResults = 1000; // Limit results

            string? namePattern = null;
            string? tag = null;
            int? layer = null;
            string? componentType = null;
            bool? activeOnly = null;

            if (request.Params != null)
            {
                if (request.Params.TryGetValue("name_pattern", out var namePatternObj))
                    namePattern = namePatternObj?.ToString();
                if (request.Params.TryGetValue("tag", out var tagObj))
                    tag = tagObj?.ToString();
                if (request.Params.TryGetValue("layer", out var layerObj) && layerObj != null)
                {
                    int layerVal;
                    if (int.TryParse(layerObj.ToString(), out layerVal))
                        layer = layerVal;
                }
                if (request.Params.TryGetValue("component_type", out var compTypeObj))
                    componentType = compTypeObj?.ToString();
                if (request.Params.TryGetValue("active_only", out var activeObj))
                {
                    bool activeVal;
                    if (bool.TryParse(activeObj?.ToString(), out activeVal))
                        activeOnly = activeVal;
                }
            }

            foreach (var obj in allObjects)
            {
                if (obj == null) continue;

                // Filter by name pattern
                if (!string.IsNullOrEmpty(namePattern) && 
                    !obj.name.Contains(namePattern, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Filter by tag
                if (!string.IsNullOrEmpty(tag) && obj.tag != tag)
                    continue;

                // Filter by layer
                if (layer.HasValue && obj.layer != layer.Value)
                    continue;

                // Filter by active state
                if (activeOnly.HasValue && activeOnly.Value && !obj.activeSelf)
                    continue;

                // Filter by component type
                if (!string.IsNullOrEmpty(componentType))
                {
                    var compType = TypeResolver.ResolveComponentType(componentType);
                    if (compType == null)
                    {
                        // Component type not found, skip this filter
                    }
                    else
                    {
                        var component = ReflectionHelper.GetComponent(obj, compType);
                        if (component == null)
                            continue;
                    }
                }

                // Add to results
                results.Add(ObjectSerializer.SerializeGameObject(obj));
                if (results.Count >= maxResults)
                    break;
            }

            var result = new Dictionary<string, object>
            {
                ["gameobjects"] = results,
                ["count"] = results.Count,
                ["total_scanned"] = allObjects.Count
            };

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleFindGameObjects: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000,
                "Failed to find GameObjects",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }

    private void HandleSearchTypes(Request request)
    {
        try
        {
            if (request.Params == null || !request.Params.TryGetValue("pattern", out var patternObj))
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602,
                    "pattern parameter is required"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            string pattern = patternObj?.ToString() ?? string.Empty;
            bool componentTypesOnly = false;
            if (request.Params.TryGetValue("component_types_only", out var compOnlyObj))
                bool.TryParse(compOnlyObj?.ToString(), out componentTypesOnly);

            var matches = TypeResolver.SearchTypes(pattern, componentTypesOnly);
            var results = new List<Dictionary<string, object>>();

            foreach (var type in matches.Take(100)) // Limit to 100 results
            {
                results.Add(ObjectSerializer.SerializeType(type));
            }

            var result = new Dictionary<string, object>
            {
                ["types"] = results,
                ["count"] = results.Count,
                ["pattern"] = pattern
            };

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleSearchTypes: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000,
                "Failed to search types",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }

    private void HandleGetSceneHierarchy(Request request)
    {
        try
        {
            string? sceneName = null;
            bool activeOnly = false;
            int maxDepth = 10;

            if (request.Params != null)
            {
                if (request.Params.TryGetValue("scene_name", out var sceneObj))
                    sceneName = sceneObj?.ToString();
                if (request.Params.TryGetValue("active_only", out var activeObj))
                    bool.TryParse(activeObj?.ToString(), out activeOnly);
                if (request.Params.TryGetValue("max_depth", out var depthObj))
                    int.TryParse(depthObj?.ToString(), out maxDepth);
            }

            var allObjects = ReflectionHelper.FindAllGameObjects();
            var rootObjects = new List<GameObject>();

            // Find root objects (no parent)
            foreach (var obj in allObjects)
            {
                if (obj == null) continue;
                if (!string.IsNullOrEmpty(sceneName) && obj.scene.name != sceneName)
                    continue;
                if (obj.transform.parent == null)
                    rootObjects.Add(obj);
            }

            var hierarchy = new List<Dictionary<string, object>>();
            foreach (var root in rootObjects)
            {
                if (activeOnly && !root.activeSelf)
                    continue;
                hierarchy.Add(BuildHierarchyNode(root, activeOnly, maxDepth, 0));
            }

            var result = new Dictionary<string, object>
            {
                ["hierarchy"] = hierarchy,
                ["root_count"] = hierarchy.Count,
                ["scene_name"] = sceneName ?? "all"
            };

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleGetSceneHierarchy: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000,
                "Failed to get scene hierarchy",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }

    private Dictionary<string, object> BuildHierarchyNode(GameObject obj, bool activeOnly, int maxDepth, int currentDepth)
    {
        var node = new Dictionary<string, object>
        {
            ["name"] = obj.name,
            ["active"] = obj.activeSelf,
            ["layer"] = obj.layer,
            ["tag"] = obj.tag
        };

        if (currentDepth < maxDepth)
        {
            var children = new List<Dictionary<string, object>>();
            for (int i = 0; i < obj.transform.childCount; i++)
            {
                var child = obj.transform.GetChild(i).gameObject;
                if (!activeOnly || child.activeSelf)
                    children.Add(BuildHierarchyNode(child, activeOnly, maxDepth, currentDepth + 1));
            }
            node["children"] = children;
            node["child_count"] = children.Count;
        }

        return node;
    }

    private void HandleListScenes(Request request)
    {
        try
        {
            var scenes = new List<Dictionary<string, object>>();
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                scenes.Add(new Dictionary<string, object>
                {
                    ["name"] = scene.name,
                    ["path"] = scene.path,
                    ["is_loaded"] = scene.isLoaded,
                    ["build_index"] = scene.buildIndex,
                    ["root_count"] = scene.rootCount
                });
            }

            var result = new Dictionary<string, object>
            {
                ["scenes"] = scenes,
                ["count"] = scenes.Count
            };

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleListScenes: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000,
                "Failed to list scenes",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }
}

