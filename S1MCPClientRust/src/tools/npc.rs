use rmcp::model::{CallToolResult, JsonObject};
use serde_json::Value;

use crate::mcp::tools::NpcArgs;
use crate::server_state::ServerState;

use super::common::{call_and_format_result, text_result};

const TOOL_NAME: &str = "s1_npc";

pub async fn handle_npc(arguments: Option<JsonObject>, state: &ServerState) -> CallToolResult {
    let raw = Value::Object(arguments.unwrap_or_default());
    let args: NpcArgs = match serde_json::from_value(raw) {
        Ok(a) => a,
        Err(e) => return text_result(format!("Error: invalid arguments: {e}")),
    };

    match args.action.as_str() {
        "list" => {
            let params = args
                .filter
                .map(|f| serde_json::json!({ "filter": f }));
            call_and_format_result(state, "list_npcs", params, TOOL_NAME, "list").await
        }
        "get" => {
            let Some(npc_id) = args.npc_id else {
                return text_result("Error: npc_id is required");
            };
            call_and_format_result(
                state,
                "get_npc",
                Some(serde_json::json!({ "npc_id": npc_id })),
                TOOL_NAME,
                "get",
            )
            .await
        }
        "teleport" => {
            let Some(npc_id) = args.npc_id else {
                return text_result("Error: npc_id is required");
            };
            let Some(pos) = args.position else {
                return text_result("Error: position is required");
            };
            call_and_format_result(
                state,
                "teleport_npc",
                Some(serde_json::json!({
                    "npc_id": npc_id,
                    "position": { "x": pos.x, "y": pos.y, "z": pos.z }
                })),
                TOOL_NAME,
                "teleport",
            )
            .await
        }
        "set_health" => {
            let Some(npc_id) = args.npc_id else {
                return text_result("Error: npc_id is required");
            };
            let Some(health) = args.health else {
                return text_result("Error: health is required");
            };
            call_and_format_result(
                state,
                "set_npc_health",
                Some(serde_json::json!({ "npc_id": npc_id, "health": health })),
                TOOL_NAME,
                "set_health",
            )
            .await
        }
        other => text_result(format!("Error: Unknown action '{other}'")),
    }
}
