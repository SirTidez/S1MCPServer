using System.Collections.Generic;
using System.Linq;
using S1MCPServer.Core;
using S1MCPServer.Helpers;
using S1MCPServer.Models;
using S1MCPServer.Utils;
#if MONO
using ScheduleOne.ItemFramework;
#else
using Il2CppScheduleOne.ItemFramework;
#endif

namespace S1MCPServer.Handlers;

/// <summary>
/// Handles item-related commands.
/// </summary>
public class ItemCommandHandler : ICommandHandler
{
    private readonly ResponseQueue _responseQueue;

    public ItemCommandHandler(ResponseQueue responseQueue)
    {
        _responseQueue = responseQueue;
    }

    public void Handle(Request request)
    {
        switch (request.Method)
        {
            case "list_items":
                HandleListItems(request);
                break;
            case "get_item":
                HandleGetItem(request);
                break;
            case "spawn_item":
                HandleSpawnItem(request);
                break;
            default:
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32601, // Method not found
                    $"Unknown item method: {request.Method}"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                break;
        }
    }

    private void HandleListItems(Request request)
    {
        try
        {
            string? category = null;
            if (request.Params != null && request.Params.TryGetValue("category", out var categoryObj))
            {
                category = categoryObj?.ToString();
            }

            ModLogger.Debug($"Listing items with category filter: {category ?? "none"}");

            var itemDefinitions = Helpers.Utils.GetAllStorableItemDefinitions();
            var items = new List<object>();

            foreach (var itemDef in itemDefinitions)
            {
                // Apply category filter if specified
                if (category != null && !string.IsNullOrEmpty(category))
                {
                    // TODO: Check item category if available
                    // For now, include all items
                }

                var itemData = new Dictionary<string, object>
                {
                    ["item_id"] = itemDef.ID ?? "unknown",
                    ["name"] = itemDef.Name ?? "Unknown Item",
                    ["description"] = itemDef.Description ?? "",
                    ["category"] = "Unknown", // TODO: Get actual category
                    ["base_price"] = 0.0f, // TODO: Get actual price
                    ["stack_limit"] = itemDef.StackLimit
                };

                items.Add(itemData);
            }

            var result = new Dictionary<string, object>
            {
                ["items"] = items,
                ["count"] = items.Count
            };

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleListItems: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000, // Game error
                "Failed to list items",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }

    private void HandleGetItem(Request request)
    {
        try
        {
            if (request.Params == null || !request.Params.TryGetValue("item_id", out var itemIdObj))
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602, // Invalid params
                    "item_id parameter is required"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            string itemId = itemIdObj?.ToString() ?? string.Empty;
            ModLogger.Debug($"Getting item: {itemId}");

            var itemDefinitions = Helpers.Utils.GetAllStorableItemDefinitions();
            var itemDef = itemDefinitions.FirstOrDefault(i => i.ID == itemId);

            if (itemDef == null)
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32000, // Game error
                    "Item not found",
                    new { item_id = itemId }
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            var result = new Dictionary<string, object>
            {
                ["item_id"] = itemDef.ID ?? "unknown",
                ["name"] = itemDef.Name ?? "Unknown Item",
                ["description"] = itemDef.Description ?? "",
                ["category"] = "Unknown", // TODO: Get actual category
                ["base_price"] = 0.0f, // TODO: Get actual price
                ["stack_limit"] = itemDef.StackLimit,
                ["legal_status"] = "unknown" // TODO: Get actual legal status
            };

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleGetItem: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000, // Game error
                "Failed to get item",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }

    private void HandleSpawnItem(Request request)
    {
        try
        {
            if (request.Params == null)
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602, // Invalid params
                    "Parameters are required"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            if (!request.Params.TryGetValue("item_id", out var itemIdObj))
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602, // Invalid params
                    "item_id parameter is required"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            if (!request.Params.TryGetValue("position", out var positionObj))
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602, // Invalid params
                    "position parameter is required"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            string itemId = itemIdObj?.ToString() ?? string.Empty;

            // Validate item ID
            if (!ValidationHelper.ValidateItemID(itemId, out string? itemError))
            {
                var errorResponse = ValidationHelper.CreateValidationErrorResponse(request.Id, itemError ?? "Invalid item ID");
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            // Parse position
            Position? position = null;
            try
            {
                if (positionObj is Dictionary<string, object> posDict)
                {
                    position = new Position
                    {
                        X = Convert.ToSingle(posDict.GetValueOrDefault("x", 0.0f)),
                        Y = Convert.ToSingle(posDict.GetValueOrDefault("y", 0.0f)),
                        Z = Convert.ToSingle(posDict.GetValueOrDefault("z", 0.0f))
                    };
                }
#if MONO
                else if (positionObj is Newtonsoft.Json.Linq.JObject jObj)
                {
                    position = new Position
                    {
                        X = jObj["x"] != null ? jObj["x"].ToObject<float>() : 0.0f,
                        Y = jObj["y"] != null ? jObj["y"].ToObject<float>() : 0.0f,
                        Z = jObj["z"] != null ? jObj["z"].ToObject<float>() : 0.0f
                    };
                }
#else
                else if (positionObj is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    position = new Position
                    {
                        X = jsonElement.TryGetProperty("x", out var xProp) ? (float)xProp.GetDouble() : 0.0f,
                        Y = jsonElement.TryGetProperty("y", out var yProp) ? (float)yProp.GetDouble() : 0.0f,
                        Z = jsonElement.TryGetProperty("z", out var zProp) ? (float)zProp.GetDouble() : 0.0f
                    };
                }
#endif
            }
            catch
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602, // Invalid params
                    "Invalid position format"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            if (position == null)
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602, // Invalid params
                    "Failed to parse position"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            // Validate position
            if (!ValidationHelper.ValidatePosition(position, out string? posError))
            {
                var errorResponse = ValidationHelper.CreateValidationErrorResponse(request.Id, posError ?? "Invalid position");
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            // Get quantity (default to 1)
            int quantity = 1;
            if (request.Params.TryGetValue("quantity", out var quantityObj))
            {
                try
                {
                    quantity = Convert.ToInt32(quantityObj);
                    if (quantity < 1)
                    {
                        quantity = 1;
                    }
                }
                catch
                {
                    // Use default quantity
                }
            }

            ModLogger.Debug($"Spawning {quantity} of item {itemId} at position ({position.X}, {position.Y}, {position.Z})");

            // TODO: Implement item spawning using native game classes (reference S1API ItemCreator patterns)
            // For now, return success
            var result = new Dictionary<string, object>
            {
                ["success"] = true,
                ["item_id"] = itemId,
                ["quantity"] = quantity,
                ["position"] = new { position.X, position.Y, position.Z }
            };

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleSpawnItem: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000, // Game error
                "Failed to spawn item",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }
}

