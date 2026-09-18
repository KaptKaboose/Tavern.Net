using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
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

    // Anything at least this big is gzipped: a full PlayerState carries every Major event's board
    // snapshot, which grows all game long (hundreds of KB of very repetitive JSON — it compresses
    // ~40:1) and is re-sent several times a second. Uncompressed, that outran ordinary upload
    // speeds and the backlog showed up as the opponent's board lagging further and further behind.
    private const int CompressionThreshold = 1024;
    private const int MaxFrameBytes = 64 * 1024 * 1024;

    // An unchanged PlayerState isn't re-sent until this long has passed — so a quiet board costs
    // nothing, while the periodic resend still acts as the keepalive and heals a peer that missed
    // an earlier one (e.g. their board wasn't open yet when it went out).
    private static readonly TimeSpan StateResendInterval = TimeSpan.FromSeconds(3);

    private readonly DispatcherTimer _broadcastTimer;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private byte[]? _lastStatePayload;
    private DateTime _lastStateSentUtc = DateTime.MinValue;
    private SynchronizationContext? _syncContext;

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

        _client.NoDelay = true;
        _stream = _client.GetStream();
        StartReceiving();
        _broadcastTimer.Start();
    }

    /// <summary>Connects to a host previously started with <see cref="HostAsync"/>.</summary>
    public async Task JoinAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        _client = new TcpClient();
        await _client.ConnectAsync(host, port, cancellationToken);
        _client.NoDelay = true;
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

        // PlayerState is a full snapshot re-sent on every tick, so only the newest matters: if the
        // previous send is still going out, drop this one instead of queueing it (a queue of stale
        // multi-hundred-KB snapshots is exactly what made the opponent lag further behind over time).
        var isState = message.Kind == OnlineMessageKind.PlayerState;
        if (isState && _sendLock.CurrentCount == 0)
        {
            return;
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        if (isState
            && _lastStatePayload is not null
            && DateTime.UtcNow - _lastStateSentUtc < StateResendInterval
            && json.AsSpan().SequenceEqual(_lastStatePayload))
        {
            return;
        }

        var frame = BuildFrame(json);

        // Both the broadcast timer and an explicit UI action can each try to send at once — this
        // just serializes the actual writes so two messages never interleave on the wire.
        await _sendLock.WaitAsync();
        try
        {
            // One write for prefix + body (with Nagle off, two small writes would be two packets).
            await _stream.WriteAsync(frame);
            if (isState)
            {
                _lastStatePayload = json;
                _lastStateSentUtc = DateTime.UtcNow;
            }
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

    /// <summary>Frame layout: 4-byte big-endian length of what follows, then one flag byte (0 = raw
    /// JSON, 1 = gzipped JSON), then the payload.</summary>
    private static byte[] BuildFrame(byte[] json)
    {
        byte[] body;
        if (json.Length >= CompressionThreshold)
        {
            var compressed = new MemoryStream();
            compressed.WriteByte(1);
            using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            {
                gzip.Write(json);
            }

            body = compressed.ToArray();
        }
        else
        {
            body = new byte[json.Length + 1];
            json.CopyTo(body, 1);
        }

        var frame = new byte[4 + body.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, body.Length);
        body.CopyTo(frame, 4);
        return frame;
    }

    private static byte[] Unwrap(byte[] body)
    {
        if (body[0] == 0)
        {
            return body[1..];
        }

        using var input = new MemoryStream(body, 1, body.Length - 1);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private void StartReceiving()
    {
        // Reads, decompression and JSON parsing happen off the UI thread; only handing a finished
        // message (or a disconnect) to subscribers is marshalled back onto it, in order.
        _syncContext = SynchronizationContext.Current;
        _receiveCts = new CancellationTokenSource();
        _ = ReceiveLoopAsync(_receiveCts.Token);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var lengthBuffer = await ReadExactAsync(4, cancellationToken).ConfigureAwait(false);
                if (lengthBuffer is null)
                {
                    break;
                }

                var length = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);
                if (length < 1 || length > MaxFrameBytes)
                {
                    break;
                }

                var body = await ReadExactAsync(length, cancellationToken).ConfigureAwait(false);
                if (body is null)
                {
                    break;
                }

                var message = JsonSerializer.Deserialize<OnlineMessage>(Unwrap(body), JsonOptions);
                if (message is not null)
                {
                    RaiseOnSubscribersThread(() => MessageReceived?.Invoke(message));
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
            var read = await _stream!.ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken).ConfigureAwait(false);
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
        RaiseOnSubscribersThread(() =>
        {
            _broadcastTimer.Stop();
            Disconnected?.Invoke();
        });
    }

    /// <summary>Runs <paramref name="action"/> on the thread that started the connection (the UI
    /// thread, in the app) — its subscribers touch UI-bound state, and DispatcherTimer only stops on
    /// its own thread. With no such context (tests) it just runs inline.</summary>
    private void RaiseOnSubscribersThread(Action action)
    {
        if (_syncContext is null || SynchronizationContext.Current == _syncContext)
        {
            action();
            return;
        }

        _syncContext.Post(_ => action(), null);
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
