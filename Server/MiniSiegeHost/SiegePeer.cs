using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using ZWebGLFrameWork.SimulationCore.MiniSiege;

namespace MiniSiegeHost;

/// <summary>One bounded, ordered outbound stream per connected player.</summary>
internal sealed class SiegePeer
{
    internal static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
    private readonly Channel<SiegePacket> outbound = Channel.CreateBounded<SiegePacket>(128);
    private readonly WebSocket socket;
    internal readonly CancellationTokenSource Lifetime;
    internal readonly Queue<SiegeInput> Inputs = new();
    internal int Player;
    internal int Sequence;
    internal bool Active = true;

    internal SiegePeer(WebSocket socket, CancellationToken cancellation)
    {
        this.socket = socket;
        Lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
    }

    internal void Send(SiegePacket packet)
    {
        if (!outbound.Writer.TryWrite(packet)) Lifetime.Cancel();
    }

    internal async Task SendLoopAsync()
    {
        await foreach (var packet in outbound.Reader.ReadAllAsync(Lifetime.Token))
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(packet, Json);
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, Lifetime.Token);
        }
    }

    internal async Task<SiegePacket?> ReceiveAsync()
    {
        var buffer = new byte[4096];
        var count = 0;
        WebSocketReceiveResult result;
        do
        {
            if (count == buffer.Length) throw new InvalidDataException("Packet exceeds 4096 bytes.");
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, count, buffer.Length - count), Lifetime.Token);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text) throw new InvalidDataException("Text packets required.");
            count += result.Count;
        } while (!result.EndOfMessage);
        return JsonSerializer.Deserialize<SiegePacket>(buffer.AsSpan(0, count), Json)
            ?? throw new InvalidDataException("Empty packet.");
    }
}
