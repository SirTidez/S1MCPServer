using System.IO.Pipes;
using System.Text;
using S1MCPServer.Core;
using S1MCPServer.Models;
using S1MCPServer.Utils;

namespace S1MCPServer.Server;

/// <summary>
/// Named Pipe server that handles communication with the MCP server.
/// Runs on a background thread and marshals commands to the main thread via CommandQueue.
/// </summary>
public class NamedPipeServer
{
    private readonly CommandQueue _commandQueue;
    private readonly ResponseQueue _responseQueue;
    private NamedPipeServerStream? _pipeServer;
    private bool _isRunning;
    private Task? _serverTask;
    private Task? _responseTask;
    private readonly System.Threading.SemaphoreSlim _pipeSemaphore = new System.Threading.SemaphoreSlim(1, 1); // Semaphore for pipe operations

    private const string PipeName = "S1MCPServer";

    public NamedPipeServer(CommandQueue commandQueue, ResponseQueue responseQueue)
    {
        _commandQueue = commandQueue;
        _responseQueue = responseQueue;
    }

    /// <summary>
    /// Starts the named pipe server on a background thread.
    /// </summary>
    public void Start()
    {
        if (_isRunning)
        {
            ModLogger.Warn("Named pipe server is already running");
            return;
        }

        _isRunning = true;
        _serverTask = Task.Run(ServerLoop);
        _responseTask = Task.Run(ResponseLoop);
        ModLogger.Info($"Named pipe server started on \\\\.\\pipe\\{PipeName}");
    }

    /// <summary>
    /// Stops the named pipe server gracefully.
    /// </summary>
    public void Stop()
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;
        _pipeServer?.Dispose();
        _pipeServer = null;

        _serverTask?.Wait(TimeSpan.FromSeconds(2));
        _responseTask?.Wait(TimeSpan.FromSeconds(2));

        ModLogger.Info("Named pipe server stopped");
    }

    /// <summary>
    /// Main server loop that accepts client connections.
    /// </summary>
    private async void ServerLoop()
    {
        ModLogger.Debug("ServerLoop started");
        while (_isRunning)
        {
            try
            {
                ModLogger.Debug($"Creating named pipe server stream: {PipeName}");
                // Create and wait for connection
                _pipeServer = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    1, // Max instances
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous
                );

                ModLogger.Debug("Waiting for client connection...");
                ModLogger.Debug("Waiting for client connection...");
                await _pipeServer.WaitForConnectionAsync();
                ModLogger.Info($"Client connected to named pipe (IsConnected: {_pipeServer.IsConnected}, CanRead: {_pipeServer.CanRead}, CanWrite: {_pipeServer.CanWrite})");
                
                // Verify connection is actually established
                if (!_pipeServer.IsConnected)
                {
                    ModLogger.Error("Pipe server reports connection but IsConnected is false!");
                }

                // Handle client communication
                ModLogger.Debug("Starting client handler...");
                await HandleClient(_pipeServer);

                ModLogger.Info("Client disconnected from named pipe");
            }
            catch (Exception ex)
            {
                if (_isRunning)
                {
                    ModLogger.Error($"Pipe server error: {ex.Message}");
                    ModLogger.Debug($"Exception type: {ex.GetType().Name}");
                    ModLogger.Debug($"Stack trace: {ex}");
                }
            }
            finally
            {
                ModLogger.Debug("Cleaning up pipe server stream...");
                _pipeServer?.Dispose();
                _pipeServer = null;

                // Wait a bit before trying to reconnect
                if (_isRunning)
                {
                    ModLogger.Debug("Waiting 1 second before reconnecting...");
                    await Task.Delay(1000);
                }
            }
        }
        ModLogger.Debug("ServerLoop ended");
    }

    /// <summary>
    /// Handles communication with a connected client.
    /// </summary>
    private async Task HandleClient(NamedPipeServerStream pipe)
    {
        ModLogger.Debug($"HandleClient started (IsConnected: {pipe.IsConnected}, CanRead: {pipe.CanRead}, CanWrite: {pipe.CanWrite})");
        
        // Give the client a moment to be ready
        await Task.Delay(100);
        ModLogger.Debug($"HandleClient: After initial delay, IsConnected: {pipe.IsConnected}");
        
        while (_isRunning && pipe.IsConnected)
        {
            try
            {
                // Check connection state before reading
                if (!pipe.IsConnected)
                {
                    ModLogger.Debug("HandleClient: Pipe is no longer connected, exiting loop");
                    break;
                }
                
                ModLogger.Debug("Waiting to read message from client...");
                
                // Read request from client - use semaphore to prevent concurrent read/write
                string jsonMessage;
                await _pipeSemaphore.WaitAsync();
                try
                {
                    // Check connection before reading
                    if (!pipe.IsConnected)
                    {
                        ModLogger.Debug("HandleClient: Pipe disconnected before read");
                        break;
                    }
                    
                    jsonMessage = await ProtocolHandler.ReadMessageAsync(pipe);
                    ModLogger.Debug($"Received raw JSON message ({jsonMessage.Length} chars): {jsonMessage}");
                }
                catch (IOException ex) when (ex.Message.Contains("No data available"))
                {
                    // Client is waiting for response - this is normal in request-response pattern
                    ModLogger.Debug("HandleClient: No data available, client may be waiting for response. Waiting before next read...");
                    // Release semaphore before waiting
                    _pipeSemaphore.Release();
                    await Task.Delay(200); // Wait a bit before trying to read again
                    continue; // Continue loop to try reading again
                }
                finally
                {
                    _pipeSemaphore.Release();
                }

                // Deserialize request
                Request request;
                try
                {
                    ModLogger.Debug("Deserializing request...");
                    request = ProtocolHandler.DeserializeRequest(jsonMessage);
#if MONO
                    ModLogger.Debug($"Deserialized request: ID={request.Id}, Method={request.Method}, Params={Newtonsoft.Json.JsonConvert.SerializeObject(request.Params)}");
#else
                    ModLogger.Debug($"Deserialized request: ID={request.Id}, Method={request.Method}, Params={System.Text.Json.JsonSerializer.Serialize(request.Params)}");
#endif
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
                    await _pipeSemaphore.WaitAsync();
                    try
                    {
                        if (pipe.IsConnected)
                        {
                            await ProtocolHandler.WriteMessageAsync(pipe, ProtocolHandler.SerializeResponse(errorResponse));
                        }
                    }
                    finally
                    {
                        _pipeSemaphore.Release();
                    }
                    continue;
                }

                // Enqueue command for main thread processing
                ModLogger.Debug($"Enqueuing command: {request.Method} (ID: {request.Id})");
                _commandQueue.EnqueueCommand(request);
                ModLogger.Debug($"Command enqueued successfully. Queue size: {_commandQueue.Count}");
                
                // Wait for the response to be sent
                int initialResponseCount = _responseQueue.Count;
                int waitIterations = 0;
                const int maxWaitIterations = 200; // Max 10 seconds (200 * 50ms)
                
                while (_responseQueue.Count >= initialResponseCount && waitIterations < maxWaitIterations)
                {
                    await Task.Delay(50);
                    waitIterations++;
                }
                
                if (waitIterations >= maxWaitIterations)
                {
                    ModLogger.Warn($"Waited {maxWaitIterations * 50}ms for response to be sent for request {request.Id}");
                }
                else
                {
                    ModLogger.Debug($"Response for request {request.Id} was sent after {waitIterations * 50}ms");
                }
                
                // Now wait for acknowledgment from client before reading next message
                ModLogger.Debug($"Waiting for acknowledgment for request {request.Id}...");
                try
                {
                    await _pipeSemaphore.WaitAsync();
                    try
                    {
                        if (!pipe.IsConnected)
                        {
                            ModLogger.Debug("HandleClient: Pipe disconnected while waiting for acknowledgment");
                            break;
                        }
                        
                        // Read acknowledgment
                        string ackJson = await ProtocolHandler.ReadMessageAsync(pipe);
                        ModLogger.Debug($"Received acknowledgment: {ackJson}");
                        
                        // Deserialize acknowledgment
                        var acknowledgment = ProtocolHandler.DeserializeAcknowledgment(ackJson);
                        if (acknowledgment.Id == request.Id)
                        {
                            ModLogger.Debug($"Acknowledgment received for request {request.Id} (status: {acknowledgment.Status})");
                        }
                        else
                        {
                            ModLogger.Warn($"Acknowledgment ID mismatch: expected {request.Id}, got {acknowledgment.Id}");
                        }
                    }
                    catch (IOException ex) when (ex.Message.Contains("No data available"))
                    {
                        ModLogger.Debug("HandleClient: No acknowledgment data available, client may have disconnected");
                        break;
                    }
                    finally
                    {
                        _pipeSemaphore.Release();
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.Error($"Error reading acknowledgment for request {request.Id}: {ex.Message}");
                    ModLogger.Debug($"Exception type: {ex.GetType().Name}");
                    // Continue - don't break the connection on ack error
                }
            }
            catch (IOException ex)
            {
                // Client disconnected or stream error
                ModLogger.Debug($"Client connection lost (IOException): {ex.Message}");
                ModLogger.Debug($"Exception type: {ex.GetType().Name}");
                
                // Check if it's a "pipe broken" error during write (which is handled by ResponseLoop)
                if (ex.Message.Contains("broken") || ex.Message.Contains("EOF"))
                {
                    ModLogger.Debug("HandleClient: Pipe broken/EOF detected, client may have disconnected");
                }
                break;
            }
            catch (Exception ex)
            {
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
    private async void ResponseLoop()
    {
        ModLogger.Debug("ResponseLoop started");
        while (_isRunning)
        {
            try
            {
                // Wait for responses from main thread
                if (_responseQueue.TryDequeue(out Response? response) && response != null)
                {
                    ModLogger.Debug($"Dequeued response for request ID: {response.Id}, has_error: {response.Error != null}, has_result: {response.Result != null}");
                    
                    if (_pipeServer != null && _pipeServer.IsConnected)
                    {
                        try
                        {
                            ModLogger.Debug($"Serializing response for ID: {response.Id}...");
                            string jsonResponse = ProtocolHandler.SerializeResponse(response);
                            ModLogger.Debug($"Serialized response ({jsonResponse.Length} chars): {jsonResponse}");
                            
                            ModLogger.Debug($"Writing response to pipe for ID: {response.Id}...");
                            
                            // Use semaphore to prevent concurrent read/write operations
                            await _pipeSemaphore.WaitAsync();
                            try
                            {
                                // Check connection again inside semaphore
                                if (_pipeServer == null || !_pipeServer.IsConnected)
                                {
                                    ModLogger.Debug($"Pipe disconnected while preparing to write response for ID: {response.Id}");
                                    throw new IOException("Pipe disconnected");
                                }
                                
                                // Write response
                                await ProtocolHandler.WriteMessageAsync(_pipeServer, jsonResponse);
                            }
                            finally
                            {
                                _pipeSemaphore.Release();
                            }
                            ModLogger.Debug($"Successfully sent response for request ID: {response.Id}");
                        }
                        catch (Exception ex)
                        {
                            ModLogger.Error($"Failed to send response for ID {response.Id}: {ex.Message}");
                            ModLogger.Debug($"Exception type: {ex.GetType().Name}");
                            ModLogger.Debug($"Stack trace: {ex}");
                            
                            // Don't re-enqueue if pipe is broken/disconnected
                            if (ex.Message.Contains("broken") || ex.Message.Contains("disconnected") || ex.Message.Contains("EOF"))
                            {
                                ModLogger.Debug($"Pipe is broken/disconnected, not re-enqueuing response for ID: {response.Id}");
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
                        // No client connected, discard response
                        ModLogger.Debug($"No client connected (pipeServer null: {_pipeServer == null}, IsConnected: {_pipeServer?.IsConnected ?? false}), discarding response for ID: {response.Id}");
                    }
                }
                else
                {
                    // No responses available, wait a bit
                    await Task.Delay(10);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Error in response loop: {ex.Message}");
                ModLogger.Debug($"Exception type: {ex.GetType().Name}");
                ModLogger.Debug($"Stack trace: {ex}");
                await Task.Delay(100);
            }
        }
        ModLogger.Debug("ResponseLoop ended");
    }
}


