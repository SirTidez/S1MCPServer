use rmcp::model::{CallToolResult, Content};
use serde_json::Value;
use tracing::error;

use crate::server_state::ServerState;

const INTERNAL_ERROR_MESSAGE: &str = "An internal error occurred while processing your request.";

pub fn text_result(text: impl Into<String>) -> CallToolResult {
    CallToolResult::success(vec![Content::text(text.into())])
}

pub fn is_truthy(value: &Value) -> bool {
    match value {
        Value::Null => false,
        Value::Bool(flag) => *flag,
        Value::Number(number) => {
            if let Some(integer) = number.as_i64() {
                integer != 0
            } else if let Some(unsigned) = number.as_u64() {
                unsigned != 0
            } else if let Some(float) = number.as_f64() {
                float != 0.0
            } else {
                false
            }
        }
        Value::String(text) => !text.is_empty(),
        Value::Array(items) => !items.is_empty(),
        Value::Object(entries) => !entries.is_empty(),
    }
}

pub fn format_action_for_error(action: Option<&Value>) -> String {
    match action {
        None => "None".to_string(),
        Some(value) => python_like_text(value),
    }
}

pub fn python_like_text(value: &Value) -> String {
    python_like_text_with_context(value, false)
}

fn python_like_text_with_context(value: &Value, in_container: bool) -> String {
    match value {
        Value::Null => "None".to_string(),
        Value::Bool(true) => "True".to_string(),
        Value::Bool(false) => "False".to_string(),
        Value::Number(number) => number.to_string(),
        Value::String(text) => {
            if in_container {
                format!("'{}'", escape_python_single_quoted_string(text))
            } else {
                text.clone()
            }
        }
        Value::Array(items) => {
            let rendered_items = items
                .iter()
                .map(|item| python_like_text_with_context(item, true))
                .collect::<Vec<_>>()
                .join(", ");
            format!("[{}]", rendered_items)
        }
        Value::Object(entries) => {
            let rendered_entries = entries
                .iter()
                .map(|(key, item)| {
                    format!(
                        "'{}': {}",
                        escape_python_single_quoted_string(key),
                        python_like_text_with_context(item, true)
                    )
                })
                .collect::<Vec<_>>()
                .join(", ");
            format!("{{{}}}", rendered_entries)
        }
    }
}

fn escape_python_single_quoted_string(text: &str) -> String {
    text.replace('\\', "\\\\").replace('\'', "\\'")
}

pub async fn call_and_format_result(
    state: &ServerState,
    method: &str,
    params: Option<Value>,
    tool_name: &str,
    action_for_log: &str,
) -> CallToolResult {
    match state.tcp_client().async_call(method, params).await {
        Ok(response) => {
            if let Some(error_response) = response.error {
                return text_result(format!(
                    "Error: {} (code: {})",
                    error_response.message, error_response.code
                ));
            }

            let result_value = response.result.unwrap_or(Value::Null);
            match serde_json::to_string_pretty(&result_value) {
                Ok(text) => text_result(text),
                Err(error) => {
                    error!(
                        tool = tool_name,
                        action = action_for_log,
                        error = %error,
                        "Failed to serialize tool response"
                    );
                    text_result(INTERNAL_ERROR_MESSAGE)
                }
            }
        }
        Err(error) => {
            error!(
                tool = tool_name,
                action = action_for_log,
                error = %error,
                "Tool request failed"
            );
            text_result(INTERNAL_ERROR_MESSAGE)
        }
    }
}
