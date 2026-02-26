using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using S1MCPServer.Core;
using S1MCPServer.Models;
using S1MCPServer.Utils;

namespace S1MCPServer.Server;

/// <summary>
/// TCP server that handles communication with the MCP server.
/// Runs on a background thread and marshals commands to the main thread via CommandQueue.
/// </summary>
public class TcpServer
{
    private readonly CommandQueue _commandQueue;
    private readonly ResponseQueue _responseQueue;
    private TcpListener? _tcpListener;
    private TcpClient? _connectedClient;
    private NetworkStream? _clientStream;
    private bool _isRunning;
    private Task? _serverTask;
    private Task? _responseTask;
    private Task? _heartbeatTask;
    private readonly System.Threading.SemaphoreSlim _streamSemaphore = new System.Threading.SemaphoreSlim(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<bool>> _pendingRequests = new();
    private System.Threading.CancellationTokenSource? _lifecycleCts;
    private int _heartbeatRequestId = 0;
    private readonly object _heartbeatIdLock = new object();

    private const int DefaultPort = 8765;

    public TcpServer(CommandQueue commandQueue, ResponseQueue responseQueue, int port = DefaultPort)
    {
        _commandQueue = commandQueue;
        _responseQueue = responseQueue;
        Port = port;
    }

    public int Port { get; }

    /// <summary>
    /// Starts the TCP server on a background thread.
    /// </summary>
    public void Start()
    {
        if (_isRunning)
        {
            ModLogger.Warn("TCP server is already running");
            return;
        }

        _isRunning = true;
        _lifecycleCts = new System.Threading.CancellationTokenSource();
        var lifecycleToken = _lifecycleCts.Token;

        _serverTask = Task.Run(() => ServerLoop(lifecycleToken), lifecycleToken);
        _responseTask = Task.Run(() => ResponseLoop(lifecycleToken), lifecycleToken);
        _heartbeatTask = Task.Run(() => HeartbeatLoop(lifecycleToken), lifecycleToken);
        ModLogger.Info($"TCP server started on port {Port}");
    }

    /// <summary>
    /// Stops the TCP server gracefully.
    /// </summary>
    public void Stop()
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;
        _lifecycleCts?.Cancel();
        CompletePendingRequests(success: false);
        
        _clientStream?.Close();
        _connectedClient?.Close();
        _tcpListener?.Stop();

        _clientStream = null;
        _connectedClient = null;
        _tcpListener = null;

        WaitForTaskCompletion(_serverTask, nameof(_serverTask));
        WaitForTaskCompletion(_responseTask, nameof(_responseTask));
        WaitForTaskCompletion(_heartbeatTask, nameof(_heartbeatTask));

        _serverTask = null;
        _responseTask = null;
        _heartbeatTask = null;

        _lifecycleCts?.Dispose();
        _lifecycleCts = null;

        ModLogger.Info("TCP server stopped");
    }

    /// <summary>
    /// Main server loop that accepts client connections.
    /// </summary>
    private async Task ServerLoop(System.Threading.CancellationToken cancellationToken)
    {
        ModLogger.Debug("ServerLoop started");
        while (_isRunning && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                ModLogger.Debug($"Creating TCP listener on port {Port}...");
                _tcpListener = new TcpListener(IPAddress.Loopback, Port);
                _tcpListener.Start();
                ModLogger.Info($"TCP server listening on {IPAddress.Loopback}:{Port}");

                ModLogger.Debug("Waiting for client connection...");
                var newClient = await _tcpListener.AcceptTcpClientAsync();
                
                // If we already have a connected client, close the new one
                // This prevents multiple MCP client instances from connecting simultaneously
                if (_connectedClient != null && _connectedClient.Connected)
                {
                    ModLogger.Warn($"Rejecting new client connection from {newClient.Client.RemoteEndPoint} - already have a connected client");
                    newClient.Close();
                    continue;
                }
                
                _connectedClient = newClient;
                _clientStream = _connectedClient.GetStream();
                
                ModLogger.Info($"Client connected from {_connectedClient.Client.RemoteEndPoint}");
                ModLogger.Debug($"Client stream - CanRead: {_clientStream.CanRead}, CanWrite: {_clientStream.CanWrite}");

                // Handle client communication
                ModLogger.Debug("Starting client handler...");
                await HandleClient(_clientStream, cancellationToken);

                ModLogger.Info("Client disconnected");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (_isRunning)
                {
                    ModLogger.Error($"TCP server error: {ex.Message}");
                    ModLogger.Debug($"Exception type: {ex.GetType().Name}");
                    ModLogger.Debug($"Stack trace: {ex}");
                }
            }
            finally
            {
                ModLogger.Debug("Cleaning up client connection...");
                _clientStream?.Close();
                _connectedClient?.Close();
                _clientStream = null;
                _connectedClient = null;
                CompletePendingRequests(success: false);

                _tcpListener?.Stop();
                _tcpListener = null;

                // Wait a bit before trying to reconnect
                if (_isRunning && !cancellationToken.IsCancellationRequested)
                {
                    ModLogger.Debug("Waiting 1 second before reconnecting...");
                    try
                    {
                        await Task.Delay(1000, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
            }
        }
        ModLogger.Debug("ServerLoop ended");
    }

    /// <summary>
    /// Handles communication with a connected client.
    /// </summary>
    private async Task HandleClient(NetworkStream stream, System.Threading.CancellationToken cancellationToken)
    {
        ModLogger.Debug($"HandleClient started (CanRead: {stream.CanRead}, CanWrite: {stream.CanWrite})");

        // Give the client a moment to be ready
        try
        {
            await Task.Delay(100, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Cancelled when this connection ends — used to abort pending TCS waits promptly
        using var disconnectCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        while (_isRunning && !cancellationToken.IsCancellationRequested && stream.CanRead && _connectedClient?.Connected == true)
        {
            try
            {
                ModLogger.Debug("Waiting to read message from client...");
                
                // Read next request from client
                string jsonMessage = string.Empty;
                bool shouldContinue = false;

                // Check connection before reading
                if (!stream.CanRead || _connectedClient?.Connected != true)
                {
                    ModLogger.Debug("HandleClient: Stream disconnected before read");
                    break;
                }

                try
                {
                    jsonMessage = await ProtocolHandler.ReadMessageAsync(stream);
                    ModLogger.Debug($"Received raw JSON message ({jsonMessage.Length} chars): {jsonMessage}");
                }
                catch (IOException ex) when (ex.Message.Contains("No data available"))
                {
                    // Client is waiting for response - this is normal in request-response pattern
                    ModLogger.Debug("HandleClient: No data available, client may be waiting for response. Waiting before next read...");
                    shouldContinue = true;
                }
                
                if (shouldContinue)
                {
                    try
                    {
                        await Task.Delay(200, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        disconnectCts.Cancel();
                        break;
                    }
                    continue;
                }

                // Deserialize request
                Request request;
                try
                {
                    ModLogger.Debug("Deserializing request...");
                    request = ProtocolHandler.DeserializeRequest(jsonMessage);
                    ModLogger.Debug($"Deserialized request: ID={request.Id}, Method={request.Method}, Params={System.Text.Json.JsonSerializer.Serialize(request.Params)}");
                }
                catch (Exception ex)
                {
                    ModLogger.Error($"Failed to deserialize request: {ex.Message}");
                    ModLogger.Debug($"Deserialization error type: {ex.GetType().Name}");
                    ModLogger.Debug($"Stack trace: {ex}");
                    var errorResponse = ProtocolHandler.CreateErrorResponse(
                        0, // Unknown ID
                        -32700, // Parse error
                        "Invalid JSON",
                        new { details = ex.Message }
                    );
                    ModLogger.Debug("Sending parse error response to client...");
                    // Use semaphore for write operation too
                    await _streamSemaphore.WaitAsync(cancellationToken);
                    try
                    {
                        if (stream.CanWrite && _connectedClient?.Connected == true)
                        {
                            await ProtocolHandler.WriteMessageAsync(stream, ProtocolHandler.SerializeResponse(errorResponse));
                        }
                    }
                    finally
                    {
                        _streamSemaphore.Release();
                    }
                    continue;
                }

                // Enqueue command for main thread processing
                ModLogger.Debug($"Enqueuing command: {request.Method} (ID: {request.Id})");
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pendingRequests[request.Id] = tcs;
                try
                {
                    _commandQueue.EnqueueCommand(request);
                    ModLogger.Debug($"Command enqueued successfully. Queue size: {_commandQueue.Count}");

                    // Wait for ResponseLoop to confirm the response was sent.
                    // Also race against a timeout and the client disconnect signal so we don't
                    // block for 10 s when the connection drops mid-request.
                    var responseTask   = tcs.Task;
                    var timeoutTask    = Task.Delay(TimeSpan.FromSeconds(10));
                    var disconnectTask = Task.Delay(Timeout.Infinite, disconnectCts.Token);
                    var completed = await Task.WhenAny(responseTask, timeoutTask, disconnectTask);

                    if (completed == timeoutTask)
                        ModLogger.Warn($"Timeout waiting for response to be sent for request {request.Id}");
                    else if (completed == disconnectTask)
                        ModLogger.Debug($"Client disconnected while waiting for response to request {request.Id}");
                    else
                        ModLogger.Debug($"Response for request {request.Id} sent successfully");
                }
                finally
                {
                    _pendingRequests.TryRemove(request.Id, out _);
                }
            }
            catch (OperationCanceledException)
            {
                disconnectCts.Cancel();
                break;
            }
            catch (IOException ex)
            {
                // Client disconnected or stream error — signal any pending TCS waits
                disconnectCts.Cancel();
                ModLogger.Debug($"Client connection lost (IOException): {ex.Message}");
                ModLogger.Debug($"Exception type: {ex.GetType().Name}");
                break;
            }
            catch (Exception ex)
            {
                disconnectCts.Cancel();
                ModLogger.Error($"Error handling client: {ex.Message}");
                ModLogger.Debug($"Exception type: {ex.GetType().Name}");
                ModLogger.Debug($"Stack trace: {ex}");
                break;
            }
        }
        ModLogger.Debug("HandleClient ended");
    }

    /// <summary>
    /// Response loop that sends responses back to the client.
    /// </summary>
    private async Task ResponseLoop(System.Threading.CancellationToken cancellationToken)
    {
        ModLogger.Debug("ResponseLoop started");
        while (_isRunning && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Wait for responses from main thread
                if (_responseQueue.TryDequeue(out Response? response) && response != null)
                {
                    ModLogger.Debug($"Dequeued response for request ID: {response.Id}, has_error: {response.Error != null}, has_result: {response.Result != null}");
                    
                    if (_clientStream != null && _clientStream.CanWrite && _connectedClient?.Connected == true)
                    {
                        try
                        {
                            ModLogger.Debug($"Serializing response for ID: {response.Id}...");
                            string jsonResponse = ProtocolHandler.SerializeResponse(response);
                            ModLogger.Debug($"Serialized response ({jsonResponse.Length} chars): {jsonResponse}");
                            
                            ModLogger.Debug($"Writing response to stream for ID: {response.Id}...");
                            
                            // Use semaphore to prevent concurrent read/write operations
                            await _streamSemaphore.WaitAsync(cancellationToken);
                            try
                            {
                                // Check connection again inside semaphore
                                if (_clientStream == null || !_clientStream.CanWrite || _connectedClient?.Connected != true)
                                {
                                    ModLogger.Debug($"Stream disconnected while preparing to write response for ID: {response.Id}");
                                    throw new IOException("Stream disconnected");
                                }
                                
                                // Write response
                                await ProtocolHandler.WriteMessageAsync(_clientStream, jsonResponse);
                            }
                            finally
                            {
                                _streamSemaphore.Release();
                            }
                            
                            ModLogger.Debug($"Successfully sent response for request ID: {response.Id}");

                            // Signal HandleClient that response was sent
                            if (_pendingRequests.TryGetValue(response.Id, out var responseTcs))
                                responseTcs.TrySetResult(true);
                        }
                        catch (Exception ex)
                        {
                            ModLogger.Error($"Failed to send response for ID {response.Id}: {ex.Message}");
                            ModLogger.Debug($"Exception type: {ex.GetType().Name}");
                            ModLogger.Debug($"Stack trace: {ex}");

                            // Signal HandleClient immediately so it doesn't wait out the full timeout
                            if (_pendingRequests.TryGetValue(response.Id, out var failedTcs))
                                failedTcs.TrySetResult(false);

                            // Don't re-enqueue if stream is broken/disconnected
                            if (ex.Message.Contains("broken") || ex.Message.Contains("disconnected") || ex.Message.Contains("EOF"))
                            {
                                ModLogger.Debug($"Stream is broken/disconnected, not re-enqueuing response for ID: {response.Id}");
                            }
                            else
                            {
                                // Re-enqueue response to try again later
                                ModLogger.Debug($"Re-enqueuing response for ID: {response.Id}");
                                _responseQueue.EnqueueResponse(response);
                            }
                        }
                    }
                    else
                    {
                        // No client connected, discard response — signal HandleClient so it doesn't wait out the timeout
                        ModLogger.Debug($"No client connected (stream null: {_clientStream == null}, CanWrite: {_clientStream?.CanWrite ?? false}, Connected: {_connectedClient?.Connected ?? false}), discarding response for ID: {response.Id}");
                        if (_pendingRequests.TryGetValue(response.Id, out var discardTcs))
                            discardTcs.TrySetResult(false);
                    }
                }
                else
                {
                    // No responses available, wait a bit
                    await Task.Delay(10, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Error in response loop: {ex.Message}");
                ModLogger.Debug($"Exception type: {ex.GetType().Name}");
                ModLogger.Debug($"Stack trace: {ex}");
                if (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(100, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
            }
        }
        ModLogger.Debug("ResponseLoop ended");
    }

    /// <summary>
    /// Heartbeat loop that sends periodic heartbeat messages to keep the connection alive.
    /// </summary>
    private async Task HeartbeatLoop(System.Threading.CancellationToken cancellationToken)
    {
        ModLogger.Debug("HeartbeatLoop started");
        const int heartbeatIntervalSeconds = 60;
        
        while (_isRunning && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(heartbeatIntervalSeconds), cancellationToken);
                
                if (!_isRunning || cancellationToken.IsCancellationRequested)
                    break;
                
                // Check if we have a connected client
                if (_clientStream == null || !_clientStream.CanWrite || _connectedClient?.Connected != true)
                {
                    ModLogger.Debug("HeartbeatLoop: No connected client, skipping heartbeat");
                    continue;
                }
                
                try
                {
                    // Generate heartbeat request ID
                    int heartbeatId;
                    lock (_heartbeatIdLock)
                    {
                        _heartbeatRequestId++;
                        heartbeatId = _heartbeatRequestId;
                    }
                    
                    ModLogger.Debug($"HeartbeatLoop: Sending server heartbeat (ID: {heartbeatId})");
                    
                    // Create heartbeat response (server-initiated, sent as notification-style response)
                    // The client will receive this but won't need to respond since it's not tied to a request
                    var heartbeatResponse = new Response
                    {
                        Id = -heartbeatId, // Negative ID indicates server-initiated (no pending request entry)
                        Result = new Dictionary<string, object>
                        {
                            ["type"] = "server_heartbeat",
                            ["status"] = "alive",
                            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        },
                        Error = null
                    };
                    
                    // Use semaphore to prevent concurrent read/write operations
                    await _streamSemaphore.WaitAsync(cancellationToken);
                    try
                    {
                        // Check connection again inside semaphore
                        if (_clientStream == null || !_clientStream.CanWrite || _connectedClient?.Connected != true)
                        {
                            ModLogger.Debug("HeartbeatLoop: Stream disconnected while preparing heartbeat");
                            continue;
                        }
                        
                        // Send heartbeat response (server-initiated)
                        string jsonResponse = ProtocolHandler.SerializeResponse(heartbeatResponse);
                        await ProtocolHandler.WriteMessageAsync(_clientStream, jsonResponse);
                        ModLogger.Debug($"HeartbeatLoop: Server heartbeat sent successfully (ID: {-heartbeatId})");
                    }
                    finally
                    {
                        _streamSemaphore.Release();
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.Debug($"HeartbeatLoop: Error sending heartbeat: {ex.Message}");
                    // Don't break the loop on heartbeat errors - connection might recover
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (_isRunning)
                {
                    ModLogger.Debug($"HeartbeatLoop: Error in heartbeat loop: {ex.Message}");
                    try
                    {
                        await Task.Delay(1000, cancellationToken); // Wait a bit before retrying
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }
        ModLogger.Debug("HeartbeatLoop ended");
    }

    private void CompletePendingRequests(bool success)
    {
        foreach (var requestId in _pendingRequests.Keys)
        {
            if (_pendingRequests.TryRemove(requestId, out var tcs))
            {
                tcs.TrySetResult(success);
            }
        }
    }

    private static void WaitForTaskCompletion(Task? task, string taskName)
    {
        if (task == null)
        {
            return;
        }

        try
        {
            task.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"{taskName} stopped with {ex.GetType().Name}: {ex.Message}");
        }
    }
}

