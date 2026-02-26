use rmcp::model::{CallToolResult, JsonObject};
use serde_json::{json, Value};

use crate::server_state::ServerState;

use super::common::{call_and_format_result, format_action_for_error, is_truthy, text_result};

const TOOL_NAME: &str = "s1_player";

pub async fn handle_player(arguments: Option<JsonObject>, state: &ServerState) -> CallToolResult {
    let args = arguments.unwrap_or_default();
    let action_value = args.get("action");
    let action = action_value.and_then(Value::as_str);
    let action_for_log = format_action_for_error(action_value);

    match action {
        Some("get") => {
            call_and_format_result(state, "get_player", Some(json!({})), TOOL_NAME, &action_for_log).await
        }
        Some("get_inventory") => {
            call_and_format_result(
                state,
                "get_player_inventory",
                Some(json!({})),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        Some("teleport") => {
            let Some(position) = args.get("position").filter(|value| is_truthy(value)) else {
                return text_result("Error: position is required for teleport");
            };

            call_and_format_result(
                state,
                "teleport_player",
                Some(json!({ "position": position })),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        Some("add_item") => {
            let Some(item_id) = args.get("item_id").filter(|value| is_truthy(value)) else {
                return text_result("Error: item_id is required for add_item");
            };

            let quantity = match parse_quantity(args.get("quantity")) {
                Ok(quantity) => quantity,
                Err(message) => return text_result(message),
            };

            call_and_format_result(
                state,
                "add_item_to_player",
                Some(json!({ "item_id": item_id, "quantity": quantity })),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        _ => text_result(format!("Error: Unknown action '{}'", action_for_log)),
    }
}

fn parse_quantity(value: Option<&Value>) -> Result<u64, &'static str> {
    let Some(raw) = value else {
        return Ok(1);
    };

    match raw {
        Value::Bool(_) => Err("Error: quantity must be a positive integer"),
        Value::Number(number) => {
            if let Some(unsigned) = number.as_u64() {
                if unsigned >= 1 {
                    return Ok(unsigned);
                }
            }

            if let Some(signed) = number.as_i64() {
                if signed >= 1 {
                    return Ok(signed as u64);
                }
            }

            Err("Error: quantity must be a positive integer")
        }
        _ => Err("Error: quantity must be a positive integer"),
    }
}

#[cfg(test)]
mod tests {
    use super::parse_quantity;
    use serde_json::json;

    #[test]
    fn parse_quantity_matches_python_integer_rules() {
        assert_eq!(parse_quantity(None).expect("default quantity"), 1);
        assert_eq!(parse_quantity(Some(&json!(1))).expect("one is valid"), 1);
        assert!(parse_quantity(Some(&json!(true))).is_err());
        assert!(parse_quantity(Some(&json!(0))).is_err());
        assert!(parse_quantity(Some(&json!(1.0))).is_err());
        assert!(parse_quantity(Some(&json!("1"))).is_err());
    }
}
