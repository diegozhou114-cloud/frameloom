# Unity Compile Error Repair Design

## Goal

Remove the nine Unity compiler errors currently reported for the simulation,
gameplay, and network assemblies without changing public APIs or game behavior.

## Scope

- `SimAttackSystem.cs`: make the writable two-component lambda match
  `SimWorld.RefAction<SimEntity, T1, T2>` by declaring all parameter types.
- `SimMovementSystem.cs`: apply the same explicit typed `ref` lambda syntax.
- `ResourceProducer.cs`: import `System` so the existing `Math.Min` calls resolve.
- `NetworkManager.cs`: pass the `reason` and `error` arguments required by the
  `Action<string>` events, retain the connection-generation guard, and store the
  subscribed delegates so `Disconnect` removes the exact same instances.

No adjacent refactoring, API changes, new abstractions, or unrelated warning
cleanup is included.

## Data And Event Flow

Simulation systems continue mutating component columns through the existing
`RefAction` overload. Resource production retains its existing cap behavior.
Network callbacks continue to reject stale connection generations; accepted
callbacks forward their original message to `HandleDisconnected` or
`HandleConnectFailed`. Disconnecting removes every callback registered for the
current client before asking that client to disconnect.

## Verification

The current Unity editor log is the RED baseline and contains exactly the nine
reported compiler errors. After the edits, Unity must complete a fresh script
compilation with zero C# compiler errors. The final diff must remain limited to
the four production files above and this design document, apart from Unity's
pre-existing generated metadata changes.
