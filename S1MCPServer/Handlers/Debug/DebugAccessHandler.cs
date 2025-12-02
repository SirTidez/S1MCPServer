using System;
using System.Collections.Generic;
using S1MCPServer.Core;
using S1MCPServer.Models;
using S1MCPServer.Utils;
using UnityEngine;

namespace S1MCPServer.Handlers.Debug;

/// <summary>
/// Handles access-related debug commands (getting fields, properties, checking active state, getting transform).
/// </summary>
public class DebugAccessHandler : DebugHandlerBase
{
    public DebugAccessHandler(ResponseQueue responseQueue) : base(responseQueue)
    {
    }

    public override void Handle(Request request)
    {
        switch (request.Method)
        {
            case "get_field":
                HandleGetField(request);
                break;
            case "get_component_property":
                HandleGetProperty(request);
                break;
            case "is_active":
                HandleIsActive(request);
                break;
            case "get_transform":
                HandleGetTransform(request);
                break;
            default:
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32601,
                    $"Unknown access method: {request.Method}"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                break;
        }
    }

    private void HandleGetField(Request request)
    {
        try
        {
            if (request.Params == null || !request.Params.TryGetValue("object_name", out var objectNameObj) ||
                !request.Params.TryGetValue("field_name", out var fieldNameObj))
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602,
                    "object_name and field_name parameters are required"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            string objectName = objectNameObj?.ToString() ?? string.Empty;
            string fieldName = fieldNameObj?.ToString() ?? string.Empty;
            string? componentType = null;
            if (request.Params.TryGetValue("component_type", out var compTypeObj))
                componentType = compTypeObj?.ToString();

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

            object? target = gameObject;
            if (!string.IsNullOrEmpty(componentType))
            {
                var compType = TypeResolver.ResolveComponentType(componentType);
                if (compType == null)
                {
                    var errorResponse = ProtocolHandler.CreateErrorResponse(
                        request.Id,
                        -32000,
                        "Component type not found",
                        new { component_type = componentType }
                    );
                    _responseQueue.EnqueueResponse(errorResponse);
                    return;
                }
                target = ReflectionHelper.GetComponent(gameObject, compType);
                if (target == null)
                {
                    var errorResponse = ProtocolHandler.CreateErrorResponse(
                        request.Id,
                        -32000,
                        "Component not found on GameObject",
                        new { component_type = componentType }
                    );
                    _responseQueue.EnqueueResponse(errorResponse);
                    return;
                }
            }

            var value = ReflectionEngine.GetFieldValue(target, fieldName);
            var result = new Dictionary<string, object>
            {
                ["field_name"] = fieldName,
                ["value"] = ObjectSerializer.SerializeObject(value)
            };

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleGetField: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000,
                "Failed to get field",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }

    private void HandleGetProperty(Request request)
    {
        try
        {
            if (request.Params == null || !request.Params.TryGetValue("object_name", out var objectNameObj) ||
                !request.Params.TryGetValue("property_name", out var propertyNameObj))
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602,
                    "object_name and property_name parameters are required"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            string objectName = objectNameObj?.ToString() ?? string.Empty;
            string propertyName = propertyNameObj?.ToString() ?? string.Empty;
            string? componentType = null;
            if (request.Params.TryGetValue("component_type", out var compTypeObj))
                componentType = compTypeObj?.ToString();

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

            object? target = gameObject;
            if (!string.IsNullOrEmpty(componentType))
            {
                var compType = TypeResolver.ResolveComponentType(componentType);
                if (compType == null)
                {
                    var errorResponse = ProtocolHandler.CreateErrorResponse(
                        request.Id,
                        -32000,
                        "Component type not found",
                        new { component_type = componentType }
                    );
                    _responseQueue.EnqueueResponse(errorResponse);
                    return;
                }
                target = ReflectionHelper.GetComponent(gameObject, compType);
                if (target == null)
                {
                    var errorResponse = ProtocolHandler.CreateErrorResponse(
                        request.Id,
                        -32000,
                        "Component not found on GameObject",
                        new { component_type = componentType }
                    );
                    _responseQueue.EnqueueResponse(errorResponse);
                    return;
                }
            }

            var value = ReflectionEngine.GetPropertyValue(target, propertyName);
            var result = new Dictionary<string, object>
            {
                ["property_name"] = propertyName,
                ["value"] = ObjectSerializer.SerializeObject(value)
            };

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleGetProperty: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000,
                "Failed to get property",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }

    private void HandleIsActive(Request request)
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
            string? componentType = null;
            if (request.Params.TryGetValue("component_type", out var compTypeObj))
                componentType = compTypeObj?.ToString();

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

            bool isActive = gameObject.activeSelf;
            if (!string.IsNullOrEmpty(componentType))
            {
                var compType = TypeResolver.ResolveComponentType(componentType);
                if (compType != null)
                {
                    var component = ReflectionHelper.GetComponent(gameObject, compType);
                    if (component is MonoBehaviour mb)
                        isActive = mb.enabled && gameObject.activeSelf;
                }
            }

            var result = new Dictionary<string, object>
            {
                ["object_name"] = objectName,
                ["is_active"] = isActive
            };

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleIsActive: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000,
                "Failed to check active state",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }

    private void HandleGetTransform(Request request)
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

            var transform = gameObject.transform;
            var result = new Dictionary<string, object>
            {
                ["position"] = ObjectSerializer.SerializeObject(transform.position),
                ["rotation"] = ObjectSerializer.SerializeObject(transform.rotation),
                ["scale"] = ObjectSerializer.SerializeObject(transform.localScale)
            };

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleGetTransform: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000,
                "Failed to get transform",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }
}

