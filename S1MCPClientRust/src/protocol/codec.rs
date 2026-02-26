use std::io::{Read, Write};

use serde::de::DeserializeOwned;
use serde::Serialize;

use crate::protocol::{ProtocolError, MAX_MESSAGE_SIZE_BYTES};

pub fn encode_length_prefixed_json<T>(value: &T) -> Result<Vec<u8>, ProtocolError>
where
    T: Serialize,
{
    let json_bytes = serde_json::to_vec(value)?;
    let payload_len = json_bytes.len();

    if payload_len > MAX_MESSAGE_SIZE_BYTES {
        return Err(ProtocolError::MessageTooLarge {
            length: payload_len,
            max: MAX_MESSAGE_SIZE_BYTES,
        });
    }

    let mut message = Vec::with_capacity(4 + payload_len);
    message.extend_from_slice(&(payload_len as u32).to_le_bytes());
    message.extend_from_slice(&json_bytes);
    Ok(message)
}

pub fn decode_length_prefixed_json<T>(data: &[u8]) -> Result<T, ProtocolError>
where
    T: DeserializeOwned,
{
    if data.len() < 4 {
        return Err(ProtocolError::MessageTooShort { actual: data.len() });
    }

    let length = u32::from_le_bytes([data[0], data[1], data[2], data[3]]) as usize;

    if length > MAX_MESSAGE_SIZE_BYTES {
        return Err(ProtocolError::MessageTooLarge {
            length,
            max: MAX_MESSAGE_SIZE_BYTES,
        });
    }

    let expected_len = 4 + length;
    if data.len() < expected_len {
        return Err(ProtocolError::MessageIncomplete {
            expected: expected_len,
            actual: data.len(),
        });
    }

    let payload = &data[4..expected_len];
    Ok(serde_json::from_slice::<T>(payload)?)
}

pub fn read_frame<R>(reader: &mut R) -> Result<Vec<u8>, ProtocolError>
where
    R: Read,
{
    let mut prefix = [0_u8; 4];
    reader.read_exact(&mut prefix)?;

    let length = u32::from_le_bytes(prefix) as usize;
    if length > MAX_MESSAGE_SIZE_BYTES {
        return Err(ProtocolError::MessageTooLarge {
            length,
            max: MAX_MESSAGE_SIZE_BYTES,
        });
    }

    let mut payload = vec![0_u8; length];
    reader.read_exact(&mut payload)?;

    let mut frame = Vec::with_capacity(4 + length);
    frame.extend_from_slice(&prefix);
    frame.extend_from_slice(&payload);
    Ok(frame)
}

pub fn write_frame<W>(writer: &mut W, frame: &[u8]) -> Result<(), ProtocolError>
where
    W: Write,
{
    writer.write_all(frame)?;
    writer.flush()?;
    Ok(())
}
