use std::io::Cursor;

use s1_mcp_client_rust::models::Request;
use s1_mcp_client_rust::protocol::{
    decode_length_prefixed_json, encode_length_prefixed_json, read_frame, write_frame,
    ProtocolError, MAX_MESSAGE_SIZE_BYTES,
};

#[test]
fn encode_decode_length_prefixed_round_trip() {
    let request = Request {
        id: 42,
        method: "handshake".to_string(),
        params: serde_json::json!({ "client": "rust-test" }),
    };

    let frame = encode_length_prefixed_json(&request).expect("encode should succeed");
    let decoded: Request = decode_length_prefixed_json(&frame).expect("decode should succeed");

    assert_eq!(decoded.id, request.id);
    assert_eq!(decoded.method, request.method);
    assert_eq!(decoded.params, request.params);
}

#[test]
fn decode_rejects_short_messages() {
    let error = decode_length_prefixed_json::<Request>(&[1_u8, 2, 3]).expect_err("must fail");

    match error {
        ProtocolError::MessageTooShort { actual } => assert_eq!(actual, 3),
        other => panic!("unexpected error variant: {other}"),
    }
}

#[test]
fn decode_rejects_incomplete_payload() {
    let mut frame = Vec::new();
    frame.extend_from_slice(&8_u32.to_le_bytes());
    frame.extend_from_slice(b"{}");

    let error = decode_length_prefixed_json::<Request>(&frame).expect_err("must fail");
    match error {
        ProtocolError::MessageIncomplete { expected, actual } => {
            assert_eq!(expected, 12);
            assert_eq!(actual, 6);
        }
        other => panic!("unexpected error variant: {other}"),
    }
}

#[test]
fn decode_rejects_payload_over_size_limit() {
    let mut frame = Vec::new();
    frame.extend_from_slice(&((MAX_MESSAGE_SIZE_BYTES as u32) + 1).to_le_bytes());

    let error = decode_length_prefixed_json::<Request>(&frame).expect_err("must fail");
    match error {
        ProtocolError::MessageTooLarge { length, max } => {
            assert_eq!(length, MAX_MESSAGE_SIZE_BYTES + 1);
            assert_eq!(max, MAX_MESSAGE_SIZE_BYTES);
        }
        other => panic!("unexpected error variant: {other}"),
    }
}

#[test]
fn read_and_write_frame_round_trip() {
    let request = Request {
        id: 7,
        method: "get_player".to_string(),
        params: serde_json::json!({}),
    };
    let frame = encode_length_prefixed_json(&request).expect("encode should succeed");

    let mut sink = Cursor::new(Vec::<u8>::new());
    write_frame(&mut sink, &frame).expect("write should succeed");

    sink.set_position(0);
    let round_trip = read_frame(&mut sink).expect("read should succeed");
    assert_eq!(round_trip, frame);
}
