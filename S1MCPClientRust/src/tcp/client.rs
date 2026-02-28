use std::net::{Shutdown, SocketAddr, TcpStream, ToSocketAddrs};
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use serde_json::Value;
use tokio::runtime::Handle;
use tokio::task::JoinHandle;
use tokio::time::MissedTickBehavior;
use tracing::{debug, info, warn};

use crate::models::Response;
use crate::protocol::{
    create_request, deserialize_response, is_server_heartbeat, read_frame, serialize_request,
    write_frame, ProtocolError,
};
use crate::tcp::error::TcpClientError;

#[derive(Debug)]
struct ClientState {
    stream: Option<TcpStream>,
    connected: bool,
}

#[derive(Debug, Clone)]
pub struct TcpClient {
    host: String,
    port: u16,
    timeout: Duration,
    reconnect_delay: Duration,
    heartbeat_interval: Duration,
    request_id_counter: Arc<AtomicU64>,
    heartbeat_stop: Arc<AtomicBool>,
    heartbeat_task: Arc<Mutex<Option<JoinHandle<()>>>>,
    state: Arc<Mutex<ClientState>>,
}

impl TcpClient {
    pub fn new(host: impl Into<String>, port: u16, timeout: Duration, reconnect_delay: Duration) -> Self {
        Self {
            host: host.into(),
            port,
            timeout,
            reconnect_delay,
            heartbeat_interval: Duration::from_secs(60),
            request_id_counter: Arc::new(AtomicU64::new(0)),
            heartbeat_stop: Arc::new(AtomicBool::new(false)),
            heartbeat_task: Arc::new(Mutex::new(None)),
            state: Arc::new(Mutex::new(ClientState {
                stream: None,
                connected: false,
            })),
        }
    }

    pub fn connect(&self) -> Result<(), TcpClientError> {
        let mut state = self.state.lock().expect("tcp client state poisoned");
        if state.connected {
            debug!("Already connected to TCP server");
            return Ok(());
        }

        let address = self.resolve_socket_addr()?;
        debug!(
            host = %self.host,
            port = self.port,
            timeout_ms = self.timeout.as_millis() as u64,
            "Attempting TCP connection"
        );

        let stream = match TcpStream::connect_timeout(&address, self.timeout) {
            Ok(stream) => stream,
            Err(error) => {
                state.stream = None;
                state.connected = false;
                return Err(TcpClientError::Connection(format!(
                    "failed to connect to {}:{}: {}",
                    self.host, self.port, error
                )));
            }
        };

        stream.set_nodelay(true)?;
        stream.set_read_timeout(Some(Duration::from_secs(90)))?;
        stream.set_write_timeout(Some(self.timeout))?;

        state.stream = Some(stream);
        state.connected = true;
        drop(state);

        info!(host = %self.host, port = self.port, "Connected to TCP server");

        // Give the C# server's HandleClient time to finish its initial 100ms delay and enter
        // the read loop so the first request is not sent before the server is ready to read.
        std::thread::sleep(Duration::from_millis(150));

        self.start_heartbeat();
        Ok(())
    }

    pub fn disconnect(&self) {
        self.stop_heartbeat();

        let mut state = self.state.lock().expect("tcp client state poisoned");
        if let Some(stream) = state.stream.take() {
            let _ = stream.shutdown(Shutdown::Both);
            debug!("TCP stream closed");
        }
        state.connected = false;
        info!("Disconnected from TCP server");
    }

    pub fn is_connected(&self) -> bool {
        let state = self.state.lock().expect("tcp client state poisoned");
        state.connected && state.stream.is_some()
    }

    pub fn call(&self, method: &str, params: Option<Value>) -> Result<Response, TcpClientError> {
        self.ensure_connected()?;

        let request_id = self.next_request_id();
        let request = create_request(request_id, method, params);
        let request_bytes = serialize_request(&request)?;

        let mut state = self.state.lock().expect("tcp client state poisoned");
        let stream = state.stream.as_mut().ok_or(TcpClientError::NotConnected)?;

        let request_len = request_bytes.len();
        debug!(method, request_id, bytes = request_len, "Sending request; waiting for response");
        if let Err(error) = write_frame(stream, &request_bytes) {
            state.connected = false;
            state.stream = None;
            return Err(TcpClientError::from(error));
        }

        loop {
            debug!(request_id, "Reading response frame from stream");
            let response_frame = match read_frame(stream) {
                Ok(frame) => frame,
                Err(error) => {
                    state.connected = false;
                    state.stream = None;
                    let tcp_error = match &error {
                        ProtocolError::Io(io_err)
                            if io_err.kind() == std::io::ErrorKind::TimedOut =>
                        {
                            TcpClientError::Connection(
                                "Read timed out (90s). Ensure the game is running, in the main scene, and the mod is loaded.".to_string(),
                            )
                        }
                        _ => TcpClientError::from(error),
                    };
                    return Err(tcp_error);
                }
            };

            let response = match deserialize_response(&response_frame) {
                Ok(response) => {
                    debug!(
                        response_id = response.id,
                        request_id,
                        frame_bytes = response_frame.len(),
                        "Deserialized response"
                    );
                    response
                }
                Err(error) => {
                    state.connected = false;
                    state.stream = None;
                    return Err(TcpClientError::from(error));
                }
            };

            if response.id != request_id {
                if is_server_heartbeat(&response) {
                    debug!(
                        response_id = response.id,
                        request_id,
                        "Received server heartbeat while waiting for request response"
                    );
                    continue;
                }

                warn!(
                    response_id = response.id,
                    request_id,
                    "Response ID mismatch; returning response for compatibility"
                );
            }

            return Ok(response);
        }
    }

    pub fn call_with_retry(
        &self,
        method: &str,
        params: Option<Value>,
        max_retries: usize,
    ) -> Result<Response, TcpClientError> {
        let mut last_error: Option<TcpClientError> = None;
        let attempts = max_retries.max(1);

        for attempt in 0..attempts {
            match self.call(method, params.clone()) {
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
                        std::thread::sleep(self.reconnect_delay);
                        self.disconnect();
                        let _ = self.connect();
                    }
                }
                Err(error) => return Err(error),
            }
        }

        Err(last_error.unwrap_or_else(|| TcpClientError::Connection("retry exhausted".to_string())))
    }

    pub async fn async_call(&self, method: &str, params: Option<Value>) -> Result<Response, TcpClientError> {
        let client = self.clone();
        let method = method.to_string();
        tokio::task::spawn_blocking(move || client.call_with_retry(&method, params, 3)).await?
    }

    fn ensure_connected(&self) -> Result<(), TcpClientError> {
        if self.is_connected() {
            return Ok(());
        }

        self.connect()
    }

    fn resolve_socket_addr(&self) -> Result<SocketAddr, TcpClientError> {
        let mut addresses = (self.host.as_str(), self.port)
            .to_socket_addrs()
            .map_err(|error| TcpClientError::AddressResolution(error.to_string()))?;

        addresses.next().ok_or_else(|| {
            TcpClientError::AddressResolution(format!("no address resolved for {}:{}", self.host, self.port))
        })
    }

    fn next_request_id(&self) -> u64 {
        self.request_id_counter.fetch_add(1, Ordering::SeqCst) + 1
    }

    pub fn start_heartbeat(&self) {
        let mut heartbeat_task = self
            .heartbeat_task
            .lock()
            .expect("tcp client heartbeat lock poisoned");

        if heartbeat_task
            .as_ref()
            .is_some_and(|task_handle| !task_handle.is_finished())
        {
            debug!("Heartbeat task already running");
            return;
        }

        let runtime_handle = match Handle::try_current() {
            Ok(runtime_handle) => runtime_handle,
            Err(_) => {
                warn!("Skipping heartbeat startup because no Tokio runtime is available");
                return;
            }
        };

        self.heartbeat_stop.store(false, Ordering::SeqCst);
        let client = self.clone();
        let handle = runtime_handle.spawn(async move {
            client.heartbeat_loop_async().await;
        });
        *heartbeat_task = Some(handle);
        debug!("Heartbeat task started");
    }

    pub fn stop_heartbeat(&self) {
        self.heartbeat_stop.store(true, Ordering::SeqCst);
        let mut heartbeat_task = self
            .heartbeat_task
            .lock()
            .expect("tcp client heartbeat lock poisoned");

        if let Some(handle) = heartbeat_task.take() {
            handle.abort();
            debug!("Heartbeat task stopped");
        }
    }

    async fn heartbeat_loop_async(&self) {
        let mut interval = tokio::time::interval(self.heartbeat_interval);
        interval.set_missed_tick_behavior(MissedTickBehavior::Delay);
        interval.tick().await;

        while !self.heartbeat_stop.load(Ordering::SeqCst) {
            interval.tick().await;
            if self.heartbeat_stop.load(Ordering::SeqCst) {
                break;
            }

            if !self.is_connected() {
                continue;
            }

            let client = self.clone();
            match tokio::task::spawn_blocking(move || {
                client.call_with_retry("heartbeat", Some(serde_json::json!({})), 1)
            })
            .await
            {
                Ok(Ok(response)) => {
                    if let Some(error) = response.error {
                        debug!(code = error.code, message = %error.message, "Heartbeat returned error");
                    }
                }
                Ok(Err(error)) => {
                    debug!(error = %error, "Heartbeat call failed");
                }
                Err(error) => {
                    if error.is_cancelled() {
                        debug!("Heartbeat task cancelled");
                        break;
                    }

                    debug!(error = %error, "Heartbeat task join failed");
                }
            }
        }
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
