using System.Collections;
using MelonLoader;
using S1MCPServer.Core;
using S1MCPServer.Helpers;
using S1MCPServer.Models;
using S1MCPServer.Server;
using S1MCPServer.Utils;
using UnityEngine;
#if MONO
using FishNet;
#else
using Il2CppFishNet;
#endif

[assembly: MelonInfo(
    typeof(S1MCPServer.S1MCPServer),
    S1MCPServer.BuildInfo.Name,
    S1MCPServer.BuildInfo.Version,
    S1MCPServer.BuildInfo.Author
)]
[assembly: MelonColor(1, 255, 0, 0)]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace S1MCPServer;

public static class BuildInfo
{
    public const string Name = "S1MCPServer";
    public const string Description = "MCP Server for Schedule I - Enables agentic LLM access to game objects";
    public const string Author = "SirTidez";
    public const string Version = "1.0.0";
}

public class S1MCPServer : MelonMod
{
    private CommandQueue? _commandQueue;
    private ResponseQueue? _responseQueue;
    private TcpServer? _tcpServer;
    private CommandRouter? _commandRouter;

    public override void OnInitializeMelon()
    {
        ModLogger.Info("S1MCPServer initialized");

        // Initialize TypeResolver (scans assemblies for Component types)
        TypeResolver.Initialize();

        // Initialize queues
        _commandQueue = new CommandQueue();
        _responseQueue = new ResponseQueue();

        // Initialize command router (will be set up in Phase 2)
        _commandRouter = new CommandRouter(_responseQueue);

        // Initialize and start TCP server
        _tcpServer = new TcpServer(_commandQueue, _responseQueue, port: 8765);
        _tcpServer.Start();

        ModLogger.Info("S1MCPServer ready - waiting for Main scene");
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        ModLogger.Debug($"Scene loaded: {sceneName}");
        if (sceneName == "Main")
        {
            ModLogger.Info("Main scene loaded - S1MCPServer is active");
        }
    }

    public override void OnUpdate()
    {
        // Process commands on main thread (Unity requirement)
        if (_commandQueue != null && _commandRouter != null)
        {
            int queueSize = _commandQueue.Count;
            if (queueSize > 0)
            {
                ModLogger.Debug($"OnUpdate: Command queue has {queueSize} command(s) waiting");
            }
            
            int processedCount = 0;
            while (_commandQueue.TryDequeue(out Request? request) && request != null)
            {
                processedCount++;
                try
                {
                    ModLogger.Debug($"OnUpdate: Processing command #{processedCount}: {request.Method} (ID: {request.Id})");
                    _commandRouter.RouteCommand(request);
                    ModLogger.Debug($"OnUpdate: Command {request.Method} (ID: {request.Id}) routed successfully");
                }
                catch (Exception ex)
                {
                    ModLogger.Error($"OnUpdate: Error processing command {request.Method} (ID: {request.Id}): {ex.Message}");
                    ModLogger.Debug($"OnUpdate: Exception type: {ex.GetType().Name}");
                    ModLogger.Debug($"OnUpdate: Stack trace: {ex}");

                    // Send error response
                    var errorResponse = ProtocolHandler.CreateErrorResponse(
                        request.Id,
                        -32603, // Internal error
                        "Internal error processing command",
                        new { details = ex.Message }
                    );
                    _responseQueue?.EnqueueResponse(errorResponse);
                    ModLogger.Debug($"OnUpdate: Enqueued error response for ID: {request.Id}");
                }
            }
            if (processedCount > 0)
            {
                ModLogger.Debug($"OnUpdate: Processed {processedCount} command(s) this frame");
            }
        }
    }

    public override void OnApplicationQuit()
    {
        ModLogger.Info("Shutting down S1MCPServer...");
        _tcpServer?.Stop();
        _commandQueue?.Clear();
        _responseQueue?.Clear();
    }
}