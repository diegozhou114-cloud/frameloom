using System.Net.WebSockets;
using System.Text.Json;
using ZWebGLFrameWork.SimulationCore.MiniSiege;

namespace MiniSiegeHost;

/// <summary>Runs two real socket clients and compares every frame against independent simulations.</summary>
internal static class SiegeVerifier
{
    internal static async Task RunAsync(string[] args)
    {
        string Option(string name, string fallback)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }
        var url = Option("--server", "ws://127.0.0.1:8787/siege");
        var directory = Option("--evidence", "artifacts/mini-siege");
        Directory.CreateDirectory(directory);
        var offline = VerifyOffline();
        Console.WriteLine($"Offline replay: {offline.Tick} frames, winner={offline.Winner}, checksum={offline.Checksum}");
        if (args.Contains("--offline-only"))
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "offline-verification.json"), JsonSerializer.Serialize(offline));
            return;
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var results = await Task.WhenAll(PlayAsync(url, timeout.Token), PlayAsync(url, timeout.Token));
        if (results[0].Tick != results[1].Tick || results[0].Checksum != results[1].Checksum || results[0].Winner != results[1].Winner)
            throw new InvalidDataException("Final states disagree.");
        var evidence = JsonSerializer.Serialize(new { passed = true, offline, clients = results, utc = DateTime.UtcNow }, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(directory, "verification.json"), evidence);
        Console.WriteLine(evidence);
    }

    private static VerificationResult VerifyOffline()
    {
        var first = new SiegeSimulation(12345);
        var replay = new SiegeSimulation(12345);
        var sequence = 0;
        while (first.Winner == -1)
        {
            var inputs = new[] { new SiegeInput { Player = 0 }, new SiegeInput { Player = 1 } };
            if (first.Tick % 20 == 0)
            {
                sequence++;
                inputs[0] = new SiegeInput { Player = 0, Sequence = sequence, Unit = sequence % 3 == 0 ? 2 : 1, Lane = 0 };
                inputs[1] = new SiegeInput { Player = 1, Sequence = sequence, Unit = sequence % 8 == 0 ? 3 : 2, Lane = sequence % 2 };
            }
            first.Step(inputs);
            replay.Step(inputs);
            if (first.Checksum != replay.Checksum) throw new InvalidDataException("Offline deterministic replay diverged.");
        }
        return new VerificationResult(-1, first.Tick, first.Winner, first.Checksum.ToString("X16"), first.Tick, Array.Empty<string>());
    }

    private static async Task<VerificationResult> PlayAsync(string url, CancellationToken cancellation)
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(url), cancellation);
        SiegeSimulation? simulation = null;
        var player = -1;
        var sequence = 0;
        var verified = 0;
        var trace = new List<string>();
        while (true)
        {
            var packet = await ReceiveAsync(socket, cancellation);
            if (packet.Kind == "welcome") player = packet.Player;
            if (packet.Kind == "start") simulation = new SiegeSimulation(packet.Seed);
            if (packet.Kind is "error" or "left") throw new InvalidDataException(packet.Message);
            if (packet.Kind != "frame") continue;
            if (simulation == null || packet.Tick != simulation.Tick) throw new InvalidDataException("Frame order mismatch.");
            simulation.Step(packet.Inputs);
            var checksum = simulation.Checksum.ToString("X16");
            if (checksum != packet.Checksum) throw new InvalidDataException($"Checksum mismatch at tick {packet.Tick}.");
            verified++;
            if (packet.Tick % 20 == 0)
            {
                trace.Add($"{packet.Tick}:{checksum}");
                await SendAsync(socket, new SiegePacket { Kind = "checksum", Tick = packet.Tick, Checksum = checksum }, cancellation);
                // Player zero attacks one lane; player one alternates, producing an asymmetric game.
                await SendAsync(socket, new SiegePacket { Kind = "input", Input = new SiegeInput
                {
                    Player = 99, Sequence = ++sequence, Unit = player == 0 ? 1 : 2,
                    Lane = player == 0 ? 0 : sequence % 2
                } }, cancellation);
            }
            if (simulation.Winner != -1)
                return new VerificationResult(player, simulation.Tick, simulation.Winner, checksum, verified, trace.ToArray());
        }
    }

    private static async Task SendAsync(ClientWebSocket socket, SiegePacket packet, CancellationToken cancellation)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(packet, SiegePeer.Json);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellation);
    }

    private static async Task<SiegePacket> ReceiveAsync(ClientWebSocket socket, CancellationToken cancellation)
    {
        var bytes = new byte[65536];
        var count = 0;
        WebSocketReceiveResult result;
        do
        {
            if (count == bytes.Length) throw new InvalidDataException("Oversized server frame.");
            result = await socket.ReceiveAsync(new ArraySegment<byte>(bytes, count, bytes.Length - count), cancellation);
            if (result.MessageType != WebSocketMessageType.Text) throw new InvalidDataException("Connection closed before game ended.");
            count += result.Count;
        } while (!result.EndOfMessage);
        return JsonSerializer.Deserialize<SiegePacket>(bytes.AsSpan(0, count), SiegePeer.Json)
            ?? throw new InvalidDataException("Invalid server packet.");
    }

    private sealed record VerificationResult(int Player, int Tick, int Winner, string Checksum, int VerifiedFrames, string[] Trace);
}
