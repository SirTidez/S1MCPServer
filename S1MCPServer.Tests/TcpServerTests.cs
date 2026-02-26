using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using S1MCPServer.Core;
using S1MCPServer.Models;
using S1MCPServer.Server;

namespace S1MCPServer.Tests;

public class TcpServerTests
{
    [Fact]
    public void Stop_CleansUpLifecycleState()
    {
        var server = new TcpServer(new CommandQueue(), new ResponseQueue(), GetFreePort());

        server.Start();
        Thread.Sleep(100);
        server.Stop();

        Assert.Null(GetPrivateField<Task>(server, "_serverTask"));
        Assert.Null(GetPrivateField<Task>(server, "_responseTask"));
        Assert.Null(GetPrivateField<Task>(server, "_heartbeatTask"));
        Assert.Null(GetPrivateField<CancellationTokenSource>(server, "_lifecycleCts"));
    }

    [Fact(Timeout = 30000)]
    public async Task DelayedResponse_AfterTimeout_IsStillDelivered()
    {
        var commandQueue = new CommandQueue();
        var responseQueue = new ResponseQueue();
        var server = new TcpServer(commandQueue, responseQueue, GetFreePort());

        server.Start();

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, server.Port);
        using var stream = client.GetStream();

        try
        {
            var request = new Request
            {
                Id = 42,
                Method = "slow_test",
                Params = new Dictionary<string, object>()
            };

            await ProtocolHandler.WriteMessageAsync(stream, ProtocolHandler.SerializeRequest(request));

            var queued = await WaitForConditionAsync(() => commandQueue.Count > 0, TimeSpan.FromSeconds(2));
            Assert.True(queued);

            await Task.Delay(TimeSpan.FromSeconds(11));

            responseQueue.EnqueueResponse(new Response
            {
                Id = 42,
                Result = new Dictionary<string, object> { ["ok"] = true },
                Error = null
            });

            var responseJson = await ProtocolHandler.ReadMessageAsync(stream).WaitAsync(TimeSpan.FromSeconds(4));
            using var doc = JsonDocument.Parse(responseJson);
            Assert.Equal(42, doc.RootElement.GetProperty("id").GetInt32());
            Assert.True(doc.RootElement.GetProperty("result").GetProperty("ok").GetBoolean());
        }
        finally
        {
            server.Stop();
        }
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static T? GetPrivateField<T>(object instance, string fieldName) where T : class
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(instance) as T;
    }

    private static async Task<bool> WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(25);
        }

        return condition();
    }
}
