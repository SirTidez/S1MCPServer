use s1_mcp_client_rust::config::Settings;
use s1_mcp_client_rust::models::Response;
use s1_mcp_client_rust::protocol::{create_request, is_server_heartbeat, map_error_code, ProtocolError};
use s1_mcp_client_rust::server_state::ServerState;
use s1_mcp_client_rust::tcp::error::TcpClientError;

#[test]
fn map_error_code_preserves_known_jsonrpc_and_server_ranges() {
    assert_eq!(map_error_code(-32700), -32700);
    assert_eq!(map_error_code(-32602), -32602);
    assert_eq!(map_error_code(-32042), -32042);

    assert_eq!(map_error_code(123), -32603);
    assert_eq!(map_error_code(-320100), -32603);
}

#[test]
fn create_request_defaults_params_to_empty_object() {
    let request = create_request(1, "handshake", None);
    assert_eq!(request.params, serde_json::json!({}));
}

#[test]
fn heartbeat_detection_matches_expected_payload_shape() {
    let heartbeat = Response {
        id: 99,
        result: Some(serde_json::json!({ "type": "server_heartbeat" })),
        error: None,
    };
    let normal = Response {
        id: 100,
        result: Some(serde_json::json!({ "type": "other" })),
        error: None,
    };

    assert!(is_server_heartbeat(&heartbeat));
    assert!(!is_server_heartbeat(&normal));
}

#[test]
fn tcp_retryable_classification_matches_policy() {
    let io_error = TcpClientError::Io(std::io::Error::new(std::io::ErrorKind::ConnectionReset, "boom"));
    let protocol_error = TcpClientError::Protocol(ProtocolError::MessageTooShort { actual: 0 });

    assert!(TcpClientError::Connection("offline".to_string()).is_retryable_connection());
    assert!(TcpClientError::AddressResolution("bad host".to_string()).is_retryable_connection());
    assert!(TcpClientError::NotConnected.is_retryable_connection());
    assert!(io_error.is_retryable_connection());
    assert!(!protocol_error.is_retryable_connection());
}

#[tokio::test]
async fn connection_gate_allows_s1_game_without_handshake() {
    let state = ServerState::from_settings(&Settings::default());

    assert!(state.can_call_tool("s1_game").await.is_ok());
}

#[tokio::test]
async fn connection_gate_blocks_non_game_tool_when_disconnected() {
    let state = ServerState::from_settings(&Settings {
        host: "127.0.0.1".to_string(),
        port: 0,
        log_level: "info".to_string(),
        connection_timeout: 0,
        reconnect_delay: 0,
        game_il2cpp_path: String::new(),
        game_mono_path: String::new(),
        game_executable: String::new(),
        game_startup_timeout: 1,
        game_connection_poll_interval: 1,
    });

    let error = state
        .can_call_tool("s1_player")
        .await
        .expect_err("non-game tool should be gated");

    assert!(error.contains("Error: Game is not connected."));
}
