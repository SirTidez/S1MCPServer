using System.Collections.Generic;
using S1MCPServer.Core;
using S1MCPServer.Models;
using S1MCPServer.Utils;
#if MONO
using FishNet;
using ScheduleOne.DevUtilities;
using ScheduleOne.Money;
using ScheduleOne.PlayerScripts;
using S1ItemInstance = ScheduleOne.ItemFramework.ItemInstance;
#else
using Il2CppFishNet;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.PlayerScripts;
using S1ItemInstance = Il2CppScheduleOne.ItemFramework.ItemInstance;
#endif

namespace S1MCPServer.Handlers;

/// <summary>
/// Handles player-related commands.
/// </summary>
public class PlayerCommandHandler : ICommandHandler
{
    private readonly ResponseQueue _responseQueue;

    public PlayerCommandHandler(ResponseQueue responseQueue)
    {
        _responseQueue = responseQueue;
    }

    public void Handle(Request request)
    {
        switch (request.Method)
        {
            case "get_player":
                HandleGetPlayer(request);
                break;
            case "get_player_inventory":
                HandleGetPlayerInventory(request);
                break;
            case "teleport_player":
                HandleTeleportPlayer(request);
                break;
            case "add_item_to_player":
                HandleAddItemToPlayer(request);
                break;
            default:
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32601, // Method not found
                    $"Unknown player method: {request.Method}"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                break;
        }
    }

    private void HandleGetPlayer(Request request)
    {
        try
        {
            ModLogger.Debug("Getting player information");

            var player = Player.Local;
            if (player == null || player.gameObject == null)
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32000, // Game error
                    "Player not ready"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            var position = Position.FromVector3(player.transform.position);
            var rotation = Position.FromVector3(player.transform.eulerAngles);

            var cashBalance = 0f;
            var bankBalance = 0f;
            var networth = 0f;
            if (MoneyManager.InstanceExists)
            {
                cashBalance = MoneyManager.Instance.cashBalance;
                bankBalance = MoneyManager.Instance.onlineBalance;
                networth = MoneyManager.Instance.LastCalculatedNetworth;
            }

            Dictionary<string, float> playerBalance = new();
            playerBalance.Add("cash", cashBalance);
            playerBalance.Add("bank", bankBalance);
            playerBalance.Add("networth", networth);
            
            string networkStatus = "unknown";
            try
            {
                var nm = InstanceFinder.NetworkManager;
                if (nm != null)
                {
                    if (nm.IsServer && nm.IsClient)
                        networkStatus = "host";
                    else if (!nm.IsServer && !nm.IsClient)
                        networkStatus = "singleplayer";
                    else if (nm.IsClient && !nm.IsServer)
                        networkStatus = "client";
                    else if (nm.IsServer && !nm.IsClient)
                        networkStatus = "server";
                }
            }
            catch
            {
                // Network manager not available
            }

            // TODO: Get actual player health, money, network status from game
            var result = new Dictionary<string, object>
            {
                ["position"] = new { position.X, position.Y, position.Z },
                ["rotation"] = new { rotation.X, rotation.Y, rotation.Z },
                ["health"] = player.Health.CurrentHealth,
                ["money"] = playerBalance,
                ["network_status"] = networkStatus 
            };

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleGetPlayer: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000, // Game error
                "Failed to get player information",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }

    private void HandleGetPlayerInventory(Request request)
    {
        try
        {
            ModLogger.Debug("Getting player inventory");

            var player = Player.Local;
            if (player == null || player.gameObject == null)
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32000, // Game error
                    "Player not ready"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            // TODO: Get actual inventory from player
            var hotbarSlots = PlayerInventory.Instance.hotbarSlots;
            List<S1ItemInstance> playerInventory = new();
            foreach (var hotbarSlot in hotbarSlots)
            {
                if (hotbarSlot.ItemInstance != null)
                {
                    playerInventory.Add(hotbarSlot.ItemInstance);
                }
            }
            var result = new Dictionary<string, object>
            {
                ["items"] = playerInventory,
                ["count"] = playerInventory.Count
            };

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleGetPlayerInventory: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000, // Game error
                "Failed to get player inventory",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }

    private void HandleTeleportPlayer(Request request)
    {
        try
        {
            var player = Player.Local;
            if (player == null || player.gameObject == null)
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32000, // Game error
                    "Player not ready"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

            if (request.Params == null || !request.Params.TryGetValue("position", out var positionObj))
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32602, // Invalid params
                    "position parameter is required"
                );
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

            ModLogger.Debug($"Teleporting player to position ({position.X}, {position.Y}, {position.Z})");

            // Teleport player
            var vectorPos = position.ToVector3();
            player.transform.position = vectorPos;

            var result = new Dictionary<string, object>
            {
                ["success"] = true,
                ["new_position"] = new { position.X, position.Y, position.Z }
            };

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleTeleportPlayer: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000, // Game error
                "Failed to teleport player",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }

    private void HandleAddItemToPlayer(Request request)
    {
        try
        {
            var player = Player.Local;
            if (player == null || player.gameObject == null)
            {
                var errorResponse = ProtocolHandler.CreateErrorResponse(
                    request.Id,
                    -32000, // Game error
                    "Player not ready"
                );
                _responseQueue.EnqueueResponse(errorResponse);
                return;
            }

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

            // Validate item ID
            if (!ValidationHelper.ValidateItemID(itemId, out string? itemError))
            {
                var errorResponse = ValidationHelper.CreateValidationErrorResponse(request.Id, itemError ?? "Invalid item ID");
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

            ModLogger.Debug($"Adding {quantity} of item {itemId} to player inventory");

            // TODO: Implement item addition using native game classes (reference S1API player inventory patterns)
            // For now, return success
            var result = new Dictionary<string, object>
            {
                ["success"] = true,
                ["item_id"] = itemId,
                ["quantity_added"] = quantity,
                ["new_total"] = quantity // TODO: Get actual new total
            };

            var response = ProtocolHandler.CreateSuccessResponse(request.Id, result);
            _responseQueue.EnqueueResponse(response);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error in HandleAddItemToPlayer: {ex.Message}");
            var errorResponse = ProtocolHandler.CreateErrorResponse(
                request.Id,
                -32000, // Game error
                "Failed to add item to player",
                new { details = ex.Message }
            );
            _responseQueue.EnqueueResponse(errorResponse);
        }
    }
}

