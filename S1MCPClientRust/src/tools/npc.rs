use rmcp::model::{CallToolResult, JsonObject};
use serde_json::{json, Value};

use crate::server_state::ServerState;

use super::common::{call_and_format_result, format_action_for_error, is_truthy, text_result};

const TOOL_NAME: &str = "s1_npc";

pub async fn handle_npc(arguments: Option<JsonObject>, state: &ServerState) -> CallToolResult {
    let args = arguments.unwrap_or_default();
    let action_value = args.get("action");
    let action = action_value.and_then(Value::as_str);
    let action_for_log = format_action_for_error(action_value);

    match action {
        Some("list") => {
            let params = args
                .get("filter")
                .filter(|value| is_truthy(value))
                .map(|filter| json!({ "filter": filter }));

            call_and_format_result(state, "list_npcs", params, TOOL_NAME, &action_for_log).await
        }
        Some("get") => {
            let Some(npc_id) = args.get("npc_id").filter(|value| is_truthy(value)) else {
                return text_result("Error: npc_id is required");
            };

            call_and_format_result(
                state,
                "get_npc",
                Some(json!({ "npc_id": npc_id })),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        Some("teleport") => {
            let Some(npc_id) = args.get("npc_id").filter(|value| is_truthy(value)) else {
                return text_result("Error: npc_id is required");
            };

            let Some(position) = args.get("position").filter(|value| is_truthy(value)) else {
                return text_result("Error: position is required");
            };

            call_and_format_result(
                state,
                "teleport_npc",
                Some(json!({ "npc_id": npc_id, "position": position })),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        Some("set_health") => {
            let Some(npc_id) = args.get("npc_id").filter(|value| is_truthy(value)) else {
                return text_result("Error: npc_id is required");
            };

            let Some(health) = args.get("health") else {
                return text_result("Error: health is required");
            };

            if health.is_null() {
                return text_result("Error: health is required");
            }

            call_and_format_result(
                state,
                "set_npc_health",
                Some(json!({ "npc_id": npc_id, "health": health })),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        _ => text_result(format!("Error: Unknown action '{}'", action_for_log)),
    }
}
