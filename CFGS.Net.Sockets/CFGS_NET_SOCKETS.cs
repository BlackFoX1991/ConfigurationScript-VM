using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using CFGS_VM.VMCore;
using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Plugin;

namespace CFGS.Net.Sockets;

/// <summary>TCP streams and UDP datagrams for CFGS scripts.</summary>
public sealed class CFGS_NET_SOCKETS : IVmPlugin
{
    private const int DefaultTimeoutMs = 30_000;
    private const int MaxTimeoutMs = 300_000;
    private const int MaxReadBytes = 1024 * 1024;
    private const int MaxUdpBytes = 65_507;

    public void Register(IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics, INamespaceRegistry namespaces)
    {
        (string MemberName, BuiltinDescriptor Descriptor)[] tcp =
        [
            ("connect", new BuiltinDescriptor("net_tcp_connect", 2, 3, (args, instr) =>
            {
                VM? vm = VM.CurrentVm;
                EnsureNetworkIo(vm, instr);
                string host = RequiredHost(args[0], instr);
                int port = Port(args[1], instr, allowZero: false);
                int timeout = Timeout(args, 2, instr);
                return ConnectAsync(host, port, timeout, vm, CurrentToken());
            }, NativeCallOptions.Awaitable)),
            ("listen", new BuiltinDescriptor("net_tcp_listen", 2, 2, (args, instr) =>
            {
                VM? vm = VM.CurrentVm;
                EnsureNetworkIo(vm, instr);
                IPAddress address = BindAddress(args[0], instr);
                int port = Port(args[1], instr, allowZero: true);
                TcpListener listener = new(address, port);
                listener.Start();
                return new TcpListenerHandle(listener, vm);
            }))
        ];

        (string MemberName, BuiltinDescriptor Descriptor)[] udp =
        [
            ("bind", new BuiltinDescriptor("net_udp_bind", 2, 2, (args, instr) =>
            {
                VM? vm = VM.CurrentVm;
                EnsureNetworkIo(vm, instr);
                IPAddress address = BindAddress(args[0], instr);
                int port = Port(args[1], instr, allowZero: true);
                UdpClient client = new(address.AddressFamily);
                try
                {
                    client.Client.Bind(new IPEndPoint(address, port));
                    return new UdpHandle(client, vm);
                }
                catch
                {
                    client.Dispose();
                    throw;
                }
            }))
        ];

        namespaces.Register(NamespaceDescriptor.FromDescriptors("net.tcp", tcp));
        namespaces.Register(NamespaceDescriptor.FromDescriptors("std.net.tcp", tcp));
        namespaces.Register(NamespaceDescriptor.FromDescriptors("net.udp", udp));
        namespaces.Register(NamespaceDescriptor.FromDescriptors("std.net.udp", udp));

        intrinsics.Register(typeof(TcpConnectionHandle), new IntrinsicDescriptor("read", 0, 2, (recv, args, instr) =>
        {
            TcpConnectionHandle handle = (TcpConnectionHandle)recv;
            EnsureNetworkIo(handle.Owner, instr);
            int count = args.Count > 0 ? PositiveInt(args[0], 1, MaxReadBytes, "read size", instr) : 4096;
            return handle.ReadAsync(count, Timeout(args, 1, instr), CurrentToken());
        }, NativeCallOptions.Awaitable));
        intrinsics.Register(typeof(TcpConnectionHandle), new IntrinsicDescriptor("write", 1, 2, (recv, args, instr) =>
        {
            TcpConnectionHandle handle = (TcpConnectionHandle)recv;
            EnsureNetworkIo(handle.Owner, instr);
            return handle.WriteAsync(Bytes(args[0], instr), Timeout(args, 1, instr), CurrentToken());
        }, NativeCallOptions.Awaitable));
        intrinsics.Register(typeof(TcpConnectionHandle), new IntrinsicDescriptor("close", 0, 0, (recv, args, instr) =>
        {
            ((TcpConnectionHandle)recv).Dispose();
            return 1;
        }));

        intrinsics.Register(typeof(TcpListenerHandle), new IntrinsicDescriptor("accept", 0, 1, (recv, args, instr) =>
        {
            TcpListenerHandle handle = (TcpListenerHandle)recv;
            EnsureNetworkIo(handle.Owner, instr);
            return handle.AcceptAsync(Timeout(args, 0, instr), CurrentToken());
        }, NativeCallOptions.Awaitable));
        intrinsics.Register(typeof(TcpListenerHandle), new IntrinsicDescriptor("port", 0, 0, (recv, args, instr) =>
            ((TcpListenerHandle)recv).Port));
        intrinsics.Register(typeof(TcpListenerHandle), new IntrinsicDescriptor("close", 0, 0, (recv, args, instr) =>
        {
            ((TcpListenerHandle)recv).Dispose();
            return 1;
        }));

        intrinsics.Register(typeof(UdpHandle), new IntrinsicDescriptor("send_to", 3, 4, (recv, args, instr) =>
        {
            UdpHandle handle = (UdpHandle)recv;
            EnsureNetworkIo(handle.Owner, instr);
            byte[] data = Bytes(args[0], instr);
            if (data.Length > MaxUdpBytes)
                throw Error(instr, $"UDP datagram exceeds {MaxUdpBytes} bytes");
            string host = RequiredHost(args[1], instr);
            int port = Port(args[2], instr, allowZero: false);
            return handle.SendToAsync(data, host, port, Timeout(args, 3, instr), CurrentToken());
        }, NativeCallOptions.Awaitable));
        intrinsics.Register(typeof(UdpHandle), new IntrinsicDescriptor("receive", 0, 1, (recv, args, instr) =>
        {
            UdpHandle handle = (UdpHandle)recv;
            EnsureNetworkIo(handle.Owner, instr);
            return handle.ReceiveAsync(Timeout(args, 0, instr), CurrentToken());
        }, NativeCallOptions.Awaitable));
        intrinsics.Register(typeof(UdpHandle), new IntrinsicDescriptor("port", 0, 0, (recv, args, instr) =>
            ((UdpHandle)recv).Port));
        intrinsics.Register(typeof(UdpHandle), new IntrinsicDescriptor("close", 0, 0, (recv, args, instr) =>
        {
            ((UdpHandle)recv).Dispose();
            return 1;
        }));
    }

    private static async Task<object?> ConnectAsync(string host, int port, int timeoutMs, VM? vm, CancellationToken executionToken)
    {
        TcpClient client = new();
        try
        {
            using CancellationTokenSource timeout = TimeoutSource(timeoutMs, executionToken);
            await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
            return new TcpConnectionHandle(client, vm);
        }
        catch (OperationCanceledException) when (!executionToken.IsCancellationRequested)
        {
            client.Dispose();
            throw TimedOut("TCP connect", timeoutMs);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static CancellationToken CurrentToken() => VM.CurrentVm?.ActiveCancellationToken ?? default;

    private static CancellationTokenSource TimeoutSource(int timeoutMs, CancellationToken executionToken)
    {
        CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(executionToken);
        source.CancelAfter(timeoutMs);
        return source;
    }

    private static TimeoutException TimedOut(string operation, int timeoutMs)
        => new($"{operation} timed out after {timeoutMs} ms");

    private static void EnsureNetworkIo(VM? vm, Instruction instr)
    {
        if (!(vm?.AllowNetworkIO ?? true))
            throw Error(instr, "network I/O is disabled (AllowNetworkIO=false)");
    }

    private static VMException Error(Instruction instr, string message)
        => new($"Runtime error: {message}", instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);

    private static string RequiredHost(object? value, Instruction instr)
    {
        string host = value?.ToString()?.Trim() ?? string.Empty;
        if (host.Length == 0)
            throw Error(instr, "host must not be empty");
        return host;
    }

    private static IPAddress BindAddress(object? value, Instruction instr)
    {
        string host = RequiredHost(value, instr);
        if (host is "*" or "any") return IPAddress.Any;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return IPAddress.Loopback;
        if (IPAddress.TryParse(host, out IPAddress? address)) return address;
        throw Error(instr, "bind address must be an IP address, localhost, or *");
    }

    private static int Port(object? value, Instruction instr, bool allowZero)
        => PositiveInt(value, allowZero ? 0 : 1, 65535, "port", instr);

    private static int Timeout(List<object> args, int index, Instruction instr)
        => index < args.Count ? PositiveInt(args[index], 1, MaxTimeoutMs, "timeoutMs", instr) : DefaultTimeoutMs;

    private static int PositiveInt(object? value, int min, int max, string name, Instruction instr)
    {
        if (!int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int result) || result < min || result > max)
            throw Error(instr, $"{name} must be an integer between {min} and {max}");
        return result;
    }

    private static byte[] Bytes(object? value, Instruction instr)
    {
        if (value is string text) return Encoding.UTF8.GetBytes(text);
        if (value is byte[] raw) return raw;
        if (value is not List<object> items)
            throw Error(instr, "data must be a string or an array of bytes");

        byte[] result = new byte[items.Count];
        for (int i = 0; i < items.Count; i++)
            result[i] = (byte)PositiveInt(items[i], 0, 255, $"data[{i}]", instr);
        return result;
    }

    private static List<object> VmBytes(byte[] data)
        => data.Select(static b => (object)(int)b).ToList();

    public sealed class TcpConnectionHandle(TcpClient client, VM? owner) : IDisposable
    {
        private readonly SemaphoreSlim _readGate = new(1, 1);
        private readonly SemaphoreSlim _writeGate = new(1, 1);
        private int _closed;
        internal VM? Owner { get; } = owner;

        public async Task<object?> ReadAsync(int count, int timeoutMs, CancellationToken executionToken = default)
        {
            try
            {
                using CancellationTokenSource timeout = TimeoutSource(timeoutMs, executionToken);
                await _readGate.WaitAsync(timeout.Token).ConfigureAwait(false);
                try
                {
                    ThrowIfClosed();
                    byte[] buffer = new byte[count];
                    int read = await client.GetStream().ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
                    return VmBytes(buffer.AsSpan(0, read).ToArray());
                }
                finally { _readGate.Release(); }
            }
            catch (OperationCanceledException) when (!executionToken.IsCancellationRequested)
            {
                throw TimedOut("TCP read", timeoutMs);
            }
        }

        public async Task<object?> WriteAsync(byte[] data, int timeoutMs, CancellationToken executionToken = default)
        {
            try
            {
                using CancellationTokenSource timeout = TimeoutSource(timeoutMs, executionToken);
                await _writeGate.WaitAsync(timeout.Token).ConfigureAwait(false);
                try
                {
                    ThrowIfClosed();
                    await client.GetStream().WriteAsync(data, timeout.Token).ConfigureAwait(false);
                    return data.Length;
                }
                finally { _writeGate.Release(); }
            }
            catch (OperationCanceledException) when (!executionToken.IsCancellationRequested)
            {
                throw TimedOut("TCP write", timeoutMs);
            }
        }

        private void ThrowIfClosed()
        {
            if (Volatile.Read(ref _closed) != 0) throw new ObjectDisposedException(nameof(TcpConnectionHandle));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 0) client.Dispose();
        }
    }

    public sealed class TcpListenerHandle(TcpListener listener, VM? owner) : IDisposable
    {
        private int _closed;
        internal VM? Owner { get; } = owner;
        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

        public async Task<object?> AcceptAsync(int timeoutMs, CancellationToken executionToken = default)
        {
            if (Volatile.Read(ref _closed) != 0) throw new ObjectDisposedException(nameof(TcpListenerHandle));
            using CancellationTokenSource timeout = TimeoutSource(timeoutMs, executionToken);
            try
            {
                TcpClient client = await listener.AcceptTcpClientAsync(timeout.Token).ConfigureAwait(false);
                return new TcpConnectionHandle(client, Owner);
            }
            catch (OperationCanceledException) when (!executionToken.IsCancellationRequested)
            {
                throw TimedOut("TCP accept", timeoutMs);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 0) listener.Stop();
        }
    }

    public sealed class UdpHandle(UdpClient client, VM? owner) : IDisposable
    {
        private readonly SemaphoreSlim _receiveGate = new(1, 1);
        private int _closed;
        internal VM? Owner { get; } = owner;
        public int Port => ((IPEndPoint)client.Client.LocalEndPoint!).Port;

        public async Task<object?> SendToAsync(byte[] data, string host, int port, int timeoutMs, CancellationToken executionToken = default)
        {
            if (Volatile.Read(ref _closed) != 0) throw new ObjectDisposedException(nameof(UdpHandle));
            using CancellationTokenSource timeout = TimeoutSource(timeoutMs, executionToken);
            try
            {
                IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, timeout.Token).ConfigureAwait(false);
                AddressFamily family = client.Client.AddressFamily;
                IPAddress address = addresses.FirstOrDefault(a => a.AddressFamily == family)
                    ?? throw new SocketException((int)SocketError.AddressFamilyNotSupported);
                return await client.SendAsync(data.AsMemory(), new IPEndPoint(address, port), timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!executionToken.IsCancellationRequested)
            {
                throw TimedOut("UDP send", timeoutMs);
            }
        }

        public async Task<object?> ReceiveAsync(int timeoutMs, CancellationToken executionToken = default)
        {
            try
            {
                using CancellationTokenSource timeout = TimeoutSource(timeoutMs, executionToken);
                await _receiveGate.WaitAsync(timeout.Token).ConfigureAwait(false);
                try
                {
                    if (Volatile.Read(ref _closed) != 0) throw new ObjectDisposedException(nameof(UdpHandle));
                    UdpReceiveResult received = await client.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                    return new Dictionary<string, object>
                    {
                        ["data"] = VmBytes(received.Buffer),
                        ["length"] = received.Buffer.Length,
                        ["host"] = received.RemoteEndPoint.Address.ToString(),
                        ["port"] = received.RemoteEndPoint.Port
                    };
                }
                finally { _receiveGate.Release(); }
            }
            catch (OperationCanceledException) when (!executionToken.IsCancellationRequested)
            {
                throw TimedOut("UDP receive", timeoutMs);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 0) client.Dispose();
        }
    }
}
