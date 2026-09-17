using System.Net;
using System.Net.Sockets;
using Tavern.Net.Online;

namespace Tavern.Net.Tests;

public class GameConnectionTests
{
    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public async Task HostAndJoin_SendAsync_DeliversMessageIntact()
    {
        using var host = new GameConnection();
        using var guest = new GameConnection();
        var port = GetFreePort();

        var hostTask = host.HostAsync(port);
        await guest.JoinAsync("127.0.0.1", port);
        await hostTask;

        OnlineMessage? received = null;
        var receivedTcs = new TaskCompletionSource();
        guest.MessageReceived += message =>
        {
            received = message;
            receivedTcs.TrySetResult();
        };

        await host.SendAsync(new OnlineMessage { Kind = OnlineMessageKind.Hello, PlayerName = "Alex" });
        await Task.WhenAny(receivedTcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.NotNull(received);
        Assert.Equal(OnlineMessageKind.Hello, received!.Kind);
        Assert.Equal("Alex", received.PlayerName);
    }

    [Fact]
    public async Task HostAndJoin_MultipleSendsInARow_EachArrivesUncorrupted()
    {
        using var host = new GameConnection();
        using var guest = new GameConnection();
        var port = GetFreePort();

        var hostTask = host.HostAsync(port);
        await guest.JoinAsync("127.0.0.1", port);
        await hostTask;

        var received = new List<OnlineMessage>();
        var allReceivedTcs = new TaskCompletionSource();
        guest.MessageReceived += message =>
        {
            received.Add(message);
            if (received.Count == 3)
            {
                allReceivedTcs.TrySetResult();
            }
        };

        // Fire concurrently, unawaited between calls — exercises SendAsync's internal send lock,
        // which exists precisely so overlapping writes (e.g. the broadcast timer racing an explicit
        // send) can never interleave on the wire.
        var send1 = host.SendAsync(new OnlineMessage { Kind = OnlineMessageKind.DiceRoll, Die1 = 1, Die2 = 1 });
        var send2 = host.SendAsync(new OnlineMessage { Kind = OnlineMessageKind.DiceRoll, Die1 = 3, Die2 = 4 });
        var send3 = host.SendAsync(new OnlineMessage { Kind = OnlineMessageKind.DiceRoll, Die1 = 6, Die2 = 6 });
        await Task.WhenAll(send1, send2, send3);

        await Task.WhenAny(allReceivedTcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Equal(3, received.Count);
        Assert.Equal(new[] { 2, 7, 12 }, received.Select(m => m.Die1 + m.Die2));
    }

    [Fact]
    public async Task Dispose_OnOneSide_RaisesDisconnectedOnTheOther()
    {
        using var host = new GameConnection();
        var guest = new GameConnection();
        var port = GetFreePort();

        var hostTask = host.HostAsync(port);
        await guest.JoinAsync("127.0.0.1", port);
        await hostTask;

        var disconnectedTcs = new TaskCompletionSource();
        host.Disconnected += () => disconnectedTcs.TrySetResult();

        guest.Dispose();
        await Task.WhenAny(disconnectedTcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.True(disconnectedTcs.Task.IsCompletedSuccessfully);
    }
}
