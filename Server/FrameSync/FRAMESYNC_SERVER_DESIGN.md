# FrameSync Server Design

## Overview

The server runs an authoritative `SimOrchestrator` that:
1. Collects inputs from both players
2. Executes authoritative simulation ticks
3. Computes checksums
4. Broadcasts both players' inputs + authority checksum back to clients

## Architecture

```
Client A ←── UDP/KCP ──→ Server (Authoritative) ←── UDP/KCP ──→ Client B
    │                         │                               │
    │  1. Send Input(tick)    │                               │
    │────────────────────────→│                               │
    │                         │  2. Collect both inputs       │
    │                         │  3. SimOrchestrator.Tick()    │
    │                         │  4. ComputeChecksum()         │
    │                         │                               │
    │  5. Recv AuthorityData   │                               │
    │←────────────────────────│──────────────────────────────→│
    │  {inputs_A[], inputs_B[], checksum, tick}               │
    │                         │                               │
    │  6. Compare checksum    │                               │
    │  ├─ match → continue    │                               │
    │  └─ mismatch → rollback │                               │
```

## Server Core

```csharp
public class FrameSyncServer
{
    private SimWorld _world;
    private SimOrchestrator _orchestrator;
    private InputReceiver _receiverA, _receiverB;
    private int _currentTick;

    public void Tick()
    {
        // 1. Drain inputs from both players
        var inputsA = _receiverA.DrainReadyInputs();
        var inputsB = _receiverB.DrainReadyInputs();

        // 2. Both players' inputs for this tick must be available
        if (!BothHaveInput(inputsA, inputsB, _currentTick))
            return; // Wait for inputs

        // 3. Feed into orchestrator
        foreach (var input in inputsA)
            _buffer.AddCommand(input.ToCommand(0));
        foreach (var input in inputsB)
            _buffer.AddCommand(input.ToCommand(1));

        // 4. Execute authoritative tick
        _orchestrator.Tick();

        // 5. Broadcast authority data
        Broadcast(new AuthorityData
        {
            Tick = _currentTick,
            InputsA = inputsA,
            InputsB = inputsB,
            Checksum = _orchestrator.LastChecksum,
        });

        _currentTick++;
    }
}
```

## Transport

- Use KCP (KcpCore) for reliable-ordered UDP
- Each player has a dedicated KCP session
- InputPacket serialized to bytes → KcpCore.Send()
- Server runs KcpCore.Update() per session each frame

## Optimization: Input Redundancy

- Input packets redundantly include un-acked inputs
- Server acks the highest sequence number it has processed
- Clients discard inputs older than server's ack
- This eliminates retransmission stalls (same as Fiedler lockstep)

## Rollback on Server

- Server does NOT rollback — it's authoritative
- Only clients rollback when their prediction doesn't match server checksum
- Server stores snapshots only for debugging/replay, not for runtime rollback

## Deployment

- .NET 8 console application
- References SimulationCore source via `<Compile Include>` (same as ReplayValidator)
- Can run on Linux/Windows server
- Single-threaded game loop (20 Tick/s), sufficient for strategy game
