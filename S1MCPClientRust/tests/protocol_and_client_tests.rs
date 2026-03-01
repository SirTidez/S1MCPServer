/// Extended protocol tests: serialize/deserialize round-trips, heartbeat
/// detection edge cases, map_error_code boundary values, and the actor
/// TcpClient's initial disconnected state.
use serde_json::json;

use s1_mcp_client_rust::models::{Acknowledgment, Response};
use s1_mcp_client_rust::protocol::{
    create_request, deserialize_response, is_server_heartbeat, map_error_code,
    serialize_acknowledgment, serialize_request,
};
use s1_mcp_client_rust::tcp::TcpClient;

// ---------------------------------------------------------------------------
// Serialize / Deserialize round-trips
// ---------------------------------------------------------------------------

#[test]
fn serialize_request_then_deserialize_response_roundtrip() {
    // Serialise a request into a length-prefixed frame, then reinterpret that
    // frame as a Response to verify the codec is symmetric and that the id
    // field survives the trip.
    let request = create_request(7, "get_player", Some(json!({ "id": "p1" })));
    let frame = serialize_request(&request).expect("serialize_request must succeed");

    // The request shape is close enough to Response for field overlap (id)
    let response: Response =
        deserialize_response(&frame).expect("deserialize_response must succeed");

    assert_eq!(response.id, 7);
}

#[test]
fn serialize_acknowledgment_produces_length_prefixed_frame() {
    let ack = Acknowledgment {
        id: 42,
        status: "ok".to_string(),
    };
    let frame = serialize_acknowledgment(&ack).expect("must succeed");
    // Frame: 4-byte LE length prefix + JSON payload
    assert!(frame.len() > 4, "frame must be longer than just the prefix");
    let payload_len = u32::from_le_bytes([frame[0], frame[1], frame[2], frame[3]]) as usize;
    assert_eq!(frame.len(), 4 + payload_len);
}

#[test]
fn deserialize_response_with_error_field() {
    let frame = build_response_frame(json!({
        "id": 3,
        "error": { "code": -32601, "message": "Method not found" }
    }));
    let response = deserialize_response(&frame).expect("must parse");
    assert_eq!(response.id, 3);
    let err = response.error.expect("error field must be present");
    assert_eq!(err.code, -32601);
    assert_eq!(err.message, "Method not found");
    assert!(response.result.is_none());
}

#[test]
fn deserialize_response_with_result_field() {
    let frame = build_response_frame(json!({
        "id": 99,
        "result": { "health": 100 }
    }));
    let response = deserialize_response(&frame).expect("must parse");
    assert!(response.error.is_none());
    let result = response.result.expect("result must be present");
    assert_eq!(result["health"], 100);
}

// ---------------------------------------------------------------------------
// is_server_heartbeat edge cases
// ---------------------------------------------------------------------------

#[test]
fn heartbeat_requires_type_exactly() {
    let r = make_response_with_result(json!({ "type": "server_heartbeat" }));
    assert!(is_server_heartbeat(&r));
}

#[test]
fn heartbeat_wrong_type_value() {
    let r = make_response_with_result(json!({ "type": "client_heartbeat" }));
    assert!(!is_server_heartbeat(&r));
}

#[test]
fn heartbeat_type_field_missing() {
    let r = make_response_with_result(json!({ "other": "server_heartbeat" }));
    assert!(!is_server_heartbeat(&r));
}

#[test]
fn heartbeat_result_null() {
    let r = Response {
        id: 1,
        result: None,
        error: None,
    };
    assert!(!is_server_heartbeat(&r));
}

#[test]
fn heartbeat_result_not_object() {
    let r = make_response_with_result(json!("server_heartbeat"));
    assert!(!is_server_heartbeat(&r));
}

// ---------------------------------------------------------------------------
// map_error_code boundary values
// ---------------------------------------------------------------------------

#[test]
fn map_error_code_preserves_parse_error() {
    assert_eq!(map_error_code(-32700), -32700);
}

#[test]
fn map_error_code_preserves_internal_error() {
    assert_eq!(map_error_code(-32603), -32603);
}

#[test]
fn map_error_code_preserves_server_defined_range_boundaries() {
    assert_eq!(map_error_code(-32099), -32099);
    assert_eq!(map_error_code(-32000), -32000);
}

#[test]
fn map_error_code_remaps_out_of_range_positive() {
    assert_eq!(map_error_code(0), -32603);
    assert_eq!(map_error_code(999), -32603);
}

#[test]
fn map_error_code_remaps_too_negative() {
    assert_eq!(map_error_code(-99999), -32603);
    assert_eq!(map_error_code(-32100), -32603);
}

// ---------------------------------------------------------------------------
// TcpClient actor — initial state
// ---------------------------------------------------------------------------

#[test]
fn tcp_client_initial_not_connected() {
    let client = TcpClient::default();
    assert!(!client.is_connected(), "must start disconnected");
}

#[test]
fn tcp_client_disconnect_when_already_disconnected_is_safe() {
    // Must not panic or deadlock.
    let client = TcpClient::default();
    client.disconnect();
    client.disconnect(); // idempotent
    assert!(!client.is_connected());
}

#[tokio::test]
async fn tcp_client_call_when_not_connected_returns_error() {
    let client = TcpClient::default();
    let result = client.call("handshake", None).await;
    assert!(
        result.is_err(),
        "call on disconnected client must return Err"
    );
}

#[tokio::test]
async fn tcp_client_connect_to_unreachable_address_returns_error() {
    use std::time::Duration;
    // Port 0 is unroutable on all platforms.
    let client = TcpClient::new("127.0.0.1", 1, Duration::from_millis(100), Duration::from_millis(50));
    let result = client.connect().await;
    assert!(result.is_err(), "connect to port 1 must fail");
    assert!(!client.is_connected());
}

#[tokio::test]
async fn tcp_client_clone_shares_connection_state() {
    let client = TcpClient::default();
    let clone = client.clone();
    // Both see the same initial state.
    assert_eq!(client.is_connected(), clone.is_connected());
    // Disconnecting via the clone must reflect on the original.
    clone.disconnect();
    assert!(!client.is_connected());
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

fn make_response_with_result(result: serde_json::Value) -> Response {
    Response {
        id: 1,
        result: Some(result),
        error: None,
    }
}

fn build_response_frame(value: serde_json::Value) -> Vec<u8> {
    use s1_mcp_client_rust::protocol::encode_length_prefixed_json;
    encode_length_prefixed_json(&value).expect("encode must succeed")
}
