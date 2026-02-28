use rmcp::model::{CallToolResult, JsonObject};
use serde_json::{json, Value};
use tracing::error;

use crate::mcp::tools::WorldArgs;
use crate::server_state::ServerState;

use super::common::{call_and_format_result, python_like_text, text_result};

const TOOL_NAME: &str = "s1_world";
const INTERNAL_ERROR_MESSAGE: &str = "An internal error occurred while processing your request.";

pub async fn handle_world(arguments: Option<JsonObject>, state: &ServerState) -> CallToolResult {
    let raw = Value::Object(arguments.unwrap_or_default());
    let args: WorldArgs = match serde_json::from_value(raw) {
        Ok(a) => a,
        Err(e) => return text_result(format!("Error: invalid arguments: {e}")),
    };

    match args.action.as_str() {
        "get_state" => {
            call_and_format_result(state, "get_game_state", Some(json!({})), TOOL_NAME, "get_state").await
        }
        "list_locations" => {
            call_and_format_result(
                state,
                "list_properties",
                Some(json!({})),
                TOOL_NAME,
                "list_locations",
            )
            .await
        }
        "get_location" => {
            if args.property_id.is_none() && args.property_name.is_none() {
                return text_result("Error: property_id or property_name is required");
            }
            let mut params = serde_json::Map::new();
            if let Some(id) = args.property_id {
                params.insert("property_id".to_string(), json!(id));
            }
            if let Some(name) = args.property_name {
                params.insert("property_name".to_string(), json!(name));
            }
            call_and_format_result(
                state,
                "get_property",
                Some(Value::Object(params)),
                TOOL_NAME,
                "get_location",
            )
            .await
        }
        "list_vehicles" => {
            call_and_format_result(
                state,
                "list_vehicles",
                Some(json!({})),
                TOOL_NAME,
                "list_vehicles",
            )
            .await
        }
        "get_vehicle" => {
            let Some(vehicle_id) = args.vehicle_id else {
                return text_result("Error: vehicle_id is required");
            };
            call_and_format_result(
                state,
                "get_vehicle",
                Some(json!({ "vehicle_id": vehicle_id })),
                TOOL_NAME,
                "get_vehicle",
            )
            .await
        }
        "capture_logs" => {
            let mut params = serde_json::Map::new();
            if let Some(v) = args.last_n_lines {
                params.insert("last_n_lines".to_string(), json!(v));
            }
            if let Some(v) = args.first_n_lines {
                params.insert("first_n_lines".to_string(), json!(v));
            }
            if let Some(v) = args.keyword {
                params.insert("keyword".to_string(), json!(v));
            }
            if let Some(v) = args.from_timestamp {
                params.insert("from_timestamp".to_string(), json!(v));
            }
            if let Some(v) = args.to_timestamp {
                params.insert("to_timestamp".to_string(), json!(v));
            }
            if let Some(v) = args.include_pattern {
                params.insert("include_pattern".to_string(), json!(v));
            }
            if let Some(v) = args.exclude_pattern {
                params.insert("exclude_pattern".to_string(), json!(v));
            }

            let response =
                match state.tcp_client().async_call("capture_logs", Some(Value::Object(params))).await {
                    Ok(r) => r,
                    Err(e) => {
                        error!(tool = TOOL_NAME, action = "capture_logs", error = %e, "Tool request failed");
                        return text_result(INTERNAL_ERROR_MESSAGE);
                    }
                };

            if let Some(err) = response.error {
                return text_result(format!("Error: {} (code: {})", err.message, err.code));
            }
            format_logs(response.result.unwrap_or(Value::Null))
        }
        "list_saves" => {
            let response =
                match state.tcp_client().async_call("list_saves", Some(json!({}))).await {
                    Ok(r) => r,
                    Err(e) => {
                        error!(tool = TOOL_NAME, action = "list_saves", error = %e, "Tool request failed");
                        return text_result(INTERNAL_ERROR_MESSAGE);
                    }
                };

            if let Some(err) = response.error {
                return text_result(format!("Error: {} (code: {})", err.message, err.code));
            }

            let result_value = response.result.unwrap_or(Value::Null);
            let result_text = match serde_json::to_string_pretty(&result_value) {
                Ok(t) => t,
                Err(e) => {
                    error!(tool = TOOL_NAME, action = "list_saves", error = %e, "Failed to serialize tool response");
                    return text_result(INTERNAL_ERROR_MESSAGE);
                }
            };

            if let Some(obj) = result_value.as_object() {
                if obj.contains_key("saves") {
                    let count = obj
                        .get("count")
                        .map(python_like_text)
                        .unwrap_or_else(|| "0".to_string());
                    return text_result(format!("Found {} save(s):\n\n{}", count, result_text));
                }
            }

            text_result(result_text)
        }
        "load_save" => {
            let Some(slot_index) = args.slot_index else {
                return text_result("Error: slot_index is required");
            };

            let response = match state
                .tcp_client()
                .async_call("load_save", Some(json!({ "slot_index": slot_index })))
                .await
            {
                Ok(r) => r,
                Err(e) => {
                    error!(tool = TOOL_NAME, action = "load_save", error = %e, "Tool request failed");
                    return text_result(INTERNAL_ERROR_MESSAGE);
                }
            };

            if let Some(err) = response.error {
                return text_result(format!("Error: {} (code: {})", err.message, err.code));
            }

            let result_value = response.result.unwrap_or(Value::Null);
            if let Some(obj) = result_value.as_object() {
                let message = obj
                    .get("message")
                    .and_then(Value::as_str)
                    .unwrap_or_default();
                let note = if obj
                    .get("returned_to_menu")
                    .and_then(Value::as_bool)
                    .unwrap_or(false)
                {
                    "\n  Note: Returned to menu first."
                } else {
                    ""
                };
                return text_result(format!("✓ {}{}", message, note));
            }

            match serde_json::to_string_pretty(&result_value) {
                Ok(t) => text_result(t),
                Err(e) => {
                    error!(tool = TOOL_NAME, action = "load_save", error = %e, "Failed to serialize tool response");
                    text_result(INTERNAL_ERROR_MESSAGE)
                }
            }
        }
        other => text_result(format!("Error: Unknown action '{other}'")),
    }
}

fn format_logs(result: Value) -> CallToolResult {
    let Some(obj) = result.as_object() else {
        return text_result(python_like_text(&result));
    };

    let lines = obj
        .get("lines")
        .and_then(Value::as_array)
        .cloned()
        .unwrap_or_default();
    let total_lines = obj
        .get("total_lines_in_file")
        .and_then(Value::as_u64)
        .unwrap_or(0);
    let filtered_count = obj
        .get("filtered_count")
        .and_then(Value::as_u64)
        .unwrap_or(0);
    let filters = obj
        .get("filters_applied")
        .and_then(Value::as_array)
        .map(|items| items.iter().map(python_like_text).collect::<Vec<String>>())
        .unwrap_or_default();
    let warning = obj.get("warning").and_then(Value::as_str);

    let mut output = Vec::new();
    if let Some(w) = warning {
        output.push(format!("Warning: {}\n", w));
    }

    output.push(format!(
        "Log Summary: {} lines returned ({} after filters, {} total)",
        lines.len(),
        filtered_count,
        total_lines
    ));

    if !filters.is_empty() {
        output.push(format!("Filters: {}", filters.join(", ")));
    }

    if lines.is_empty() {
        output.push("(No lines matched)".to_string());
        return text_result(output.join("\n"));
    }

    output.push("\n--- Log Lines ---".to_string());

    for line in lines {
        if let Some(line_obj) = line.as_object() {
            let line_number = line_obj
                .get("line_number")
                .map(python_like_text)
                .unwrap_or_else(|| "?".to_string());
            let timestamp = line_obj
                .get("timestamp")
                .and_then(Value::as_str)
                .unwrap_or_default();
            let content = line_obj
                .get("content")
                .map(python_like_text)
                .unwrap_or_default();

            if timestamp.is_empty() {
                output.push(format!("[{}] {}", line_number, content));
            } else {
                output.push(format!("[{}] [{}] {}", line_number, timestamp, content));
            }
        } else {
            output.push(format!("[?] {}", python_like_text(&line)));
        }
    }

    text_result(output.join("\n"))
}
