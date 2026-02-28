use rmcp::model::{CallToolResult, JsonObject};
use serde_json::Value;

use crate::mcp::tools::PlayerArgs;
use crate::server_state::ServerState;

use super::common::{call_and_format_result, text_result};

const TOOL_NAME: &str = "s1_player";

pub async fn handle_player(arguments: Option<JsonObject>, state: &ServerState) -> CallToolResult {
    let raw = Value::Object(arguments.unwrap_or_default());
    let args: PlayerArgs = match serde_json::from_value(raw) {
        Ok(a) => a,
        Err(e) => return text_result(format!("Error: invalid arguments: {e}")),
    };

    match args.action.as_str() {
        "get" => {
            call_and_format_result(state, "get_player", Some(serde_json::json!({})), TOOL_NAME, "get").await
        }
        "get_inventory" => {
            call_and_format_result(
                state,
                "get_player_inventory",
                Some(serde_json::json!({})),
                TOOL_NAME,
                "get_inventory",
            )
            .await
        }
        "teleport" => {
            let Some(pos) = args.position else {
                return text_result("Error: position is required for teleport");
            };
            call_and_format_result(
                state,
                "teleport_player",
                Some(serde_json::json!({ "position": { "x": pos.x, "y": pos.y, "z": pos.z } })),
                TOOL_NAME,
                "teleport",
            )
            .await
        }
        "add_item" => {
            let Some(item_id) = args.item_id else {
                return text_result("Error: item_id is required for add_item");
            };
            let quantity = args.quantity.unwrap_or(1);
            if quantity < 1 {
                return text_result("Error: quantity must be a positive integer");
            }
            call_and_format_result(
                state,
                "add_item_to_player",
                Some(serde_json::json!({ "item_id": item_id, "quantity": quantity })),
                TOOL_NAME,
                "add_item",
            )
            .await
        }
        other => text_result(format!("Error: Unknown action '{other}'")),
    }
}

#[cfg(test)]
mod tests {
    use serde_json::json;

    use crate::mcp::tools::PlayerArgs;

    #[test]
    fn player_args_default_quantity() {
        let args: PlayerArgs =
            serde_json::from_value(json!({ "action": "add_item", "item_id": "weed_brick" }))
                .unwrap();
        assert_eq!(args.quantity, None);
    }

    #[test]
    fn player_args_rejects_invalid_action() {
        // No error at the serde level; action validation is in the match arm.
        let args: PlayerArgs =
            serde_json::from_value(json!({ "action": "fly" })).unwrap();
        assert_eq!(args.action, "fly");
    }
}
