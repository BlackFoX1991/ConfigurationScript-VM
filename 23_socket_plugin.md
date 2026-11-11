# TCP and UDP Socket Plugin

Build the solution and import `CFGS.Net.Sockets.dll` to use `net.tcp`, `net.udp`, and their `std.net` aliases:

```cfs
import "dist/Debug/net10.0/CFGS.Net.Sockets.dll";
```

The plugin uses the .NET socket library already included in .NET 10. It needs no native .NET import or fixed path to `System.Net.Sockets.dll`. An embedding host must grant `AllowPluginLoading` and `AllowNetworkIO`. Both are denied by default in `CfgsScriptEngine`; the CLI's bare VM allows network access by default. `AllowNativeInterop` is a separate capability: if granted, scripts can access .NET networking directly, regardless of this plugin's gate.

## TCP

- `await net.tcp.connect(host, port, timeoutMs = 30000)` returns a connection handle.
- `net.tcp.listen(bindAddress, port)` starts a listener and returns a listener handle. Use `0` to choose an available port.
- `await listener.accept(timeoutMs = 30000)` returns a connection handle.
- `listener.port()` returns the bound port. `listener.close()` stops accepting.
- `await connection.read(maxBytes = 4096, timeoutMs = 30000)` returns an array of byte values. It returns **up to** `maxBytes` bytes, possibly fewer, and `[]` at end of stream. It does not preserve message boundaries.
- `await connection.write(data, timeoutMs = 30000)` writes all bytes and returns the byte count.
- `connection.close()` releases the socket. Closing again is safe.

`data` can be a byte array (`[0, 65, 255]`) or a string, which is encoded as UTF-8. A single read is limited to 1 MiB. Ports must be from 1 to 65535, except listener port `0`. Timeouts must be from 1 to 300000 milliseconds. A timeout or socket failure raises an error.

```cfs
import "dist/Debug/net10.0/CFGS.Net.Sockets.dll";

var connection = await net.tcp.connect("127.0.0.1", 9000);
await connection.write("ping");
var bytes = await connection.read(4096);
connection.close();
print(bytes);
```

For a protocol with messages, the script must add its own framing (for example, a length prefix or delimiter) and keep reading until the complete message arrives.

## UDP

- `net.udp.bind(bindAddress, port)` returns a UDP handle. Use `0` for an available local port.
- `await socket.send_to(data, host, port, timeoutMs = 30000)` sends one datagram and returns its byte count.
- `await socket.receive(timeoutMs = 30000)` receives one datagram as `{"data": [...], "length": n, "host": senderIp, "port": senderPort}`.
- `socket.port()` returns the bound local port. `socket.close()` releases the socket.

`bindAddress` must be a numeric IPv4/IPv6 address, `localhost`, or `*` (IPv4 wildcard). `send_to` accepts a DNS host name with the same address family as the bound socket. A datagram is limited to 65507 bytes. UDP preserves datagram boundaries but does not guarantee delivery, ordering, or duplicate suppression.

```cfs
import "dist/Debug/net10.0/CFGS.Net.Sockets.dll";

var socket = net.udp.bind("127.0.0.1", 0);
await socket.send_to([65, 66], "127.0.0.1", socket.port());
var packet = await socket.receive();
print(packet["data"]);   # [65, 66]
socket.close();
```

Both protocols require the surrounding network/firewall to allow the requested connection or bind. The plugin does not add TLS; use the HTTP plugin for HTTP/HTTPS. For a custom encrypted TCP protocol, TLS would require a separate API extension.
