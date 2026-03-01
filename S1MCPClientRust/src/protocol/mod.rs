mod codec;

use thiserror::Error;

use crate::models::{Acknowledgment, Request, Response};

pub use codec::{decode_length_prefixed_json, encode_length_prefixed_json, read_frame, write_frame};

pub const MAX_MESSAGE_SIZE_BYTES: usize = 10 * 1024 * 1024;

#[derive(Debug, Error)]
pub enum ProtocolError {
    #[error("message too short: missing length prefix (got {actual} bytes)")]
    MessageTooShort { actual: usize },
    #[error("message incomplete: expected {expected} bytes, got {actual}")]
    MessageIncomplete { expected: usize, actual: usize },
    #[error("invalid message length: {length} (max {max})")]
    MessageTooLarge { length: usize, max: usize },
    #[error("invalid utf-8 payload: {0}")]
    InvalidUtf8(#[from] std::string::FromUtf8Error),
    #[error("json error: {0}")]
    Json(#[from] serde_json::Error),
    #[error("i/o error: {0}")]
    Io(#[from] std::io::Error),
}

pub fn serialize_request(request: &Request) -> Result<Vec<u8>, ProtocolError> {
    encode_length_prefixed_json(request)
}

pub fn serialize_acknowledgment(ack: &Acknowledgment) -> Result<Vec<u8>, ProtocolError> {
    encode_length_prefixed_json(ack)
}

pub fn deserialize_response(data: &[u8]) -> Result<Response, ProtocolError> {
    decode_length_prefixed_json(data)
}

pub fn create_request(id: u64, method: impl Into<String>, params: Option<serde_json::Value>) -> Request {
    Request {
        id,
        method: method.into(),
        params: params.unwrap_or_else(|| serde_json::json!({})),
    }
}

pub fn map_error_code(mod_error_code: i32) -> i32 {
    match mod_error_code {
        -32700 | -32600 | -32601 | -32602 | -32603 => mod_error_code,
        -32099..=-32000 => mod_error_code,
        _ => -32603,
    }
}

pub fn is_server_heartbeat(response: &Response) -> bool {
    response
        .result
        .as_ref()
        .and_then(|value| value.as_object())
        .and_then(|object| object.get("type"))
        .and_then(|value| value.as_str())
        .is_some_and(|value| value == "server_heartbeat")
}
