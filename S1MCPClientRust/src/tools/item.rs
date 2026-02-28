use rmcp::model::{CallToolResult, JsonObject};
use serde_json::Value;

use crate::mcp::tools::ItemArgs;
use crate::server_state::ServerState;

use super::common::{call_and_format_result, text_result};

const TOOL_NAME: &str = "s1_item";

pub async fn handle_item(arguments: Option<JsonObject>, state: &ServerState) -> CallToolResult {
    let raw = Value::Object(arguments.unwrap_or_default());
    let args: ItemArgs = match serde_json::from_value(raw) {
        Ok(a) => a,
        Err(e) => return text_result(format!("Error: invalid arguments: {e}")),
    };

    match args.action.as_str() {
        "list" => {
            let params = args
                .category
                .map(|c| serde_json::json!({ "category": c }));
            call_and_format_result(state, "list_items", params, TOOL_NAME, "list").await
        }
        "get" => {
            let Some(item_id) = args.item_id else {
                return text_result("Error: item_id is required");
            };
            call_and_format_result(
                state,
                "get_item",
                Some(serde_json::json!({ "item_id": item_id })),
                TOOL_NAME,
                "get",
            )
            .await
        }
        "spawn" => {
            let Some(item_id) = args.item_id else {
                return text_result("Error: item_id is required");
            };
            let Some(pos) = args.position else {
                return text_result("Error: position is required");
            };
            let quantity = args.quantity.unwrap_or(1);
            if quantity < 1 {
                return text_result("Error: quantity must be a positive integer");
            }
            let mut params = serde_json::json!({
                "item_id": item_id,
                "position": { "x": pos.x, "y": pos.y, "z": pos.z }
            });
            if quantity != 1 {
                params["quantity"] = serde_json::json!(quantity);
            }
            call_and_format_result(state, "spawn_item", Some(params), TOOL_NAME, "spawn").await
        }
        other => text_result(format!("Error: Unknown action '{other}'")),
    }
}

#[cfg(test)]
mod tests {
    use serde_json::json;

    use crate::mcp::tools::ItemArgs;

    #[test]
    fn item_args_default_quantity() {
        let args: ItemArgs =
            serde_json::from_value(json!({ "action": "spawn", "item_id": "weed_brick" })).unwrap();
        assert_eq!(args.quantity, None);
    }
}
