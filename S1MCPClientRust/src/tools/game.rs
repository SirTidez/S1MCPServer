use rmcp::model::{CallToolResult, JsonObject};
use serde_json::Value;
use tracing::error;

use crate::docs::context7::search_s1api_docs;
use crate::game::lifecycle::{close_game, get_game_process_info, launch_game, GameVersion, LaunchGameOptions};
use crate::server_state::ServerState;

use super::common::{format_action_for_error, is_truthy, text_result};

const TOOL_NAME: &str = "s1_game";
const INTERNAL_ERROR_MESSAGE: &str = "An internal error occurred while processing your request.";

pub async fn handle_game(arguments: Option<JsonObject>, state: &ServerState) -> CallToolResult {
    let args = arguments.unwrap_or_default();
    let action_value = args.get("action");
    let action = action_value.and_then(Value::as_str);
    let action_for_log = format_action_for_error(action_value);

    match action {
        Some("launch") => {
            let version_input = args
                .get("version")
                .and_then(Value::as_str)
                .unwrap_or_default();

            let version = match GameVersion::parse(version_input) {
                Ok(version) => version,
                Err(message) => return text_result(message),
            };

            let enable_debugger = args
                .get("enable_debugger")
                .map(is_truthy)
                .unwrap_or(false);
            let wait_for_connection = args
                .get("wait_for_connection")
                .map(is_truthy)
                .unwrap_or(true);

            let options = LaunchGameOptions {
                version,
                enable_debugger,
                wait_for_connection,
            };

            match launch_game(&options, &state.tcp_client(), state.settings()).await {
                Ok(text) => text_result(text),
                Err(message) => text_result(message),
            }
        }
        Some("close") => {
            match close_game(&state.tcp_client(), state.settings()).await {
                Ok(text) => text_result(text),
                Err(message) => text_result(message),
            }
        }
        Some("process_info") => {
            let process_info = get_game_process_info(state.settings()).await;
            match serde_json::to_string_pretty(&process_info) {
                Ok(text) => text_result(text),
                Err(serialization_error) => {
                    error!(
                        tool = TOOL_NAME,
                        action = action_for_log,
                        error = %serialization_error,
                        "Failed to serialize process info"
                    );
                    text_result(INTERNAL_ERROR_MESSAGE)
                }
            }
        }
        Some("search_docs") => {
            let Some(topic) = args.get("topic").and_then(Value::as_str) else {
                return text_result("Error: topic is required for search_docs");
            };

            let tokens = match parse_tokens(args.get("tokens")) {
                Ok(tokens) => tokens,
                Err(message) => return text_result(message),
            };

            match search_s1api_docs(topic, tokens).await {
                Ok(content) => text_result(content),
                Err(message) => text_result(message),
            }
        }
        _ => text_result(format!("Error: Unknown action '{}'", action_for_log)),
    }
}

fn parse_tokens(value: Option<&Value>) -> Result<u32, &'static str> {
    let Some(raw) = value else {
        return Ok(5000);
    };

    match raw {
        Value::Bool(_) => Err("Error: tokens must be a valid integer"),
        Value::Number(number) => {
            if let Some(unsigned) = number.as_u64() {
                if unsigned < 1 {
                    return Err("Error: tokens must be a positive integer");
                }
                return u32::try_from(unsigned).map_err(|_| "Error: tokens must be a valid integer");
            }
            if let Some(signed) = number.as_i64() {
                if signed < 1 {
                    return Err("Error: tokens must be a positive integer");
                }

                return u32::try_from(signed).map_err(|_| "Error: tokens must be a valid integer");
            }
            Err("Error: tokens must be a valid integer")
        }
        Value::String(text) => {
            let parsed = text
                .parse::<i64>()
                .map_err(|_| "Error: tokens must be a valid integer")?;
            if parsed < 1 {
                return Err("Error: tokens must be a positive integer");
            }

            u32::try_from(parsed).map_err(|_| "Error: tokens must be a valid integer")
        }
        _ => Err("Error: tokens must be a valid integer"),
    }
}

#[cfg(test)]
mod tests {
    use super::parse_tokens;
    use serde_json::json;

    #[test]
    fn parse_tokens_matches_python_validation_shape() {
        assert_eq!(parse_tokens(None).expect("default tokens"), 5000);
        assert_eq!(parse_tokens(Some(&json!(1))).expect("positive integer"), 1);
        assert_eq!(parse_tokens(Some(&json!("42"))).expect("numeric string"), 42);

        assert!(parse_tokens(Some(&json!(0))).is_err());
        assert!(parse_tokens(Some(&json!(-1))).is_err());
        assert!(parse_tokens(Some(&json!(true))).is_err());
        assert!(parse_tokens(Some(&json!(1.5))).is_err());
        assert!(parse_tokens(Some(&json!("abc"))).is_err());
    }
}
