"""TCP client for communicating with the mod."""

import asyncio
import socket
import struct
import threading
import time
from typing import Any, Dict, Optional
import json

from .models.request import Request
from .models.response import Response
from .models.acknowledgment import Acknowledgment
from .protocol import serialize_request, deserialize_response, ProtocolError, create_request
from .utils.logger import get_logger


class TcpConnectionError(Exception):
    """TCP connection error."""
    pass


class TcpClient:
    """TCP client for mod communication."""
    
    def __init__(self, host: str = "localhost", port: int = 8765, timeout: float = 5.0, reconnect_delay: float = 0.25):
        """
        Initialize the TCP client.
        
        Args:
            host: Server hostname or IP address
            port: Server port number
            timeout: Connection timeout in seconds
            reconnect_delay: Delay before reconnection attempts in seconds
        """
        self.host = host
        self.port = port
        self.timeout = timeout
        self.reconnect_delay = reconnect_delay
        self.logger = get_logger()
        
        self._socket: Optional[socket.socket] = None
        self._connected = False
        self._lock = threading.RLock()  # Use reentrant lock to allow nested calls
        self._request_id_counter = 0
        self._request_id_lock = threading.Lock()
        self._heartbeat_thread: Optional[threading.Thread] = None
        self._heartbeat_stop_event = threading.Event()
    
    def connect(self) -> None:
        """
        Connect to the TCP server.
        
        Raises:
            TcpConnectionError: If connection fails
        """
        with self._lock:
            if self._connected:
                self.logger.debug("Already connected to TCP server")
                return
            
            try:
                self.logger.debug(f"Attempting to connect to TCP server: {self.host}:{self.port}")
                self.logger.debug(f"Connection timeout: {self.timeout}s")
                
                # Create socket
                self.logger.debug("Creating TCP socket...")
                self._socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
                # Set timeout only for connection, not for reads (reads will block until data arrives)
                # We'll use a longer timeout for reads in _read_message
                self._socket.settimeout(self.timeout)
                
                # Connect to server
                self.logger.debug(f"Connecting to {self.host}:{self.port}...")
                self._socket.connect((self.host, self.port))
                
                # Disable Nagle's algorithm for lower latency
                self._socket.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
                
                # After connection, set a longer timeout for read operations (90 seconds)
                # This allows for heartbeat intervals (60s) plus some buffer
                self._socket.settimeout(90.0)
                
                self._connected = True
                self.logger.info(f"Connected to TCP server successfully at {self.host}:{self.port}")
                self.logger.debug(f"Connection state: _connected={self._connected}, socket_valid={self._socket is not None}")
                
                # Start heartbeat thread
                self._start_heartbeat()
                
            except socket.timeout:
                self._socket = None
                self._connected = False
                error_msg = f"Connection timeout to {self.host}:{self.port}"
                self.logger.error(error_msg)
                raise TcpConnectionError(error_msg)
            except socket.error as e:
                self._socket = None
                self._connected = False
                error_msg = f"Failed to connect to TCP server: {e}"
                self.logger.error(error_msg)
                self.logger.debug(f"Error code: {e.errno if hasattr(e, 'errno') else 'N/A'}")
                raise TcpConnectionError(error_msg) from e
            except Exception as e:
                self._socket = None
                self._connected = False
                error_msg = f"Unexpected error connecting to TCP server: {e}"
                self.logger.error(error_msg)
                raise TcpConnectionError(error_msg) from e
    
    def disconnect(self) -> None:
        """Disconnect from the TCP server."""
        # Stop heartbeat thread
        self._stop_heartbeat()
        
        with self._lock:
            if self._socket is not None:
                try:
                    self._socket.close()
                    self.logger.debug("Socket closed successfully")
                except Exception as e:
                    self.logger.warning(f"Error closing socket: {e}")
                finally:
                    self._socket = None
                    self._connected = False
                    self.logger.info("Disconnected from TCP server")
            else:
                # Still mark as disconnected even if socket is None
                self._connected = False
                self.logger.debug("Disconnect called but socket was already None")
    
    def is_connected(self) -> bool:
        """Check if connected to the server."""
        with self._lock:
            return self._connected and self._socket is not None
    
    def _ensure_connected(self) -> None:
        """Ensure we're connected, reconnect if necessary."""
        if not self.is_connected():
            self.logger.debug("_ensure_connected: Not connected, attempting to connect...")
            self.connect()
        else:
            self.logger.debug("_ensure_connected: Already connected")
    
    def _get_next_request_id(self) -> int:
        """Get the next request ID (thread-safe)."""
        with self._request_id_lock:
            self._request_id_counter += 1
            return self._request_id_counter
    
    def _read_message(self) -> bytes:
        """
        Read a complete message from the socket.
        
        Returns:
            Complete message bytes (length prefix + JSON)
        
        Raises:
            TcpConnectionError: If read fails
        """
        try:
            self.logger.debug("Reading message from socket...")
            
            # Read 4-byte length prefix
            self.logger.debug("Reading 4-byte length prefix...")
            length_bytes = self._socket.recv(4)
            self.logger.debug(f"Read {len(length_bytes)} bytes for length prefix")
            
            if len(length_bytes) != 4:
                raise TcpConnectionError(f"Failed to read message length: got {len(length_bytes)} bytes instead of 4")
            
            message_length = struct.unpack('<I', length_bytes)[0]
            self.logger.debug(f"Message length: {message_length} bytes")
            
            if message_length < 0 or message_length > 10 * 1024 * 1024:  # Max 10MB
                raise TcpConnectionError(f"Invalid message length: {message_length}")
            
            # Read JSON message
            self.logger.debug(f"Reading {message_length} bytes of JSON message...")
            message_bytes = b''
            while len(message_bytes) < message_length:
                chunk = self._socket.recv(message_length - len(message_bytes))
                if not chunk:
                    raise TcpConnectionError(f"Socket closed before message complete (read {len(message_bytes)}/{message_length} bytes)")
                message_bytes += chunk
                self.logger.debug(f"Read {len(message_bytes)}/{message_length} bytes so far")
            
            self.logger.debug(f"Complete message read: {len(length_bytes + message_bytes)} total bytes")
            return length_bytes + message_bytes
            
        except socket.timeout as e:
            self.logger.error(f"Socket read timeout: {e}")
            raise TcpConnectionError(f"Socket read timeout: {e}") from e
        except socket.error as e:
            self.logger.error(f"Error reading from socket: {e}")
            raise TcpConnectionError(f"Error reading from socket: {e}") from e
    
    def _write_message(self, data: bytes) -> None:
        """
        Write a complete message to the socket.
        
        Args:
            data: Message bytes to write
        
        Raises:
            TcpConnectionError: If write fails
        """
        try:
            self.logger.debug(f"Writing {len(data)} bytes to socket...")
            
            # Write all data
            total_sent = 0
            while total_sent < len(data):
                sent = self._socket.send(data[total_sent:])
                if sent == 0:
                    raise TcpConnectionError("Socket connection broken during write")
                total_sent += sent
                self.logger.debug(f"Sent {total_sent}/{len(data)} bytes")
            
            self.logger.debug(f"Successfully wrote {len(data)} bytes")
            
        except socket.error as e:
            self.logger.error(f"Error writing to socket: {e}")
            raise TcpConnectionError(f"Error writing to socket: {e}") from e
    
    def _serialize_acknowledgment(self, ack: Acknowledgment) -> bytes:
        """
        Serialize an acknowledgment to the length-prefixed format.
        
        Args:
            ack: Acknowledgment object to serialize
        
        Returns:
            Serialized message bytes
        """
        logger = self.logger
        
        # Convert to dict
        json_dict = {
            "id": ack.id,
            "status": ack.status
        }
        logger.debug(f"Serializing acknowledgment: {json_dict}")
        
        # Serialize to JSON
        json_str = json.dumps(json_dict, ensure_ascii=False)
        json_bytes = json_str.encode('utf-8')
        logger.debug(f"Acknowledgment JSON length: {len(json_str)} chars, bytes: {len(json_bytes)}")
        
        # Prepend 4-byte length (little-endian int32)
        length_bytes = struct.pack('<I', len(json_bytes))
        
        result = length_bytes + json_bytes
        logger.debug(f"Total serialized acknowledgment: {len(result)} bytes")
        return result
    
    def call(self, method: str, params: Optional[Dict[str, Any]] = None) -> Response:
        """
        Call a method on the mod and wait for response.
        
        Args:
            method: Method name
            params: Optional parameters
        
        Returns:
            Response object
        
        Raises:
            TcpConnectionError: If communication fails
            ProtocolError: If protocol error occurs
        """
        self.logger.debug(f"call() invoked for method: {method}, params: {params}")
        self._ensure_connected()
        
        with self._lock:
            try:
                # Create request
                request_id = self._get_next_request_id()
                self.logger.debug(f"Generated request ID: {request_id}")
                request = create_request(request_id, method, params)
                self.logger.debug(f"Created request object: id={request.id}, method={request.method}, params={request.params}")
                
                # Serialize and send
                self.logger.debug("Serializing request...")
                request_bytes = serialize_request(request)
                self.logger.debug(f"Serialized request: {len(request_bytes)} bytes")
                self.logger.debug(f"Sending request: {method} (ID: {request_id})")
                self._write_message(request_bytes)
                self.logger.debug("Request sent successfully, waiting for response...")
                
                # Read response
                self.logger.debug("Reading response from socket...")
                response_bytes = self._read_message()
                self.logger.debug(f"Received response bytes: {len(response_bytes)} bytes")
                
                self.logger.debug("Deserializing response...")
                response = deserialize_response(response_bytes)
                self.logger.debug(f"Deserialized response: id={response.id}, has_error={response.error is not None}, has_result={response.result is not None}")
                
                # Verify response ID matches
                if response.id != request_id:
                    # Check if it's a server-initiated heartbeat
                    if (response.result and 
                        isinstance(response.result, dict) and 
                        response.result.get("type") == "server_heartbeat"):
                        self.logger.debug(f"Received server-initiated heartbeat (ID: {response.id}), continuing to wait for response to request {request_id}")
                        # This is a server heartbeat, not our response - continue waiting
                        # Read the next message (our actual response)
                        response_bytes = self._read_message()
                        response = deserialize_response(response_bytes)
                        # Verify this one matches
                        if response.id != request_id:
                            self.logger.warning(f"Response ID mismatch after server heartbeat: expected {request_id}, got {response.id}")
                        else:
                            self.logger.debug(f"Response ID matches request ID: {request_id}")
                    else:
                        self.logger.warning(f"Response ID mismatch: expected {request_id}, got {response.id}")
                else:
                    self.logger.debug(f"Response ID matches request ID: {request_id}")
                
                if response.error:
                    self.logger.debug(f"Response contains error: code={response.error.code}, message={response.error.message}")
                else:
                    self.logger.debug(f"Response contains result: {type(response.result)}")

                return response
                
            except (TcpConnectionError, ProtocolError) as e:
                # Mark as disconnected on error
                self.logger.error(f"Protocol/Connection error during call: {e}")
                self._connected = False
                raise
            except Exception as e:
                self.logger.error(f"Unexpected error during call: {e}", exc_info=True)
                self._connected = False
                raise TcpConnectionError(f"Unexpected error during call: {e}") from e
    
    def call_with_retry(self, method: str, params: Optional[Dict[str, Any]] = None, max_retries: int = 3) -> Response:
        """
        Call a method with automatic retry on connection errors.
        
        Args:
            method: Method name
            params: Optional parameters
            max_retries: Maximum number of retry attempts
        
        Returns:
            Response object
        
        Raises:
            TcpConnectionError: If all retries fail
        """
        self.logger.debug(f"call_with_retry: Starting call for method '{method}' with {max_retries} max retries")
        last_error = None
        
        for attempt in range(max_retries):
            try:
                self.logger.debug(f"call_with_retry: Attempt {attempt + 1}/{max_retries} for method '{method}'")
                return self.call(method, params)
            except TcpConnectionError as e:
                last_error = e
                self.logger.warning(f"call_with_retry: Attempt {attempt + 1}/{max_retries} failed: {e}")
                if attempt < max_retries - 1:
                    self.logger.debug(f"call_with_retry: Waiting {self.reconnect_delay}s before retry...")
                    time.sleep(self.reconnect_delay)
                    try:
                        self.logger.debug("call_with_retry: Disconnecting before retry...")
                        self.disconnect()
                    except Exception as disconnect_error:
                        self.logger.debug(f"call_with_retry: Error during disconnect (ignored): {disconnect_error}")
                    try:
                        self.logger.debug("call_with_retry: Attempting to reconnect...")
                        self.connect()
                        self.logger.debug("call_with_retry: Reconnected successfully")
                    except TcpConnectionError as connect_error:
                        self.logger.debug(f"call_with_retry: Reconnection failed (will retry): {connect_error}")
                else:
                    self.logger.error(f"call_with_retry: All {max_retries} attempts failed for method '{method}'")
        
        self.logger.error(f"call_with_retry: Giving up after {max_retries} failed attempts")
        raise last_error or TcpConnectionError("Call failed with unknown error")
    
    async def async_call(self, method: str, params: Optional[Dict[str, Any]] = None) -> "Response":
        """Async wrapper for call_with_retry — runs in thread pool so the event loop stays unblocked."""
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, lambda: self.call_with_retry(method, params))

    def __enter__(self):
        """Context manager entry."""
        self.connect()
        return self
    
    def __exit__(self, exc_type, exc_val, exc_tb):
        """Context manager exit."""
        self.disconnect()
    
    def _start_heartbeat(self) -> None:
        """Start the heartbeat thread."""
        if self._heartbeat_thread is not None and self._heartbeat_thread.is_alive():
            self.logger.debug("Heartbeat thread already running")
            return
        
        self._heartbeat_stop_event.clear()
        self._heartbeat_thread = threading.Thread(target=self._heartbeat_loop, daemon=True)
        self._heartbeat_thread.start()
        self.logger.debug("Heartbeat thread started")
    
    def _stop_heartbeat(self) -> None:
        """Stop the heartbeat thread."""
        if self._heartbeat_thread is None:
            return
        
        self._heartbeat_stop_event.set()
        if self._heartbeat_thread.is_alive():
            self._heartbeat_thread.join(timeout=2.0)
        self._heartbeat_thread = None
        self.logger.debug("Heartbeat thread stopped")
    
    def _heartbeat_loop(self) -> None:
        """Heartbeat loop that sends periodic heartbeat messages."""
        heartbeat_interval = 60.0  # 60 seconds
        self.logger.debug(f"Heartbeat loop started (interval: {heartbeat_interval}s)")
        
        while not self._heartbeat_stop_event.is_set():
            try:
                # Wait for heartbeat interval or stop event
                if self._heartbeat_stop_event.wait(timeout=heartbeat_interval):
                    # Stop event was set
                    break
                
                # Check if we're still connected
                if not self.is_connected():
                    self.logger.debug("Heartbeat: Not connected, skipping heartbeat")
                    continue
                
                # Check if lock is held (tool call in progress) - if so, skip this heartbeat
                # We can't use acquire(timeout) on a regular Lock to check without blocking
                # Instead, we'll just try the heartbeat and let it fail gracefully if lock is held
                try:
                    # Send heartbeat request - use call_with_retry with minimal retries
                    # If a tool call is in progress, this will wait briefly then fail gracefully
                    self.logger.debug("Sending heartbeat to server...")
                    try:
                        # Use call_with_retry with 1 retry max and shorter timeout
                        # This will fail quickly if lock is held or connection is bad
                        response = self.call_with_retry("heartbeat", {}, max_retries=1)
                        
                        if response.error:
                            self.logger.debug(f"Heartbeat error: {response.error.message}")
                        else:
                            self.logger.debug("Heartbeat sent successfully")
                    except TcpConnectionError as e:
                        self.logger.debug(f"Heartbeat connection error (will retry next interval): {e}")
                        # Connection lost, but don't break loop - it might recover
                    except Exception as e:
                        # This might include lock timeout or other issues - just log and continue
                        self.logger.debug(f"Error sending heartbeat (will retry next interval): {e}")
                        # Don't break the loop - connection might recover
                except Exception as e:
                    self.logger.debug(f"Error in heartbeat send attempt: {e}")
                    # Don't break the loop - connection might recover
                    
            except Exception as e:
                self.logger.debug(f"Error in heartbeat loop: {e}")
                # Wait a bit before retrying
                if not self._heartbeat_stop_event.wait(timeout=1.0):
                    continue
                else:
                    break
        
        self.logger.debug("Heartbeat loop ended")

