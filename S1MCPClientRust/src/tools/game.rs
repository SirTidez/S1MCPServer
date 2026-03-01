use rmcp::model::{CallToolResult, JsonObject};
use serde_json::Value;

use crate::docs::context7::search_s1api_docs;
use crate::game::lifecycle::{close_game, get_game_process_info, launch_game, GameVersion, LaunchGameOptions};
use crate::mcp::tools::GameArgs;
use crate::server_state::ServerState;

use super::common::{text_result};

const TOOL_NAME: &str = "s1_game";
const INTERNAL_ERROR_MESSAGE: &str = "An internal error occurred while processing your request.";

pub async fn handle_game(arguments: Option<JsonObject>, state: &ServerState) -> CallToolResult {
    let raw = Value::Object(arguments.unwrap_or_default());
    let args: GameArgs = match serde_json::from_value(raw) {
        Ok(a) => a,
        Err(e) => return text_result(format!("Error: invalid arguments: {e}")),
    };

    match args.action.as_str() {
        "launch" => {
            let version_input = args.version.as_deref().unwrap_or("");
            let version = match GameVersion::parse(version_input) {
                Ok(v) => v,
                Err(msg) => return text_result(msg),
            };

            let options = LaunchGameOptions {
                version,
                enable_debugger: args.enable_debugger.unwrap_or(false),
                wait_for_connection: args.wait_for_connection.unwrap_or(true),
            };

            match launch_game(&options, &state.tcp_client(), state.settings()).await {
                Ok(text) => text_result(text),
                Err(msg) => text_result(msg),
            }
        }
        "close" => match close_game(&state.tcp_client(), state.settings()).await {
            Ok(text) => text_result(text),
            Err(msg) => text_result(msg),
        },
        "process_info" => {
            let process_info = get_game_process_info(state.settings()).await;
            match serde_json::to_string_pretty(&process_info) {
                Ok(text) => text_result(text),
                Err(e) => {
                    tracing::error!(
                        tool = TOOL_NAME,
                        action = "process_info",
                        error = %e,
                        "Failed to serialize process info"
                    );
                    text_result(INTERNAL_ERROR_MESSAGE)
                }
            }
        }
        "search_docs" => {
            let Some(topic) = args.topic else {
                return text_result("Error: topic is required for search_docs");
            };
            let tokens = args.tokens.unwrap_or(5000);
            if tokens < 1 {
                return text_result("Error: tokens must be a positive integer");
            }
            match search_s1api_docs(&topic, tokens).await {
                Ok(content) => text_result(content),
                Err(msg) => text_result(msg),
            }
        }
        other => text_result(format!("Error: Unknown action '{other}'")),
    }
}

#[cfg(test)]
mod tests {
    use serde_json::json;

    use crate::mcp::tools::GameArgs;

    #[test]
    fn game_args_defaults() {
        let args: GameArgs =
            serde_json::from_value(json!({ "action": "search_docs", "topic": "Player" })).unwrap();
        assert_eq!(args.tokens, None);
        assert_eq!(args.enable_debugger, None);
    }

    #[test]
    fn game_args_tokens_parsed() {
        let args: GameArgs =
            serde_json::from_value(json!({ "action": "search_docs", "topic": "NPC", "tokens": 1000 }))
                .unwrap();
        assert_eq!(args.tokens, Some(1000));
    }
}
