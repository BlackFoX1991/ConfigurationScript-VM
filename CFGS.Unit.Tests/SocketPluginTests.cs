using System.Net;
using System.Net.Sockets;
using CFGS_VM.Hosting;
using Xunit;

namespace CFGS.Unit.Tests;

public sealed class SocketPluginTests
{
    private static CfgsScriptEngine CreateEngine(bool allowNetwork = true)
    {
        CfgsScriptEngine engine = new();
        engine.Permissions.AllowPluginLoading = true;
        engine.Permissions.AllowNetworkIO = allowNetwork;
        engine.LoadPlugin(Path.Combine(AppContext.BaseDirectory, "CFGS.Net.Sockets.dll"));
        return engine;
    }

    [Fact]
    public async Task TcpClientReadsPartialDataAndEndOfStream()
    {
        using TcpListener server = new(IPAddress.Loopback, 0);
        server.Start();
        int port = ((IPEndPoint)server.LocalEndpoint).Port;
        Task serverTask = Task.Run(async () =>
        {
            using TcpClient accepted = await server.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            byte[] request = new byte[1];
            Assert.Equal(1, await accepted.GetStream().ReadAsync(request, TestContext.Current.CancellationToken));
            Assert.Equal(65, request[0]);
            await accepted.GetStream().WriteAsync(new byte[] { 66 }, TestContext.Current.CancellationToken);
        }, TestContext.Current.CancellationToken);

        CfgsScript script = CreateEngine().CompileSource("<socket-tcp-client>", $$"""
var written = 0;
var first = [];
var eof = [];
async func main() {
    var connection = await net.tcp.connect("127.0.0.1", {{port}});
    written = await connection.write([65]);
    first = await connection.read(4);
    eof = await connection.read(4);
    connection.close();
}
""");
        await script.RunAsync(TestContext.Current.CancellationToken);
        await script.InvokeAsync("main", null, TestContext.Current.CancellationToken);
        await serverTask;

        Assert.Equal(1, script.GetGlobal<int>("written"));
        Assert.Equal([66], Bytes(script.GetGlobal("first")));
        Assert.Empty(Bytes(script.GetGlobal("eof")));
    }

    [Fact]
    public async Task TcpListenerAcceptsAndReplies()
    {
        CfgsScript script = CreateEngine().CompileSource("<socket-tcp-listener>", """
var listener = net.tcp.listen("127.0.0.1", 0);
var port = listener.port();
var received = [];
async func main() {
    var connection = await listener.accept();
    received = await connection.read(1);
    await connection.write([77]);
    connection.close();
    listener.close();
}
""");
        await script.RunAsync(TestContext.Current.CancellationToken);
        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, script.GetGlobal<int>("port"), TestContext.Current.CancellationToken);
        await client.GetStream().WriteAsync(new byte[] { 76 }, TestContext.Current.CancellationToken);

        await script.InvokeAsync("main", null, TestContext.Current.CancellationToken);
        byte[] response = new byte[1];
        Assert.Equal(1, await client.GetStream().ReadAsync(response, TestContext.Current.CancellationToken));
        Assert.Equal(77, response[0]);
        Assert.Equal([76], Bytes(script.GetGlobal("received")));
    }

    [Fact]
    public async Task UdpPreservesDatagramAndSenderEndpoint()
    {
        CfgsScript script = CreateEngine().CompileSource("<socket-udp>", """
var sender = std.net.udp.bind("127.0.0.1", 0);
var receiver = net.udp.bind("127.0.0.1", 0);
var sent = 0;
var packet = null;
async func main() {
    sent = await sender.send_to([0, 65, 255], "127.0.0.1", receiver.port());
    packet = await receiver.receive();
    sender.close();
    receiver.close();
}
""");
        await script.RunAsync(TestContext.Current.CancellationToken);
        await script.InvokeAsync("main", null, TestContext.Current.CancellationToken);

        Assert.Equal(3, script.GetGlobal<int>("sent"));
        Dictionary<string, object> packet = Assert.IsType<Dictionary<string, object>>(script.GetGlobal("packet"));
        Assert.Equal([0, 65, 255], Bytes(packet["data"]));
        Assert.Equal(3, Convert.ToInt32(packet["length"]));
        Assert.Equal("127.0.0.1", packet["host"]);
        Assert.InRange(Convert.ToInt32(packet["port"]), 1, 65535);
    }

    [Fact]
    public async Task Ipv6TcpAndUdpLoopback()
    {
        if (!Socket.OSSupportsIPv6) return;

        using TcpListener server = new(IPAddress.IPv6Loopback, 0);
        server.Start();
        int port = ((IPEndPoint)server.LocalEndpoint).Port;
        Task serverTask = Task.Run(async () =>
        {
            using TcpClient accepted = await server.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            await accepted.GetStream().WriteAsync(new byte[] { 88 }, TestContext.Current.CancellationToken);
        }, TestContext.Current.CancellationToken);

        CfgsScript script = CreateEngine().CompileSource("<socket-ipv6>", $$"""
var tcpData = [];
var udpData = null;
async func main() {
    var connection = await net.tcp.connect("::1", {{port}});
    tcpData = await connection.read(1);
    connection.close();
    var socket = net.udp.bind("::1", 0);
    await socket.send_to([89], "::1", socket.port());
    udpData = await socket.receive();
    socket.close();
}
""");
        await script.RunAsync(TestContext.Current.CancellationToken);
        await script.InvokeAsync("main", null, TestContext.Current.CancellationToken);
        await serverTask;
        Assert.Equal([88], Bytes(script.GetGlobal("tcpData")));
        Dictionary<string, object> udpData = Assert.IsType<Dictionary<string, object>>(script.GetGlobal("udpData"));
        Assert.Equal([89], Bytes(udpData["data"]));
    }

    [Fact]
    public async Task NetworkPermissionIsRecheckedOnExistingHandle()
    {
        CfgsScript script = CreateEngine().CompileSource("<socket-permission-change>", """
var socket = net.udp.bind("127.0.0.1", 0);
async func send() { return await socket.send_to([1], "127.0.0.1", socket.port()); }
func close() { socket.close(); }
""");
        await script.RunAsync(TestContext.Current.CancellationToken);
        script.Runtime.AllowNetworkIO = false;

        CfgsScriptException error = await Assert.ThrowsAsync<CfgsScriptException>(
            () => script.InvokeAsync("send", null, TestContext.Current.CancellationToken));
        Assert.Contains("AllowNetworkIO=false", error.Message);
        await script.InvokeAsync("close", null, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SocketTimeoutIsCatchableAndHostCancellationPropagates()
    {
        CfgsScript script = CreateEngine().CompileSource("<socket-timeout>", """
var socket = net.udp.bind("127.0.0.1", 0);
var caught = false;
async func wait() {
    try { await socket.receive(30); }
    catch (error) { caught = true; }
    socket.close();
}
""");
        await script.RunAsync(TestContext.Current.CancellationToken);
        await script.InvokeAsync("wait", null, TestContext.Current.CancellationToken);
        Assert.True(script.GetGlobal<bool>("caught"));

        CfgsScriptEngine timedEngine = CreateEngine();
        timedEngine.Permissions.MaxExecutionTime = TimeSpan.FromMilliseconds(250);
        CfgsScript timedScript = timedEngine.CompileSource("<socket-host-cancel>", """
var socket = net.udp.bind("127.0.0.1", 0);
async func wait() { await socket.receive(300000); }
""");
        await timedScript.RunAsync(TestContext.Current.CancellationToken);
        CfgsScriptException canceled = await Assert.ThrowsAsync<CfgsScriptException>(
            () => timedScript.InvokeAsync("wait", null, TestContext.Current.CancellationToken));
        Assert.Equal("CFGS9100", canceled.Code);
        (timedScript.GetGlobal("socket") as IDisposable)?.Dispose();
    }

    [Fact]
    public async Task EmbeddedHostDeniesSocketsAndHttpWhenNetworkIsDisabled()
    {
        CfgsScriptEngine engine = CreateEngine(allowNetwork: false);
        engine.LoadPlugin(Path.Combine(AppContext.BaseDirectory, "CFGS.Web.Http.dll"));
        CfgsScript script = engine.CompileSource("<socket-denied>", """
func socket_op() { return net.udp.bind("127.0.0.1", 0); }
func http_op() { return http.get("http://127.0.0.1:1/"); }
""");
        await script.RunAsync(TestContext.Current.CancellationToken);

        CfgsScriptException socketError = await Assert.ThrowsAsync<CfgsScriptException>(
            () => script.InvokeAsync("socket_op", null, TestContext.Current.CancellationToken));
        CfgsScriptException httpError = await Assert.ThrowsAsync<CfgsScriptException>(
            () => script.InvokeAsync("http_op", null, TestContext.Current.CancellationToken));
        Assert.Contains("AllowNetworkIO=false", socketError.Message);
        Assert.Contains("AllowNetworkIO=false", httpError.Message);
    }

    private static int[] Bytes(object? value)
        => Assert.IsType<List<object>>(value).Select(Convert.ToInt32).ToArray();
}
