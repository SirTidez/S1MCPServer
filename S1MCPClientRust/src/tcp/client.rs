//! Async TCP client implemented with the Actor pattern.
//!
//! A background Tokio task owns the `tokio::net::TcpStream`. Callers send
//! `(Request, oneshot::Sender<Response>)` pairs over an `mpsc` channel; the
//! actor task serialises and sends them, then reads the response and replies
//! through the oneshot. This eliminates `spawn_blocking`, all
//! `Mutex<ClientState>` contention on I/O, and lock-poisoning risk (the only
//! remaining `Mutex` uses `parking_lot` which never poisons).

use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::Arc;
use std::time::Duration;

use parking_lot::Mutex;
use serde_json::Value;
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::TcpStream;
use tokio::sync::{mpsc, oneshot};
use tokio::task::JoinHandle;
use tokio::time::MissedTickBehavior;
use tracing::{debug, info, warn};

use crate::models::Response;
use crate::protocol::{
    create_request, deserialize_response, is_server_heartbeat, serialize_request, ProtocolError,
    MAX_MESSAGE_SIZE_BYTES,
};
use crate::tcp::error::TcpClientError;

// ---------------------------------------------------------------------------
// Internal actor message
// ---------------------------------------------------------------------------

struct ActorRequest {
    method: String,
    params: Option<Value>,
    reply: oneshot::Sender<Result<Response, TcpClientError>>,
}

// ---------------------------------------------------------------------------
// TcpClient
// ---------------------------------------------------------------------------

/// Cloneable handle to the background actor task.
#[derive(Debug, Clone)]
pub struct TcpClient {
    host: String,
    port: u16,
    timeout: Duration,
    reconnect_delay: Duration,
    heartbeat_interval: Duration,
    request_id_counter: Arc<AtomicU64>,
    /// `true` once the actor task is running and the socket is open.
    connected: Arc<AtomicBool>,
    /// Channel into the actor. `None` before first connect.
    tx: Arc<Mutex<Option<mpsc::Sender<ActorRequest>>>>,
    /// Handle to the actor Tokio task.
    actor_task: Arc<Mutex<Option<JoinHandle<()>>>>,
    /// Handle to the heartbeat Tokio task.
    heartbeat_task: Arc<Mutex<Option<JoinHandle<()>>>>,
}

impl TcpClient {
    pub fn new(
        host: impl Into<String>,
        port: u16,
        timeout: Duration,
        reconnect_delay: Duration,
    ) -> Self {
        Self {
            host: host.into(),
            port,
            timeout,
            reconnect_delay,
            heartbeat_interval: Duration::from_secs(60),
            request_id_counter: Arc::new(AtomicU64::new(0)),
            connected: Arc::new(AtomicBool::new(false)),
            tx: Arc::new(Mutex::new(None)),
            actor_task: Arc::new(Mutex::new(None)),
            heartbeat_task: Arc::new(Mutex::new(None)),
        }
    }

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    pub async fn connect(&self) -> Result<(), TcpClientError> {
        if self.connected.load(Ordering::SeqCst) {
            debug!("Already connected to TCP server");
            return Ok(());
        }

        let addr = format!("{}:{}", self.host, self.port);
        debug!(host = %self.host, port = self.port, "Attempting TCP connection");

        let stream = tokio::time::timeout(self.timeout, TcpStream::connect(&addr))
            .await
            .map_err(|_| {
                TcpClientError::Connection(format!(
                    "connect to {}:{} timed out after {}s",
                    self.host,
                    self.port,
                    self.timeout.as_secs()
                ))
            })?
            .map_err(|e| {
                TcpClientError::Connection(format!(
                    "failed to connect to {}:{}: {}",
                    self.host, self.port, e
                ))
            })?;

        stream.set_nodelay(true)?;
        info!(host = %self.host, port = self.port, "Connected to TCP server");

        // Small delay so the C# server exits its 100 ms startup pause and
        // enters its read loop before we send the first request.
        tokio::time::sleep(Duration::from_millis(150)).await;

        // Spin up the actor
        let (tx, rx) = mpsc::channel::<ActorRequest>(64);
        let actor = Actor {
            stream,
            rx,
            connected: Arc::clone(&self.connected),
        };
        let handle = tokio::spawn(actor.run());

        *self.tx.lock() = Some(tx);
        *self.actor_task.lock() = Some(handle);
        self.connected.store(true, Ordering::SeqCst);

        self.start_heartbeat();
        Ok(())
    }

    pub fn disconnect(&self) {
        self.stop_heartbeat();
        // Dropping the sender closes the mpsc channel, which signals the actor
        // to exit its loop.
        *self.tx.lock() = None;
        self.connected.store(false, Ordering::SeqCst);

        if let Some(handle) = self.actor_task.lock().take() {
            handle.abort();
        }
        info!("Disconnected from TCP server");
    }

    pub fn is_connected(&self) -> bool {
        self.connected.load(Ordering::SeqCst)
    }

    /// Send a single request and await its response (no retry).
    pub async fn call(
        &self,
        method: &str,
        params: Option<Value>,
    ) -> Result<Response, TcpClientError> {
        let tx = {
            let guard = self.tx.lock();
            guard.clone().ok_or(TcpClientError::NotConnected)?
        };

        let request_id = self.next_request_id();
        debug!(method, request_id, "Dispatching request to actor");

        let (reply_tx, reply_rx) = oneshot::channel();
        tx.send(ActorRequest {
            method: method.to_string(),
            params,
            reply: reply_tx,
        })
        .await
        .map_err(|_| TcpClientError::NotConnected)?;

        reply_rx
            .await
            .map_err(|_| TcpClientError::NotConnected)?
    }

    /// Send a request with up to `max_retries` attempts on retryable errors.
    pub async fn call_with_retry(
        &self,
        method: &str,
        params: Option<Value>,
        max_retries: usize,
    ) -> Result<Response, TcpClientError> {
        let attempts = max_retries.max(1);
        let mut last_error: Option<TcpClientError> = None;

        for attempt in 0..attempts {
            match self.call(method, params.clone()).await {
                Ok(response) => return Ok(response),
                Err(error) if error.is_retryable_connection() => {
                    warn!(
                        attempt = attempt + 1,
                        attempts,
                        error = %error,
                        "TCP call attempt failed"
                    );
                    last_error = Some(error);

                    if attempt + 1 < attempts {
                        tokio::time::sleep(self.reconnect_delay).await;
                        self.disconnect();
                        let _ = self.connect().await;
                    }
                }
                Err(error) => return Err(error),
            }
        }

        Err(last_error
            .unwrap_or_else(|| TcpClientError::Connection("retry exhausted".to_string())))
    }

    /// Convenience alias — now a thin wrapper because `call_with_retry` is
    /// already async; no `spawn_blocking` needed.
    pub async fn async_call(
        &self,
        method: &str,
        params: Option<Value>,
    ) -> Result<Response, TcpClientError> {
        self.call_with_retry(method, params, 3).await
    }

    // -----------------------------------------------------------------------
    // Heartbeat
    // -----------------------------------------------------------------------

    pub fn start_heartbeat(&self) {
        let mut guard = self.heartbeat_task.lock();
        if guard
            .as_ref()
            .is_some_and(|h: &JoinHandle<()>| !h.is_finished())
        {
            debug!("Heartbeat task already running");
            return;
        }

        let client = self.clone();
        let handle = tokio::spawn(async move {
            client.heartbeat_loop().await;
        });
        *guard = Some(handle);
        debug!("Heartbeat task started");
    }

    pub fn stop_heartbeat(&self) {
        if let Some(handle) = self.heartbeat_task.lock().take() {
            handle.abort();
            debug!("Heartbeat task stopped");
        }
    }

    async fn heartbeat_loop(&self) {
        let mut interval = tokio::time::interval(self.heartbeat_interval);
        interval.set_missed_tick_behavior(MissedTickBehavior::Delay);
        interval.tick().await; // skip the immediate first tick

        loop {
            interval.tick().await;

            if !self.is_connected() {
                continue;
            }

            match self.call_with_retry("heartbeat", Some(serde_json::json!({})), 1).await {
                Ok(response) => {
                    if let Some(error) = response.error {
                        debug!(code = error.code, message = %error.message, "Heartbeat returned error");
                    }
                }
                Err(error) => {
                    debug!(error = %error, "Heartbeat call failed");
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    fn next_request_id(&self) -> u64 {
        self.request_id_counter.fetch_add(1, Ordering::SeqCst) + 1
    }
}

impl Default for TcpClient {
    fn default() -> Self {
        Self::new(
            "127.0.0.1",
            8765,
            Duration::from_secs(5),
            Duration::from_millis(250),
        )
    }
}

// ---------------------------------------------------------------------------
// Actor task — owns the TcpStream
// ---------------------------------------------------------------------------

struct Actor {
    stream: TcpStream,
    rx: mpsc::Receiver<ActorRequest>,
    connected: Arc<AtomicBool>,
}

impl Actor {
    async fn run(mut self) {
        while let Some(request) = self.rx.recv().await {
            let result = self.handle_request(request.method, request.params).await;
            if result.is_err() {
                self.connected.store(false, Ordering::SeqCst);
            }
            let _ = request.reply.send(result);
        }
        // Channel closed — client disconnected.
        self.connected.store(false, Ordering::SeqCst);
        debug!("Actor task exiting");
    }

    async fn handle_request(
        &mut self,
        method: String,
        params: Option<Value>,
    ) -> Result<Response, TcpClientError> {
        // Synthesise a placeholder request ID (actor is single-threaded so IDs
        // don't need global uniqueness here; the TcpClient counter is used for
        // tracing and the C# server echoes back whatever ID it receives).
        let request = create_request(0, &method, params);
        let frame = serialize_request(&request)?;

        // --- Write ---
        self.write_frame(&frame).await?;

        // --- Read loop — skip server heartbeats ---
        loop {
            let response_frame = self.read_frame().await?;
            let response = deserialize_response(&response_frame)?;

            if is_server_heartbeat(&response) {
                debug!("Actor: received server heartbeat; continuing to wait for response");
                continue;
            }

            return Ok(response);
        }
    }

    async fn write_frame(&mut self, frame: &[u8]) -> Result<(), TcpClientError> {
        self.stream
            .write_all(frame)
            .await
            .map_err(|e| TcpClientError::Io(e))?;
        self.stream
            .flush()
            .await
            .map_err(|e| TcpClientError::Io(e))?;
        Ok(())
    }

    async fn read_frame(&mut self) -> Result<Vec<u8>, TcpClientError> {
        // Read 4-byte little-endian length prefix
        let mut prefix = [0_u8; 4];
        tokio::time::timeout(
            Duration::from_secs(90),
            self.stream.read_exact(&mut prefix),
        )
        .await
        .map_err(|_| {
            TcpClientError::Connection(
                "Read timed out (90s). Ensure the game is running, in the main scene, and the mod is loaded."
                    .to_string(),
            )
        })?
        .map_err(|e| TcpClientError::Io(e))?;

        let length = u32::from_le_bytes(prefix) as usize;
        if length > MAX_MESSAGE_SIZE_BYTES {
            return Err(TcpClientError::Protocol(ProtocolError::MessageTooLarge {
                length,
                max: MAX_MESSAGE_SIZE_BYTES,
            }));
        }

        let mut payload = vec![0_u8; length];
        self.stream
            .read_exact(&mut payload)
            .await
            .map_err(|e| TcpClientError::Io(e))?;

        let mut frame = Vec::with_capacity(4 + length);
        frame.extend_from_slice(&prefix);
        frame.extend_from_slice(&payload);
        Ok(frame)
    }
}
