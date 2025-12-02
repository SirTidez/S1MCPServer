using System;
using System.Collections.Generic;
using System.Linq;
using S1MCPServer.Core;
using S1MCPServer.Models;
using S1MCPServer.Utils;
using UnityEngine;

namespace S1MCPServer.Handlers.Debug;

/// <summary>
/// Handles type inspection commands (inspecting types, listing members).
/// </summary>
public class DebugTypeInspectionHandler : DebugHandlerBase
{
    public DebugTypeInspectionHandler(ResponseQueue responseQueue) : base(responseQueue)
    {
    }

    public override void Handle(Request request)
    {
        switch (request.Method)
        {
            case "inspect_type":
                HandleInspectType(request);
                break;
            case "list_members":
                HandleListMembers(request);
                break;
            default:
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32601,
                    $"Unknown type inspection method: {request.Method}"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                break;
        }
    }

    private void HandleInspectType(Request request)
    {
        try
        {
            if (request.Params == null || !request.Params.TryGetValue("type_name", out var typeNameObj))
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602,
                    "type_name parameter is required"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            string typeName = typeNameObj?.ToString() ?? string.Empty;
            ModLogger.Debug($"Inspecting type: {typeName}");

            Type? type = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    type = assembly.GetType(typeName);
                    if (type != null)
                        break;
                }
                catch { }
            }

            if (type == null)
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32000,
                    "Type not found",
                    new { type_name = typeName }
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            var result = ObjectSerializer.SerializeType(type);

            bool includePrivate = false;
            if (request.Params != null && request.Params.TryGetValue("include_private", out var privateObj))
                bool.TryParse(privateObj?.ToString(), out includePrivate);

            var fields = ReflectionEngine.GetFields(type, includePrivate);
            var properties = ReflectionEngine.GetProperties(type, includePrivate);
            var methods = ReflectionEngine.GetMethods(type, includePrivate);

            result["fields"] = fields.Select(f => new Dictionary<string, object>
            {
                ["name"] = f.Name,
                ["type"] = f.FieldType.Name,
                ["full_type"] = f.FieldType.FullName ?? "Unknown",
                ["is_static"] = f.IsStatic,
                ["is_public"] = f.IsPublic,
                ["is_readonly"] = f.IsInitOnly
            }).ToList();

            result["properties"] = properties.Select(p => new Dictionary<string, object>
            {
                ["name"] = p.Name,
                ["type"] = p.PropertyType.Name,
                ["full_type"] = p.PropertyType.FullName ?? "Unknown",
                ["can_read"] = p.CanRead,
                ["can_write"] = p.CanWrite
            }).ToList();

            result["methods"] = methods.Select(m => new Dictionary<string, object>
            {
                ["name"] = m.Name,
                ["return_type"] = m.ReturnType.Name,
                ["full_return_type"] = m.ReturnType.FullName ?? "Unknown",
                ["parameters"] = m.GetParameters().Select(p => new Dictionary<string, object>
                {
                    ["name"] = p.Name,
                    ["type"] = p.ParameterType.Name,
                    ["full_type"] = p.ParameterType.FullName ?? "Unknown",
                    ["is_optional"] = p.IsOptional,
                    ["has_default_value"] = p.HasDefaultValue
                }).ToList(),
                ["is_static"] = m.IsStatic,
                ["is_public"] = m.IsPublic
            }).ToList();

            result["field_count"] = fields.Length;
            result["property_count"] = properties.Length;
            result["method_count"] = methods.Length;

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleInspectType: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000,
                "Failed to inspect type",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }

    private void HandleListMembers(Request request)
    {
        try
        {
            if (request.Params == null)
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602,
                    "Parameters required"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            bool includePrivate = false;
            if (request.Params.TryGetValue("include_private", out var privateObj))
                bool.TryParse(privateObj?.ToString(), out includePrivate);

            Type? type = null;
            object? instance = null;

            if (request.Params.TryGetValue("type_name", out var typeNameObj))
            {
                string typeName = typeNameObj?.ToString() ?? string.Empty;
                type = ReflectionEngine.ResolveType(typeName);
            }

            if (request.Params.TryGetValue("object_name", out var objectNameObj))
            {
                string objectName = objectNameObj?.ToString() ?? string.Empty;
                var gameObject = ReflectionHelper.FindGameObject(objectName);
                if (gameObject != null)
                {
                    instance = gameObject;
                    type = gameObject.GetType();
                }
            }

            if (type == null)
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602,
                    "Either type_name or object_name parameter is required"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            var fields = ReflectionEngine.GetFields(type, includePrivate);
            var properties = ReflectionEngine.GetProperties(type, includePrivate);
            var methods = ReflectionEngine.GetMethods(type, includePrivate);

            var result = new Dictionary<string, object>
            {
                ["type"] = type.Name,
                ["full_type"] = type.FullName ?? "Unknown",
                ["fields"] = fields.Select(f => new Dictionary<string, object>
                {
                    ["name"] = f.Name,
                    ["type"] = f.FieldType.Name,
                    ["is_static"] = f.IsStatic,
                    ["is_public"] = f.IsPublic
                }).ToList(),
                ["properties"] = properties.Select(p => new Dictionary<string, object>
                {
                    ["name"] = p.Name,
                    ["type"] = p.PropertyType.Name,
                    ["can_read"] = p.CanRead,
                    ["can_write"] = p.CanWrite
                }).ToList(),
                ["methods"] = methods.Select(m => new Dictionary<string, object>
                {
                    ["name"] = m.Name,
                    ["return_type"] = m.ReturnType.Name,
                    ["parameters"] = m.GetParameters().Select(p => new Dictionary<string, object>
                    {
                        ["name"] = p.Name,
                        ["type"] = p.ParameterType.Name
                    }).ToList(),
                    ["is_static"] = m.IsStatic
                }).ToList()
            };

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleListMembers: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000,
                "Failed to list members",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }
}

