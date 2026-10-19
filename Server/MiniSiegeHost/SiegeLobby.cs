using System.Net.WebSockets;
using ZWebGLFrameWork.SimulationCore.MiniSiege;

namespace MiniSiegeHost;

/// <summary>Pairs two connections and broadcasts authoritative ordered input frames.</summary>
internal sealed class SiegeLobby
{
    private readonly object gate = new();
    private readonly List<SiegePeer> peers = new();
    private readonly Dictionary<int, string> checksums = new();
    private readonly CancellationTokenSource lifetime = new();
    private SiegeSimulation? simulation;
    private string matchId = Guid.NewGuid().ToString("N");
    private int mismatches;

    internal SiegeLobby() => _ = TickLoopAsync();
    internal void Stop() => lifetime.Cancel();
    internal object Status()
    {
        lock (gate) return new { connections = peers.Count, tick = simulation?.Tick ?? 0, matchId, mismatches };
    }

    internal async Task ConnectAsync(WebSocket socket, CancellationToken cancellation)
    {
        var peer = new SiegePeer(socket, cancellation);
        lock (gate)
        {
            if (peers.Count == 2)
            {
                peer.Lifetime.Dispose();
                return;
            }
            peer.Player = peers.Count;
            peers.Add(peer);
            peer.Send(new SiegePacket { Kind = "welcome", Player = peer.Player, MatchId = matchId });
            if (peers.Count == 2)
            {
                var seed = Random.Shared.Next(1, int.MaxValue);
                simulation = new SiegeSimulation(seed);
                foreach (var member in peers)
                    member.Send(new SiegePacket { Kind = "start", Seed = seed, MatchId = matchId });
            }
        }
        var sending = peer.SendLoopAsync();
        try
        {
            while (!peer.Lifetime.IsCancellationRequested)
            {
                var packet = await peer.ReceiveAsync();
                if (packet == null) break;
                lock (gate) Accept(peer, packet);
            }
        }
        catch (Exception error) when (error is WebSocketException or OperationCanceledException or InvalidDataException or System.Text.Json.JsonException)
        {
            Console.WriteLine($"Player {peer.Player} disconnected: {error.Message}");
        }
        finally
        {
            lock (gate)
            {
                if (peer.Active)
                {
                    foreach (var member in peers)
                    {
                        member.Active = false;
                        if (member != peer) member.Send(new SiegePacket { Kind = "left", Message = "对手已断开，请重新连接开始新对局" });
                    }
                    peers.Clear();
                    checksums.Clear();
                    simulation = null;
                    matchId = Guid.NewGuid().ToString("N");
                }
            }
            peer.Lifetime.Cancel();
            try { await sending; }
            catch (Exception error) when (error is OperationCanceledException or WebSocketException) { }
            peer.Lifetime.Dispose();
        }
    }

    private void Accept(SiegePeer peer, SiegePacket packet)
    {
        if (!peer.Active) return;
        if (packet.Kind == "checksum")
        {
            if (checksums.TryGetValue(packet.Tick, out var expected) && packet.Checksum != expected)
            {
                mismatches++;
                Console.Error.WriteLine($"DESYNC match={matchId} player={peer.Player} tick={packet.Tick} expected={expected} actual={packet.Checksum}");
            }
            return;
        }
        if (packet.Kind != "input" || simulation == null || simulation.Winner != -1) return;
        var input = packet.Input;
        if (input == null || input.Sequence <= peer.Sequence || input.Unit < 1 || input.Unit > 3 || input.Lane < 0 || input.Lane > 1 || peer.Inputs.Count >= 8)
        {
            peer.Send(new SiegePacket { Kind = "rejected", Message = "操作被拒绝：无效、过期或队列已满" });
            return;
        }
        input.Player = peer.Player;
        peer.Sequence = input.Sequence;
        peer.Inputs.Enqueue(input);
    }

    private async Task TickLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        try
        {
            while (await timer.WaitForNextTickAsync(lifetime.Token))
            {
                lock (gate)
                {
                    if (peers.Count != 2 || simulation == null || simulation.Winner != -1) continue;
                    var tick = simulation.Tick;
                    var inputs = peers.Select(peer => peer.Inputs.TryDequeue(out var input) ? input : new SiegeInput { Player = peer.Player }).ToArray();
                    simulation.Step(inputs);
                    var checksum = simulation.Checksum.ToString("X16");
                    checksums[tick] = checksum;
                    checksums.Remove(tick - 400);
                    var frame = new SiegePacket { Kind = "frame", MatchId = matchId, Tick = tick, Inputs = inputs, Checksum = checksum };
                    foreach (var peer in peers) peer.Send(frame);
                    if (simulation.Winner != -1) Console.WriteLine($"Match {matchId} finished: winner={simulation.Winner} ticks={simulation.Tick}");
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Simulation loop failed: {error}");
            lock (gate)
                foreach (var peer in peers)
                    peer.Send(new SiegePacket { Kind = "error", Message = "权威模拟失败，请重启服务器" });
        }
    }
}
