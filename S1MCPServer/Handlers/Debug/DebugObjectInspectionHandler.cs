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
/// Handles object inspection commands (inspecting GameObjects).
/// </summary>
public class DebugObjectInspectionHandler : DebugHandlerBase
{
    public DebugObjectInspectionHandler(ResponseQueue responseQueue) : base(responseQueue)
    {
    }

    public override void Handle(Request request)
    {
        switch (request.Method)
        {
            case "inspect_object":
                HandleInspectObject(request);
                break;
            default:
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32601,
                    $"Unknown object inspection method: {request.Method}"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                break;
        }
    }

    private void HandleInspectObject(Request request)
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
            ModLogger.Debug($"Inspecting object: {objectName}");

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

            var result = InspectGameObjectWithReflection(gameObject);

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleInspectObject: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000,
                "Failed to inspect object",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }

    private Dictionary<string, object> InspectGameObjectWithReflection(GameObject gameObject)
    {
        var result = new Dictionary<string, object>
        {
            ["object_name"] = gameObject.name,
            ["object_type"] = "GameObject",
            ["full_type"] = gameObject.GetType().FullName ?? "Unknown"
        };

        var transform = gameObject.transform;
        result["position"] = new { x = transform.position.x, y = transform.position.y, z = transform.position.z };
        result["rotation"] = new { x = transform.rotation.x, y = transform.rotation.y, z = transform.rotation.z, w = transform.rotation.w };
        result["scale"] = new { x = transform.localScale.x, y = transform.localScale.y, z = transform.localScale.z };

        var components = new List<Dictionary<string, object>>();
        var componentSet = new HashSet<Component>();
        
        try
        {
            var allComponents = gameObject.GetComponents<Component>();
            foreach (var component in allComponents)
            {
                if (component != null)
                    componentSet.Add(component);
            }
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"Error getting Component types: {ex.Message}");
        }

        try
        {
            var monoBehaviours = gameObject.GetComponents<MonoBehaviour>();
            foreach (var mb in monoBehaviours)
            {
                if (mb != null)
                    componentSet.Add(mb);
            }
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"Error getting MonoBehaviour types: {ex.Message}");
        }

        foreach (var component in componentSet)
        {
            if (component == null) continue;

            try
            {
                var componentData = InspectComponent(component);
                components.Add(componentData);
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"Error inspecting component {component.GetType().Name}: {ex.Message}");
                components.Add(new Dictionary<string, object>
                {
                    ["type"] = component.GetType().Name,
                    ["full_type"] = component.GetType().FullName ?? "Unknown",
                    ["error"] = ex.Message
                });
            }
        }

        result["components"] = components;
        result["component_count"] = components.Count;
        result["component_types"] = components.Select(c => c.ContainsKey("type") ? c["type"]?.ToString() : "Unknown").ToList();

        var hierarchy = new List<Dictionary<string, object>>();
        for (int i = 0; i < transform.childCount; i++)
        {
            var child = transform.GetChild(i);
            hierarchy.Add(new Dictionary<string, object>
            {
                ["name"] = child.name,
                ["path"] = GetGameObjectPath(child.gameObject)
            });
        }
        result["children"] = hierarchy;
        result["child_count"] = hierarchy.Count;

        return result;
    }

    private Dictionary<string, object> InspectComponent(Component component)
    {
        var componentData = new Dictionary<string, object>
        {
            ["type"] = component.GetType().Name,
            ["full_type"] = component.GetType().FullName ?? "Unknown"
        };

        var properties = new Dictionary<string, object>();
        try
        {
            var props = component.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            foreach (var prop in props.Take(20))
            {
                try
                {
                    if (ValueFormatter.IsBackingField(prop.Name) || ValueFormatter.IsIntPtrType(prop.PropertyType))
                        continue;

                    if (prop.CanRead && prop.GetIndexParameters().Length == 0)
                    {
                        var value = prop.GetValue(component);
                        if (value != null && ValueFormatter.IsIntPtrType(value.GetType()))
                            continue;
                            
                        var formattedValue = ValueFormatter.FormatValue(value);
                        if (formattedValue != null)
                        {
                            properties[prop.Name] = formattedValue;
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
        componentData["properties"] = properties;

        var fields = new Dictionary<string, object>();
        try
        {
            var fieldInfos = component.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            foreach (var field in fieldInfos.Take(20))
            {
                try
                {
                    if (ValueFormatter.IsBackingField(field.Name) || ValueFormatter.IsIntPtrType(field.FieldType))
                        continue;

                    var value = ReflectionHelper.GetFieldValue(component, field.Name);
                    
                    if (value != null && ValueFormatter.IsIntPtrType(value.GetType()))
                        continue;
                        
                    var formattedValue = ValueFormatter.FormatValue(value);
                    if (formattedValue != null)
                    {
                        fields[field.Name] = formattedValue;
                    }
                }
                catch { }
            }
        }
        catch { }
        componentData["fields"] = fields;

        return componentData;
    }
}

