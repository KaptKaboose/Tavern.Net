using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows.Threading;

namespace Tavern.Net.Online;

/// <summary>
/// A direct TCP connection between two Tavern.Net instances — one hosts, the other joins. Carries
/// length-prefixed JSON <see cref="OnlineMessage"/>s in both directions and fires a periodic tick
/// the owner uses to (re)broadcast its own player state; that periodic traffic doubles as a
/// keepalive against idle NAT/router timeouts during a long turn, so there's no separate heartbeat
/// message. Reachability (same LAN, a mesh VPN like Tailscale, or a manually forwarded port) is
/// entirely up to the two players — this class just needs an address to dial.
/// </summary>
public sealed class GameConnection : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly DispatcherTimer _broadcastTimer;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private TcpListener? _listener;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _receiveCts;
    private bool _disconnected;

    public event Action<OnlineMessage>? MessageReceived;

    /// <summary>Fires roughly every 300ms once connected — the owner sends its current PlayerState
    /// on each tick; see the class's own doc comment on why this also serves as the keepalive.</summary>
    public event Action? BroadcastTick;

    public event Action? Disconnected;

    public GameConnection()
    {
        _broadcastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _broadcastTimer.Tick += (_, _) => BroadcastTick?.Invoke();
    }

    /// <summary>Starts listening on <paramref name="port"/> and returns once a guest connects.</summary>
    public async Task HostAsync(int port, CancellationToken cancellationToken = default)
    {
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        try
        {
            _client = await _listener.AcceptTcpClientAsync(cancellationToken);
        }
        finally
        {
            _listener.Stop();
        }

        _stream = _client.GetStream();
        StartReceiving();
        _broadcastTimer.Start();
    }

    /// <summary>Connects to a host previously started with <see cref="HostAsync"/>.</summary>
    public async Task JoinAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        _client = new TcpClient();
        await _client.ConnectAsync(host, port, cancellationToken);
        _stream = _client.GetStream();
        StartReceiving();
        _broadcastTimer.Start();
    }

    /// <summary>A best-guess local IPv4 address to show a host for the other player to connect to —
    /// works for LAN play out of the box; for internet play the player substitutes their own
    /// Tailscale address or forwarded public IP instead.</summary>
    public static string? GetLocalIPAddress()
    {
        foreach (var ip in Dns.GetHostAddresses(Dns.GetHostName()))
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
            {
                return ip.ToString();
            }
        }

        return null;
    }

    public async Task SendAsync(OnlineMessage message)
    {
        if (_stream is null || _disconnected)
        {
            return;
        }

        // Both the broadcast timer and an explicit UI action can each try to send at once — this
        // just serializes the actual writes so two messages never interleave on the wire.
        await _sendLock.WaitAsync();
        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
            var lengthPrefix = BitConverter.GetBytes(json.Length);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(lengthPrefix);
            }

            await _stream.WriteAsync(lengthPrefix);
            await _stream.WriteAsync(json);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            RaiseDisconnected();
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private void StartReceiving()
    {
        _receiveCts = new CancellationTokenSource();
        _ = ReceiveLoopAsync(_receiveCts.Token);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var lengthBuffer = await ReadExactAsync(4, cancellationToken);
                if (lengthBuffer is null)
                {
                    break;
                }

                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(lengthBuffer);
                }

                var length = BitConverter.ToInt32(lengthBuffer);
                var payload = await ReadExactAsync(length, cancellationToken);
                if (payload is null)
                {
                    break;
                }

                var message = JsonSerializer.Deserialize<OnlineMessage>(payload, JsonOptions);
                if (message is not null)
                {
                    MessageReceived?.Invoke(message);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            // Falls through to the disconnect check below regardless of which of these fired.
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            RaiseDisconnected();
        }
    }

    /// <summary>Reads exactly <paramref name="count"/> bytes, or null if the peer closed the
    /// connection partway through — a single ReadAsync isn't guaranteed to fill the buffer even
    /// when more data is coming, so this loops until it has everything (or the stream ends).</summary>
    private async Task<byte[]?> ReadExactAsync(int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await _stream!.ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken);
            if (read == 0)
            {
                return null;
            }

            offset += read;
        }

        return buffer;
    }

    private void RaiseDisconnected()
    {
        if (_disconnected)
        {
            return;
        }

        _disconnected = true;
        _broadcastTimer.Stop();
        Disconnected?.Invoke();
    }

    public void Dispose()
    {
        _broadcastTimer.Stop();
        _receiveCts?.Cancel();
        _stream?.Dispose();
        _client?.Dispose();
        _listener?.Stop();
    }
}
