# Sockets, QUIC, DNS, NetworkInformation

Raw socket APIs (`Socket`, `TcpClient`/`Listener`, `UdpClient`, UDS), QUIC streams, DNS, `IPNetwork`, NIC enumeration, ping. Load when working below HTTP at the transport layer.

## DNS

```csharp
IPHostEntry e  = await Dns.GetHostEntryAsync("host.contoso.com");
IPAddress[] xs = await Dns.GetHostAddressesAsync("contoso.com");
string name    = Dns.GetHostName();
```

## Raw `Socket`

```csharp
using Socket client = new(ipEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
await client.ConnectAsync(ipEndPoint);

await client.SendAsync(Encoding.UTF8.GetBytes("hi<|EOM|>"), SocketFlags.None);
var buf = new byte[1024];
int n = await client.ReceiveAsync(buf, SocketFlags.None);
client.Shutdown(SocketShutdown.Both);
```

Async patterns:
- `Task` / `ValueTask`-returning: `ConnectAsync`, `AcceptAsync`, `ReceiveAsync`, `SendAsync`. `Memory<byte>` / `ReadOnlyMemory<byte>` overloads avoid `byte[]` allocations.
- `SocketAsyncEventArgs` (high-perf, allocation-free): pre-allocate args, set `Buffer`/`SetBuffer`, `RemoteEndPoint`, hook `Completed`, use returned `bool` to decide sync-vs-async.
- `CancellationToken` on every modern overload.

```csharp
var saea = new SocketAsyncEventArgs();
saea.SetBuffer(new byte[4096], 0, 4096);
saea.Completed += (_, e) => { /* dispatch */ };
if (!socket.ReceiveAsync(saea)) { /* completed synchronously */ }
```

## `TcpClient` / `TcpListener`

Wrap `Socket` + `NetworkStream`. Recommended for simple stream-oriented code; drop to `Socket` for granular control.

```csharp
// client
using TcpClient client = new();
await client.ConnectAsync(ipEndPoint);
await using NetworkStream s = client.GetStream();
int n = await s.ReadAsync(buf);

// listener
TcpListener listener = new(new IPEndPoint(IPAddress.Any, 13));
listener.Start();
using TcpClient handler = await listener.AcceptTcpClientAsync();
await using NetworkStream s = handler.GetStream();
await s.WriteAsync(Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("o")));
listener.Stop();
```

`TcpClient()` default ctor → dual-stack (IPv6 if supported, IPv4 fallback). `TcpClient(AddressFamily)` accepts `InterNetwork`/`InterNetworkV6`/`Unknown` only. `TcpListener.Start(int backlog)` = `Bind` + `Listen(backlog)`. `NetworkStream` does **not** own the underlying socket from `TcpClient.GetStream()` — closing the stream does not close the socket.

## UDP

```csharp
using var udp = new UdpClient(ipEndPoint);
byte[] msg = Encoding.UTF8.GetBytes("ping");
await udp.SendAsync(msg, msg.Length, "host", 4000);

UdpReceiveResult result = await udp.ReceiveAsync();
string text = Encoding.UTF8.GetString(result.Buffer);

// Multicast
udp.JoinMulticastGroup(IPAddress.Parse("239.0.0.1"));
udp.MulticastLoopback = false;

// Broadcast
udp.EnableBroadcast = true;
await udp.SendAsync(data, data.Length, IPAddress.Broadcast, 9000);
```

## Unix domain sockets

```csharp
var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
await s.ConnectAsync(new UnixDomainSocketEndPoint("/var/run/api.sock"));
```

## QUIC (`System.Net.Quic`)

Public APIs since .NET 7 (preview), stable since .NET 9. Backed by **MsQuic**.

| Platform | Status |
|---|---|
| Windows 11 / Server 2022+ | `msquic.dll` ships with runtime |
| Linux | install `libmsquic` ≥ 2.2 from `packages.microsoft.com` |
| macOS | partial via `brew install libmsquic`; set `DYLD_FALLBACK_LIBRARY_PATH=$(brew --prefix)/lib:$DYLD_FALLBACK_LIBRARY_PATH` |

Capability check:

```csharp
if (!QuicListener.IsSupported)   { /* fallback */ }
if (!QuicConnection.IsSupported) { /* fallback */ }
```

### Listener (server)

```csharp
var serverConnectionOptions = new QuicServerConnectionOptions
{
    DefaultStreamErrorCode = 0x0A,
    DefaultCloseErrorCode  = 0x0B,
    ServerAuthenticationOptions = new SslServerAuthenticationOptions
    {
        ApplicationProtocols = [new SslApplicationProtocol("protocol-name")],
        ServerCertificate    = serverCertificate
    }
};

var listener = await QuicListener.ListenAsync(new QuicListenerOptions
{
    ListenEndPoint           = new IPEndPoint(IPAddress.Loopback, 0),
    ApplicationProtocols     = [new SslApplicationProtocol("protocol-name")],
    ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(serverConnectionOptions)
});

while (running)
    var connection = await listener.AcceptConnectionAsync();
await listener.DisposeAsync();
```

### Connection (client)

```csharp
var clientConnectionOptions = new QuicClientConnectionOptions
{
    RemoteEndPoint                  = listener.LocalEndPoint,
    DefaultStreamErrorCode          = 0x0A,
    DefaultCloseErrorCode           = 0x0B,
    MaxInboundUnidirectionalStreams = 10,
    MaxInboundBidirectionalStreams  = 100,
    ClientAuthenticationOptions     = new SslClientAuthenticationOptions
    {
        ApplicationProtocols = [new SslApplicationProtocol("protocol-name")],
        TargetHost           = "server.example"
    }
};

var connection = await QuicConnection.ConnectAsync(clientConnectionOptions);

var outgoing = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional);
var incoming = await connection.AcceptInboundStreamAsync();

await connection.CloseAsync(0x0C);
await connection.DisposeAsync();
```

### `QuicStream`

Inherits `Stream`. Two flavors: bidirectional / unidirectional.

| Method | Opener | Acceptor |
|---|---|---|
| `CanRead` | bi: true / uni: false | true |
| `CanWrite` | true | bi: true / uni: false |
| `CompleteWrites` | half-close → peer reads 0 | bi: half-close / uni: no-op |
| `Abort(Read)` | bi: STOP_SENDING / uni: no-op | STOP_SENDING |
| `Abort(Write)` | RESET_STREAM | bi: RESET_STREAM / uni: no-op |

`ReadsClosed` / `WritesClosed` (`Task` completes on side closure). `DisposeAsync` aborts unread reads but **gracefully closes writes** (like `CompleteWrites`).

```csharp
await using var stream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, ct);
await stream.WriteAsync(data, ct);
await stream.WriteAsync(data, completeWrites: true, ct);    // last chunk + half-close
while (await stream.ReadAsync(buffer, ct) > 0) { /* ... */ }
```

Caveat: opening a stream does not send data; the peer doesn't see it until first `WriteAsync`. `AcceptInboundStreamAsync()` hangs until first byte.

## DNS, IPNetwork, NetworkInformation

```csharp
// IPNetwork (.NET 8+)
var net = IPNetwork.Parse("10.0.0.0/8");
bool inside = net.Contains(IPAddress.Parse("10.1.2.3"));

// IPAddress helpers
IPAddress.MapToIPv6(); /* MapToIPv4(); IsIPv4MappedToIPv6; */

// NetworkInterface enumeration
foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
{
    if (nic.OperationalStatus != OperationalStatus.Up) continue;
    foreach (var ua in nic.GetIPProperties().UnicastAddresses)
        Console.WriteLine($"{nic.Name}: {ua.Address}");
}

// Ping
using var ping = new Ping();
PingReply r = await ping.SendPingAsync("contoso.com", TimeSpan.FromSeconds(2));
```
