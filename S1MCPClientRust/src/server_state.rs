use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use std::time::Duration;

use serde_json::json;
use tokio::sync::RwLock;
use tracing::{debug, info, warn};

use crate::config::Settings;
use crate::tcp::TcpClient;

const NOT_CONNECTED_MESSAGE: &str = "Error: Game is not connected.\nIf the game is already running, wait a moment and retry.\nOtherwise use s1_game with action='launch'.";

#[derive(Debug)]
pub struct ServerState {
    settings: Settings,
    tcp_client: Arc<TcpClient>,
    is_connected: AtomicBool,
    server_instructions: RwLock<Option<String>>,
}

impl ServerState {
    pub fn from_settings(settings: &Settings) -> Self {
        let tcp_client = TcpClient::new(
            settings.host.clone(),
            settings.port,
            Duration::from_secs(settings.connection_timeout),
            Duration::from_secs(settings.reconnect_delay),
        );

        Self {
            settings: settings.clone(),
            tcp_client: Arc::new(tcp_client),
            is_connected: AtomicBool::new(false),
            server_instructions: RwLock::new(None),
        }
    }

    pub fn settings(&self) -> &Settings {
        &self.settings
    }

    pub fn tcp_client(&self) -> Arc<TcpClient> {
        Arc::clone(&self.tcp_client)
    }

    pub async fn startup_handshake(&self) {
        match self.try_handshake().await {
            Ok(()) => info!("Initial handshake successful; game tools enabled"),
            Err(error) => {
                self.is_connected.store(false, Ordering::SeqCst);
                info!(error = %error, "Game not connected at startup; waiting for lifecycle flow");
            }
        }
    }

    pub async fn can_call_tool(&self, tool_name: &str) -> Result<(), String> {
        if tool_name == "s1_game" {
            return Ok(());
        }

        if self.is_connected.load(Ordering::SeqCst) && self.tcp_client.is_connected() {
            return Ok(());
        }

        match self.try_handshake().await {
            Ok(()) => {
                info!("Lazy reconnect succeeded; game tools are now available");
                Ok(())
            }
            Err(error) => {
                debug!(error = %error, "Lazy reconnect failed");
                Err(NOT_CONNECTED_MESSAGE.to_string())
            }
        }
    }

    pub async fn instructions_snapshot(&self) -> Option<String> {
        self.server_instructions.read().await.clone()
    }

    async fn try_handshake(&self) -> Result<(), String> {
        let response = self
            .tcp_client
            .async_call("handshake", Some(json!({})))
            .await
            .map_err(|error| error.to_string())?;

        if let Some(error) = response.error {
            self.is_connected.store(false, Ordering::SeqCst);
            return Err(format!("{} ({})", error.message, error.code));
        }

        self.is_connected.store(true, Ordering::SeqCst);

        let instructions = response
            .result
            .as_ref()
            .and_then(|value| value.as_object())
            .and_then(|obj| obj.get("instructions"))
            .and_then(|value| value.as_str())
            .map(ToOwned::to_owned);

        if let Some(ref text) = instructions {
            debug!(length = text.len(), "Stored handshake instructions for MCP init context");
        } else {
            warn!("Handshake completed without instructions payload");
        }

        *self.server_instructions.write().await = instructions;
        Ok(())
    }
}
