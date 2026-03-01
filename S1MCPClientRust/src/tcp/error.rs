use thiserror::Error;

use crate::protocol::ProtocolError;

#[derive(Debug, Error)]
pub enum TcpClientError {
    #[error("tcp connection error: {0}")]
    Connection(String),
    #[error("address resolution failed: {0}")]
    AddressResolution(String),
    #[error("tcp stream not connected")]
    NotConnected,
    #[error("i/o error: {0}")]
    Io(#[from] std::io::Error),
    #[error("protocol error: {0}")]
    Protocol(#[from] ProtocolError),
    #[error("async task failed: {0}")]
    AsyncTask(#[from] tokio::task::JoinError),
}

impl TcpClientError {
    pub fn is_retryable_connection(&self) -> bool {
        matches!(
            self,
            Self::Connection(_) | Self::AddressResolution(_) | Self::NotConnected | Self::Io(_)
        )
    }
}
