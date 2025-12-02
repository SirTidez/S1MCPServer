using System;
using System.Collections.Generic;
using S1MCPServer.Core;
using S1MCPServer.Models;
using S1MCPServer.Utils;
using UnityEngine;

namespace S1MCPServer.Handlers.Debug;

/// <summary>
/// Handles modification-related debug commands (setting fields, properties, calling methods, setting active state, setting transform).
/// </summary>
public class DebugModificationHandler : DebugHandlerBase
{
    public DebugModificationHandler(ResponseQueue responseQueue) : base(responseQueue)
    {
    }

    public override void Handle(Request request)
    {
        switch (request.Method)
        {
            case "set_field":
                HandleSetField(request);
                break;
            case "set_component_property":
                HandleSetProperty(request);
                break;
            case "call_method":
                HandleCallMethod(request);
                break;
            case "set_active":
                HandleSetActive(request);
                break;
            case "set_transform":
                HandleSetTransform(request);
                break;
            default:
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32601,
                    $"Unknown modification method: {request.Method}"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                break;
        }
    }

    private void HandleSetField(Request request)
    {
        try
        {
            if (request.Params == null || !request.Params.TryGetValue("object_name", out var objectNameObj) ||
                !request.Params.TryGetValue("field_name", out var fieldNameObj) ||
                !request.Params.TryGetValue("value", out var valueObj))
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602,
                    "object_name, field_name, and value parameters are required"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            string objectName = objectNameObj?.ToString() ?? string.Empty;
            string fieldName = fieldNameObj?.ToString() ?? string.Empty;
            object? value = valueObj;
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

            bool success = ReflectionEngine.SetFieldValue(target, fieldName, value);
            var result = new Dictionary<string, object>
            {
                ["field_name"] = fieldName,
                ["success"] = success
            };

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleSetField: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000,
                "Failed to set field",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }

    private void HandleSetProperty(Request request)
    {
        try
        {
            if (request.Params == null || !request.Params.TryGetValue("object_name", out var objectNameObj) ||
                !request.Params.TryGetValue("property_name", out var propertyNameObj) ||
                !request.Params.TryGetValue("value", out var valueObj))
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602,
                    "object_name, property_name, and value parameters are required"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            string objectName = objectNameObj?.ToString() ?? string.Empty;
            string propertyName = propertyNameObj?.ToString() ?? string.Empty;
            object? value = valueObj;
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

            bool success = ReflectionEngine.SetPropertyValue(target, propertyName, value);
            var result = new Dictionary<string, object>
            {
                ["property_name"] = propertyName,
                ["success"] = success
            };

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleSetProperty: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000,
                "Failed to set property",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }

    private void HandleCallMethod(Request request)
    {
        try
        {
            if (request.Params == null || !request.Params.TryGetValue("object_name", out var objectNameObj) ||
                !request.Params.TryGetValue("method_name", out var methodNameObj))
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602,
                    "object_name and method_name parameters are required"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            string objectName = objectNameObj?.ToString() ?? string.Empty;
            string methodName = methodNameObj?.ToString() ?? string.Empty;
            object[]? parameters = null;
            string? componentType = null;
            if (request.Params.TryGetValue("component_type", out var compTypeObj))
                componentType = compTypeObj?.ToString();
            if (request.Params.TryGetValue("parameters", out var paramsObj) && paramsObj is List<object> paramList)
                parameters = paramList.ToArray();

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

            var returnValue = ReflectionEngine.InvokeMethod(target, methodName, parameters);
            var result = new Dictionary<string, object>
            {
                ["method_name"] = methodName,
                ["return_value"] = ObjectSerializer.SerializeObject(returnValue)
            };

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleCallMethod: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000,
                "Failed to call method",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }

    private void HandleSetActive(Request request)
    {
        try
        {
            if (request.Params == null || !request.Params.TryGetValue("object_name", out var objectNameObj) ||
                !request.Params.TryGetValue("active", out var activeObj))
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602,
                    "object_name and active parameters are required"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            string objectName = objectNameObj?.ToString() ?? string.Empty;
            bool active = false;
            if (!bool.TryParse(activeObj?.ToString(), out active))
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602,
                    "active parameter must be a boolean"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

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

            bool success = false;
            if (!string.IsNullOrEmpty(componentType))
            {
                var compType = TypeResolver.ResolveComponentType(componentType);
                if (compType != null)
                {
                    var component = ReflectionHelper.GetComponent(gameObject, compType);
                    if (component is MonoBehaviour mb)
                    {
                        mb.enabled = active;
                        success = true;
                    }
                }
            }
            else
            {
                gameObject.SetActive(active);
                success = true;
            }

            var result = new Dictionary<string, object>
            {
                ["object_name"] = objectName,
                ["active"] = active,
                ["success"] = success
            };

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleSetActive: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000,
                "Failed to set active state",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }

    private void HandleSetTransform(Request request)
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
            bool changed = false;

            if (request.Params.TryGetValue("position", out var posObj) && posObj is Dictionary<string, object> posDict)
            {
                if (posDict.TryGetValue("x", out var x) && posDict.TryGetValue("y", out var y) && posDict.TryGetValue("z", out var z))
                {
                    float.TryParse(x?.ToString(), out var fx);
                    float.TryParse(y?.ToString(), out var fy);
                    float.TryParse(z?.ToString(), out var fz);
                    transform.position = new Vector3(fx, fy, fz);
                    changed = true;
                }
            }

            if (request.Params.TryGetValue("rotation", out var rotObj) && rotObj is Dictionary<string, object> rotDict)
            {
                if (rotDict.TryGetValue("x", out var x) && rotDict.TryGetValue("y", out var y) && 
                    rotDict.TryGetValue("z", out var z) && rotDict.TryGetValue("w", out var w))
                {
                    float.TryParse(x?.ToString(), out var fx);
                    float.TryParse(y?.ToString(), out var fy);
                    float.TryParse(z?.ToString(), out var fz);
                    float.TryParse(w?.ToString(), out var fw);
                    transform.rotation = new Quaternion(fx, fy, fz, fw);
                    changed = true;
                }
            }

            if (request.Params.TryGetValue("scale", out var scaleObj) && scaleObj is Dictionary<string, object> scaleDict)
            {
                if (scaleDict.TryGetValue("x", out var x) && scaleDict.TryGetValue("y", out var y) && scaleDict.TryGetValue("z", out var z))
                {
                    float.TryParse(x?.ToString(), out var fx);
                    float.TryParse(y?.ToString(), out var fy);
                    float.TryParse(z?.ToString(), out var fz);
                    transform.localScale = new Vector3(fx, fy, fz);
                    changed = true;
                }
            }

            var result = new Dictionary<string, object>
            {
                ["object_name"] = objectName,
                ["success"] = changed
            };

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleSetTransform: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000,
                "Failed to set transform",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }
}

