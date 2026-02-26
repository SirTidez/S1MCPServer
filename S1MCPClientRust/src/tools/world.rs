use rmcp::model::{CallToolResult, JsonObject};
use serde_json::{json, Map, Value};
use tracing::error;

use crate::server_state::ServerState;

use super::common::{
    call_and_format_result, format_action_for_error, is_truthy, python_like_text, text_result,
};

const TOOL_NAME: &str = "s1_world";
const INTERNAL_ERROR_MESSAGE: &str = "An internal error occurred while processing your request.";

pub async fn handle_world(arguments: Option<JsonObject>, state: &ServerState) -> CallToolResult {
    let args = arguments.unwrap_or_default();
    let action_value = args.get("action");
    let action = action_value.and_then(Value::as_str);
    let action_for_log = format_action_for_error(action_value);

    match action {
        Some("get_state") => {
            call_and_format_result(
                state,
                "get_game_state",
                Some(json!({})),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        Some("list_locations") => {
            call_and_format_result(
                state,
                "list_properties",
                Some(json!({})),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        Some("get_location") => {
            let property_id = args.get("property_id").filter(|value| is_truthy(value));
            let property_name = args.get("property_name").filter(|value| is_truthy(value));

            if property_id.is_none() && property_name.is_none() {
                return text_result("Error: property_id or property_name is required");
            }

            let mut params = Map::new();
            if let Some(value) = property_id {
                params.insert("property_id".to_string(), value.clone());
            }
            if let Some(value) = property_name {
                params.insert("property_name".to_string(), value.clone());
            }

            call_and_format_result(
                state,
                "get_property",
                Some(Value::Object(params)),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        Some("list_vehicles") => {
            call_and_format_result(
                state,
                "list_vehicles",
                Some(json!({})),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        Some("get_vehicle") => {
            let Some(vehicle_id) = args.get("vehicle_id").filter(|value| is_truthy(value)) else {
                return text_result("Error: vehicle_id is required");
            };

            call_and_format_result(
                state,
                "get_vehicle",
                Some(json!({ "vehicle_id": vehicle_id })),
                TOOL_NAME,
                &action_for_log,
            )
            .await
        }
        Some("capture_logs") => {
            let mut params = Map::new();
            for key in [
                "last_n_lines",
                "first_n_lines",
                "keyword",
                "from_timestamp",
                "to_timestamp",
                "include_pattern",
                "exclude_pattern",
            ] {
                if let Some(value) = args.get(key) {
                    params.insert(key.to_string(), value.clone());
                }
            }

            let response = match state
                .tcp_client()
                .async_call("capture_logs", Some(Value::Object(params)))
                .await
            {
                Ok(response) => response,
                Err(request_error) => {
                    error!(
                        tool = TOOL_NAME,
                        action = action_for_log,
                        error = %request_error,
                        "Tool request failed"
                    );
                    return text_result(INTERNAL_ERROR_MESSAGE);
                }
            };

            if let Some(error_response) = response.error {
                return text_result(format!(
                    "Error: {} (code: {})",
                    error_response.message, error_response.code
                ));
            }

            format_logs(response.result.unwrap_or(Value::Null))
        }
        Some("list_saves") => {
            let response = match state
                .tcp_client()
                .async_call("list_saves", Some(json!({})))
                .await
            {
                Ok(response) => response,
                Err(request_error) => {
                    error!(
                        tool = TOOL_NAME,
                        action = action_for_log,
                        error = %request_error,
                        "Tool request failed"
                    );
                    return text_result(INTERNAL_ERROR_MESSAGE);
                }
            };

            if let Some(error_response) = response.error {
                return text_result(format!(
                    "Error: {} (code: {})",
                    error_response.message, error_response.code
                ));
            }

            let result_value = response.result.unwrap_or(Value::Null);
            let result_text = match serde_json::to_string_pretty(&result_value) {
                Ok(text) => text,
                Err(serialization_error) => {
                    error!(
                        tool = TOOL_NAME,
                        action = action_for_log,
                        error = %serialization_error,
                        "Failed to serialize tool response"
                    );
                    return text_result(INTERNAL_ERROR_MESSAGE);
                }
            };

            if let Some(result_object) = result_value.as_object() {
                if result_object.contains_key("saves") {
                    let count = result_object
                        .get("count")
                        .map(python_like_text)
                        .unwrap_or_else(|| "0".to_string());
                    return text_result(format!("Found {} save(s):\n\n{}", count, result_text));
                }
            }

            text_result(result_text)
        }
        Some("load_save") => {
            let Some(slot_index) = args.get("slot_index").filter(|value| !value.is_null()) else {
                return text_result("Error: slot_index is required");
            };

            let response = match state
                .tcp_client()
                .async_call("load_save", Some(json!({ "slot_index": slot_index })))
                .await
            {
                Ok(response) => response,
                Err(request_error) => {
                    error!(
                        tool = TOOL_NAME,
                        action = action_for_log,
                        error = %request_error,
                        "Tool request failed"
                    );
                    return text_result(INTERNAL_ERROR_MESSAGE);
                }
            };

            if let Some(error_response) = response.error {
                return text_result(format!(
                    "Error: {} (code: {})",
                    error_response.message, error_response.code
                ));
            }

            let result_value = response.result.unwrap_or(Value::Null);
            if let Some(result_object) = result_value.as_object() {
                let message = result_object
                    .get("message")
                    .and_then(Value::as_str)
                    .unwrap_or_default();
                let note = if result_object
                    .get("returned_to_menu")
                    .is_some_and(is_truthy)
                {
                    "\n  Note: Returned to menu first."
                } else {
                    ""
                };
                return text_result(format!("✓ {}{}", message, note));
            }

            match serde_json::to_string_pretty(&result_value) {
                Ok(text) => text_result(text),
                Err(serialization_error) => {
                    error!(
                        tool = TOOL_NAME,
                        action = action_for_log,
                        error = %serialization_error,
                        "Failed to serialize tool response"
                    );
                    text_result(INTERNAL_ERROR_MESSAGE)
                }
            }
        }
        _ => text_result(format!("Error: Unknown action '{}'", action_for_log)),
    }
}

fn format_logs(result: Value) -> CallToolResult {
    let Some(result_object) = result.as_object() else {
        return text_result(python_like_text(&result));
    };

    let lines = result_object
        .get("lines")
        .and_then(Value::as_array)
        .cloned()
        .unwrap_or_default();
    let total_lines = result_object
        .get("total_lines_in_file")
        .and_then(Value::as_u64)
        .unwrap_or(0);
    let filtered_count = result_object
        .get("filtered_count")
        .and_then(Value::as_u64)
        .unwrap_or(0);
    let filters = result_object
        .get("filters_applied")
        .and_then(Value::as_array)
        .map(|items| {
            items
                .iter()
                .map(python_like_text)
                .collect::<Vec<String>>()
        })
        .unwrap_or_default();
    let warning = result_object.get("warning").and_then(Value::as_str);

    let mut output = Vec::new();
    if let Some(warning_text) = warning {
        output.push(format!("Warning: {}\n", warning_text));
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
        if let Some(line_object) = line.as_object() {
            let line_number = line_object
                .get("line_number")
                .map(python_like_text)
                .unwrap_or_else(|| "?".to_string());
            let timestamp = line_object
                .get("timestamp")
                .and_then(Value::as_str)
                .unwrap_or_default();
            let content = line_object
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
