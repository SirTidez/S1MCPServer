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
/// Handles component inspection commands (inspecting components, listing components, getting member values).
/// </summary>
public class DebugComponentInspectionHandler : DebugHandlerBase
{
    public DebugComponentInspectionHandler(ResponseQueue responseQueue) : base(responseQueue)
    {
    }

    public override void Handle(Request request)
    {
        switch (request.Method)
        {
            case "inspect_component":
                HandleInspectComponent(request);
                break;
            case "list_components":
                HandleListComponents(request);
                break;
            case "get_component_by_type":
                HandleGetComponentByType(request);
                break;
            case "get_member_value":
                HandleGetMemberValue(request);
                break;
            default:
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32601,
                    $"Unknown component inspection method: {request.Method}"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                break;
        }
    }

    private void HandleInspectComponent(Request request)
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
            string? objectName = null;
            string? npcId = null;
            
            if (request.Params.TryGetValue("object_name", out var objectNameObj))
                objectName = objectNameObj?.ToString();
            if (request.Params.TryGetValue("npc_id", out var npcIdObj))
                npcId = npcIdObj?.ToString();

            int maxDepth = 3;
            if (request.Params.TryGetValue("max_depth", out var maxDepthObj))
                int.TryParse(maxDepthObj?.ToString(), out maxDepth);

            ModLogger.Debug($"Inspecting component: {componentTypeName} on object: {objectName ?? npcId ?? "any"}");

            Component? component = null;
            GameObject? gameObject = null;

            if (!string.IsNullOrEmpty(npcId))
            {
                gameObject = GameObjectResolver.GetGameObjectFromNPCId(npcId);
                if (gameObject == null)
                {
                    var errorResponse = ProtocolHandler.CreateErrorResponse(
                        request.Id,
                        -32000,
                        "NPC not found or has no GameObject",
                        new { npc_id = npcId }
                    );
                    _responseQueue.EnqueueResponse(errorResponse);
                    return;
                }
                objectName = gameObject.name;
            }
            else if (!string.IsNullOrEmpty(objectName))
            {
                gameObject = ReflectionHelper.FindGameObject(objectName);
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
            }

            if (gameObject != null)
            {
                component = ComponentResolver.FindComponentByTypeName(gameObject, componentTypeName);
            }
            else
            {
                component = ComponentResolver.FindComponentByTypeNameInScene(componentTypeName);
            }

            if (component == null)
            {
                List<string> availableComponents = new List<string>();
                List<string> similarComponents = new List<string>();

                if (gameObject != null)
                {
                    var allComponents = gameObject.GetComponents<Component>();
                    foreach (var comp in allComponents)
                    {
                        if (comp == null) continue;
                        var compType = comp.GetType();
                        availableComponents.Add(compType.Name);

                        if (compType.Name.Contains(componentTypeName, StringComparison.OrdinalIgnoreCase) ||
                            componentTypeName.Contains(compType.Name, StringComparison.OrdinalIgnoreCase) ||
                            LevenshteinDistance(compType.Name.ToLower(), componentTypeName.ToLower()) <= 3)
                        {
                            similarComponents.Add(compType.Name);
                        }
                    }
                }

                var errorData = new Dictionary<string, object>
                {
                    ["component_type_searched"] = componentTypeName,
                    ["object_name"] = objectName ?? "scene",
                    ["suggestion"] = similarComponents.Count > 0
                        ? $"Component not found. Did you mean: {string.Join(", ", similarComponents)}?"
                        : "Component not found. Use 's1_list_components' to see all available components on this GameObject.",
                    ["similar_components"] = similarComponents,
                    ["available_components"] = availableComponents.Take(10).ToList()
                };

                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32000,
                    "Component not found",
                    errorData
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            var result = InspectComponentDeep(component, maxDepth);
            result["component_type"] = component.GetType().FullName ?? component.GetType().Name;
            result["game_object"] = component.gameObject.name;
            result["game_object_path"] = GetGameObjectPath(component.gameObject);

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleInspectComponent: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000,
                "Failed to inspect component",
                new { details = ex.Message, stack_trace = ex.StackTrace }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }

    private Dictionary<string, object> InspectComponentDeep(Component component, int maxDepth = 3)
    {
        var result = new Dictionary<string, object>
        {
            ["type"] = component.GetType().Name,
            ["full_type"] = component.GetType().FullName ?? "Unknown"
        };

        var properties = new Dictionary<string, object>();
        try
        {
            var props = component.GetType().GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            
            foreach (var prop in props)
            {
                try
                {
                    if (ValueFormatter.IsBackingField(prop.Name) || ValueFormatter.IsIntPtrType(prop.PropertyType))
                        continue;

                    if (prop.CanRead && prop.GetIndexParameters().Length == 0)
                    {
                        var value = prop.GetValue(component);
                        var formattedValue = ValueFormatter.FormatValueDeep(value, maxDepth - 1);
                        if (formattedValue != null)
                        {
                            properties[prop.Name] = formattedValue;
                        }
                    }
                }
                catch (Exception ex)
                {
                    properties[prop.Name] = new Dictionary<string, object>
                    {
                        ["error"] = ex.Message,
                        ["readable"] = false
                    };
                }
            }
        }
        catch (Exception ex)
        {
            properties["_error"] = ex.Message;
        }
        result["properties"] = properties;

        var fields = new Dictionary<string, object>();
        try
        {
            var fieldInfos = component.GetType().GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            
            foreach (var field in fieldInfos)
            {
                try
                {
                    if (ValueFormatter.IsBackingField(field.Name) || ValueFormatter.IsIntPtrType(field.FieldType))
                        continue;

                    var value = ReflectionHelper.GetFieldValue(component, field.Name);
                    var formattedValue = ValueFormatter.FormatValueDeep(value, maxDepth - 1);
                    if (formattedValue != null)
                    {
                        fields[field.Name] = formattedValue;
                    }
                }
                catch (Exception ex)
                {
                    fields[field.Name] = new Dictionary<string, object>
                    {
                        ["error"] = ex.Message,
                        ["readable"] = false
                    };
                }
            }
        }
        catch (Exception ex)
        {
            fields["_error"] = ex.Message;
        }
        result["fields"] = fields;

        return result;
    }

    private void HandleListComponents(Request request)
    {
        try
        {
            string? objectName = null;
            string? npcId = null;

            if (request.Params != null)
            {
                request.Params.TryGetValue("object_name", out var objectNameObj);
                objectName = objectNameObj?.ToString();
                request.Params.TryGetValue("npc_id", out var npcIdObj);
                npcId = npcIdObj?.ToString();
            }

            if (string.IsNullOrEmpty(objectName) && string.IsNullOrEmpty(npcId))
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602,
                    "Either object_name or npc_id parameter is required"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            GameObject? gameObject = null;

            if (!string.IsNullOrEmpty(npcId))
            {
                gameObject = GameObjectResolver.GetGameObjectFromNPCId(npcId);
                if (gameObject == null)
                {
                    var errorResponse = ProtocolHandler.CreateErrorResponse(
                        request.Id,
                        -32000,
                        "NPC not found or has no GameObject",
                        new { npc_id = npcId }
                    );
                    _responseQueue.EnqueueResponse(errorResponse);
                    return;
                }
                objectName = gameObject.name;
            }
            else if (!string.IsNullOrEmpty(objectName))
            {
                gameObject = ReflectionHelper.FindGameObject(objectName);
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
            }

            if (gameObject == null)
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32000,
                    "Failed to resolve GameObject"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            ModLogger.Debug($"Listing components for GameObject: {gameObject.name}");

            var allComponents = gameObject.GetComponents<Component>();
            var componentList = new List<Dictionary<string, object>>();

            foreach (var component in allComponents)
            {
                if (component == null) continue;

                try
                {
                    var componentType = component.GetType();
                    var componentInfo = new Dictionary<string, object>
                    {
                        ["component_name"] = componentType.Name,
                        ["component_type"] = componentType.FullName ?? componentType.Name,
                        ["namespace"] = componentType.Namespace ?? "Unknown",
                        ["assembly"] = componentType.Assembly.GetName().Name ?? "Unknown"
                    };

                    if (componentType.Namespace?.Contains("ScheduleOne") == true)
                    {
                        componentInfo["is_game_component"] = true;

                        if (componentType.Name.Contains("Dealer"))
                        {
                            componentInfo["debug_hint"] = "Dealer component - check 'Home' property for movement issues";
                        }
                        else if (componentType.Name.Contains("Movement"))
                        {
                            componentInfo["debug_hint"] = "Movement component - check 'HasDestination', 'IsMoving', 'IsPaused' properties";
                        }
                        else if (componentType.Name.Contains("NPC") && !componentType.Name.Contains("Dealer"))
                        {
                            componentInfo["debug_hint"] = "Core NPC component - check 'IsConscious', 'Health' properties";
                        }
                    }
                    else
                    {
                        componentInfo["is_game_component"] = false;
                    }

                    componentList.Add(componentInfo);
                }
                catch (Exception ex)
                {
                    ModLogger.Debug($"Error listing component: {ex.Message}");
                    componentList.Add(new Dictionary<string, object>
                    {
                        ["component_name"] = "Unknown",
                        ["error"] = ex.Message
                    });
                }
            }

            var result = new Dictionary<string, object>
            {
                ["game_object_name"] = gameObject.name,
                ["components"] = componentList,
                ["component_count"] = componentList.Count,
                ["npc_id"] = npcId ?? "N/A",
                ["usage_hint"] = "Use 's1_inspect_component' with any component_name or component_type from this list to inspect it in detail"
            };

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleListComponents: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000,
                "Failed to list components",
                new { details = ex.Message, stack_trace = ex.StackTrace }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }

    private void HandleGetComponentByType(Request request)
    {
        try
        {
            if (request.Params == null || !request.Params.TryGetValue("object_name", out var objectNameObj) ||
                !request.Params.TryGetValue("component_type", out var componentTypeObj))
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602,
                    "object_name and component_type parameters are required"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            string objectName = objectNameObj?.ToString() ?? string.Empty;
            string componentTypeName = componentTypeObj?.ToString() ?? string.Empty;

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

            var component = ComponentResolver.FindComponentByTypeName(gameObject, componentTypeName);
            if (component == null)
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32000,
                    "Component not found",
                    new { component_type = componentTypeName, object_name = objectName }
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            var result = new Dictionary<string, object>
            {
                ["component_type"] = component.GetType().FullName ?? component.GetType().Name,
                ["component_name"] = component.GetType().Name,
                ["game_object"] = gameObject.name,
                ["found"] = true
            };

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleGetComponentByType: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000,
                "Failed to get component by type",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }

    private void HandleGetMemberValue(Request request)
    {
        try
        {
            if (request.Params == null || !request.Params.TryGetValue("object_name", out var objectNameObj) ||
                !request.Params.TryGetValue("member_path", out var memberPathObj))
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602,
                    "object_name and member_path parameters are required"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            string objectName = objectNameObj?.ToString() ?? string.Empty;
            string memberPath = memberPathObj?.ToString() ?? string.Empty;
            string? componentType = null;
            string? npcId = null;
            
            if (request.Params.TryGetValue("component_type", out var componentTypeObj))
                componentType = componentTypeObj?.ToString();
            if (request.Params.TryGetValue("npc_id", out var npcIdObj))
                npcId = npcIdObj?.ToString();

            GameObject? gameObject = null;
            
            if (!string.IsNullOrEmpty(npcId))
            {
                gameObject = GameObjectResolver.GetGameObjectFromNPCId(npcId);
                if (gameObject == null)
                {
                    var errorResponse = ProtocolHandler.CreateErrorResponse(
                        request.Id,
                        -32000,
                        "NPC not found or has no GameObject",
                        new { npc_id = npcId }
                    );
                    _responseQueue.EnqueueResponse(errorResponse);
                    return;
                }
                objectName = gameObject.name;
            }
            else if (!string.IsNullOrEmpty(objectName))
            {
                gameObject = ReflectionHelper.FindGameObject(objectName);
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
            }
            else
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602,
                    "Either object_name or npc_id parameter is required"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            object? targetObject = gameObject;
            
            if (!string.IsNullOrEmpty(componentType))
            {
                var component = ComponentResolver.FindComponentByTypeName(gameObject, componentType);
                if (component == null)
                {
                    var errorResponse = ProtocolHandler.CreateErrorResponse(
                        request.Id,
                        -32000,
                        "Component not found",
                        new { component_type = componentType, object_name = objectName }
                    );
                    _responseQueue.EnqueueResponse(errorResponse);
                    return;
                }
                targetObject = component;
            }

            // Navigate the member path (supports nested access like "Dealer.Home.BuildingName")
            var pathParts = memberPath.Split('.');
            object? currentValue = targetObject;
            string currentPath = "";

            foreach (var part in pathParts)
            {
                if (currentValue == null)
                {
                    var errorResponse = ProtocolHandler.CreateErrorResponse(
                        request.Id,
                        -32000,
                        $"Null value encountered at path: {currentPath}",
                        new { member_path = memberPath, current_path = currentPath }
                    );
                    _responseQueue.EnqueueResponse(errorResponse);
                    return;
                }

                currentPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}.{part}";
                
                // Use TryGetFieldOrProperty to handle Mono/IL2CPP differences (fields on Mono are typically properties on IL2CPP)
                try
                {
                    // Try instance member first
                    var newValue = ReflectionHelper.TryGetFieldOrProperty(currentValue, part);
                    
                    // If not found, try static member
                    if (newValue == null && currentValue != null)
                    {
                        newValue = ReflectionHelper.TryGetStaticFieldOrProperty(currentValue.GetType(), part);
                    }
                    
                    if (newValue == null)
                    {
                        var errorResponse = ProtocolHandler.CreateErrorResponse(
                            request.Id,
                            -32000,
                            $"Member '{part}' not found at path '{currentPath}'",
                            new { member_path = memberPath, current_path = currentPath }
                        );
                        _responseQueue.EnqueueResponse(errorResponse);
                        return;
                    }
                    
                    currentValue = newValue;
                }
                catch (Exception ex)
                {
                    var errorResponse = ProtocolHandler.CreateErrorResponse(
                        request.Id,
                        -32000,
                        $"Error reading member '{part}' at path '{currentPath}'",
                        new { member_path = memberPath, error = ex.Message }
                    );
                    _responseQueue.EnqueueResponse(errorResponse);
                    return;
                }
            }

            // Format the final value
            var formattedValue = ValueFormatter.FormatValueDeep(currentValue, 2);

            var result = new Dictionary<string, object>
            {
                ["member_path"] = memberPath,
                ["value"] = formattedValue,
                ["value_type"] = currentValue?.GetType().FullName ?? "null",
                ["object_name"] = objectName,
                ["component_type"] = componentType ?? "GameObject"
            };

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleGetMemberValue: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000,
                "Failed to get member value",
                new { details = ex.Message, stack_trace = ex.StackTrace }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }
}

