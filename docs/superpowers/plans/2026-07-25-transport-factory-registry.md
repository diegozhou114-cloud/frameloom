# Transport Factory Registry Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the single-slot realtime factory registry with a deterministic `RealtimeTransport`-keyed router that supports WebSocket and NativeKcp concurrently.

**Architecture:** `RealtimeChannelFactoryRegistry.Current` remains a stable `IRealtimeChannelFactory`. Concrete assemblies register one transport key each; the Runtime assembly owns the synchronized dictionary and invokes the selected concrete factory after releasing the registry lock.

**Tech Stack:** Unity 2022.3.62f3c1, C#, NUnit, Unity Test Framework

## Global Constraints

- Runtime references Contracts only; it must not reference WebGL, Native, NativeWebSocket, or kcp2k.
- Native registers only `RealtimeTransport.NativeKcp`; WebGL later registers only `RealtimeTransport.WebSocket`.
- Null, duplicate, and missing registrations fail deterministically.
- `ResetForTests` clears registrations while `Current` remains the same router object.
- Tests and logs write beneath `/tmp/zweb-network-server-evidence/network/registry`.

---

### Task 1: Add the transport-keyed router

**Files:**
- Create: `Assets/Tests/Network/RealtimeChannelFactoryRegistryTests.cs`
- Create: `Assets/Tests/Network/RealtimeChannelFactoryRegistryTests.cs.meta`
- Modify: `Assets/Scripts/Network/Runtime/RealtimeChannelFactoryRegistry.cs`
- Modify: `Assets/Scripts/Network/Native/NativeRealtimeChannelFactory.cs`

**Interfaces:**
- Consumes: `RealtimeTransport`, `IRealtimeChannelFactory`, and `IRealtimeChannel`.
- Produces: `RealtimeChannelFactoryRegistry.Install(RealtimeTransport, IRealtimeChannelFactory)` and stable `RealtimeChannelFactoryRegistry.Current` routing.

- [ ] **Step 1: Write a runtime RED test for the required API**

Create `RealtimeChannelFactoryRegistryTests.cs` with a first test that uses reflection so the current project still compiles and the test fails as an assertion rather than a compiler error:

```csharp
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZWebGLFrameWork.Network.Contracts.Protocol;
using ZWebGLFrameWork.Network.Contracts.Transport;
using ZWebGLFrameWork.Network.Runtime;

namespace ZWebGLFrameWork.Network.Tests
{
    public sealed class RealtimeChannelFactoryRegistryTests
    {
        [SetUp]
        public void SetUp() => RealtimeChannelFactoryRegistry.ResetForTests();

        [TearDown]
        public void TearDown() => RealtimeChannelFactoryRegistry.ResetForTests();

        [Test]
        public void Registry_ExposesTransportKeyedInstall()
        {
            MethodInfo install = typeof(RealtimeChannelFactoryRegistry).GetMethod(
                "Install", new[] { typeof(RealtimeTransport), typeof(IRealtimeChannelFactory) });

            Assert.That(install, Is.Not.Null);
        }
    }
}
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```bash
mkdir -p /tmp/zweb-network-server-evidence/network/registry
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform EditMode \
  -testFilter ZWebGLFrameWork.Network.Tests.RealtimeChannelFactoryRegistryTests.Registry_ExposesTransportKeyedInstall \
  -testResults /tmp/zweb-network-server-evidence/network/registry/api-red.xml \
  -logFile /tmp/zweb-network-server-evidence/network/registry/api-red.log
```

Expected: one executed test fails because the two-parameter `Install` method is absent.

- [ ] **Step 3: Add the minimal keyed method signature**

Add the overload without removing the old method yet:

```csharp
public static void Install(RealtimeTransport transport, IRealtimeChannelFactory factory)
{
    if (factory == null) throw new ArgumentNullException(nameof(factory));
    throw new NotSupportedException($"Transport registration is not implemented: {transport}.");
}
```

- [ ] **Step 4: Run the API test and verify GREEN**

Run the Step 2 command with `api-green.xml` and `api-green.log`.

Expected: one test passes.

- [ ] **Step 5: Write behavior RED tests**

Extend the fixture with these tests and fakes:

```csharp
[Test]
public void Registry_RoutesBothTransportsWithoutOverwrite()
{
    var webSocketChannel = new StubChannel();
    var nativeChannel = new StubChannel();
    var webSocketFactory = new StubFactory(RealtimeTransport.WebSocket, webSocketChannel);
    var nativeFactory = new StubFactory(RealtimeTransport.NativeKcp, nativeChannel);

    RealtimeChannelFactoryRegistry.Install(RealtimeTransport.NativeKcp, nativeFactory);
    RealtimeChannelFactoryRegistry.Install(RealtimeTransport.WebSocket, webSocketFactory);

    Assert.That(RealtimeChannelFactoryRegistry.Current.Create(RealtimeTransport.NativeKcp),
        Is.SameAs(nativeChannel));
    Assert.That(RealtimeChannelFactoryRegistry.Current.Create(RealtimeTransport.WebSocket),
        Is.SameAs(webSocketChannel));
    Assert.That(nativeFactory.CreateCount, Is.EqualTo(1));
    Assert.That(webSocketFactory.CreateCount, Is.EqualTo(1));
}

[Test]
public void Registry_RejectsNullDuplicateAndMissingRegistrations()
{
    var factory = new StubFactory(RealtimeTransport.NativeKcp, new StubChannel());

    Assert.Throws<ArgumentNullException>(() =>
        RealtimeChannelFactoryRegistry.Install(RealtimeTransport.NativeKcp, null));
    RealtimeChannelFactoryRegistry.Install(RealtimeTransport.NativeKcp, factory);
    Assert.Throws<InvalidOperationException>(() =>
        RealtimeChannelFactoryRegistry.Install(RealtimeTransport.NativeKcp, factory));
    Assert.Throws<InvalidOperationException>(() =>
        RealtimeChannelFactoryRegistry.Current.Create(RealtimeTransport.WebSocket));
}

[Test]
public void Registry_ResetClearsEntriesButKeepsStableRouter()
{
    IRealtimeChannelFactory router = RealtimeChannelFactoryRegistry.Current;
    RealtimeChannelFactoryRegistry.Install(RealtimeTransport.NativeKcp,
        new StubFactory(RealtimeTransport.NativeKcp, new StubChannel()));

    RealtimeChannelFactoryRegistry.ResetForTests();

    Assert.That(RealtimeChannelFactoryRegistry.Current, Is.SameAs(router));
    Assert.Throws<InvalidOperationException>(() =>
        router.Create(RealtimeTransport.NativeKcp));
}

[Test]
public async Task ConnectionController_ConsumesStableRegistryRouter()
{
    var channel = new StubChannel();
    RealtimeChannelFactoryRegistry.Install(
        RealtimeTransport.NativeKcp,
        new StubFactory(RealtimeTransport.NativeKcp, channel));
    IRealtimeChannelFactory router = RealtimeChannelFactoryRegistry.Current;
    var controller = new RealtimeConnectionController(router);

    await controller.ConnectAsync(
        new RealtimeEndpoint(
            RealtimeTransport.NativeKcp, new Uri("udp://127.0.0.1:8888")),
        new RealtimeCredentials
        {
            SessionId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            OpaqueTicket = new byte[] { 1 },
            ResumeToken = new byte[] { 2 },
            MacKey = new byte[32],
            ExpiresAtUnixSeconds = long.MaxValue
        },
        CancellationToken.None);

    Assert.That(controller.Current, Is.SameAs(channel));
    Assert.That(RealtimeChannelFactoryRegistry.Current, Is.SameAs(router));
}

private sealed class StubFactory : IRealtimeChannelFactory
{
    private readonly RealtimeTransport _expected;
    private readonly IRealtimeChannel _channel;
    public int CreateCount { get; private set; }

    public StubFactory(RealtimeTransport expected, IRealtimeChannel channel)
    {
        _expected = expected;
        _channel = channel;
    }

    public IRealtimeChannel Create(RealtimeTransport transport)
    {
        Assert.That(transport, Is.EqualTo(_expected));
        CreateCount++;
        return _channel;
    }
}

private sealed class StubChannel : IRealtimeChannel
{
    public RealtimeChannelState State => RealtimeChannelState.Disconnected;
    public Task ConnectAsync(RealtimeEndpoint endpoint, RealtimeCredentials credentials,
        CancellationToken cancellationToken) => Task.CompletedTask;
    public Task SendAsync(byte[] completeMessage, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public Task<byte[]> ReceiveAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Array.Empty<byte>());
    public Task CloseAsync(RealtimeCloseReason reason, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public void Dispose() { }
}
```

- [ ] **Step 6: Run the fixture and verify behavior RED**

Run the Step 2 command with the fixture filter and `behavior-red.xml` / `behavior-red.log`.

Expected: API-presence test passes; routing, controller-consumption, and reset
tests fail because keyed registration throws `NotSupportedException`.

- [ ] **Step 7: Implement the synchronized router**

Replace `RealtimeChannelFactoryRegistry.cs` with:

```csharp
using System;
using System.Collections.Generic;
using ZWebGLFrameWork.Network.Contracts.Protocol;
using ZWebGLFrameWork.Network.Contracts.Transport;

namespace ZWebGLFrameWork.Network.Runtime
{
    public static class RealtimeChannelFactoryRegistry
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<RealtimeTransport, IRealtimeChannelFactory> Factories =
            new Dictionary<RealtimeTransport, IRealtimeChannelFactory>();
        private static readonly IRealtimeChannelFactory Router = new RegisteredFactoryRouter();

        public static IRealtimeChannelFactory Current => Router;

        public static void Install(
            RealtimeTransport transport, IRealtimeChannelFactory factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (!Enum.IsDefined(typeof(RealtimeTransport), transport))
                throw new ArgumentOutOfRangeException(nameof(transport));
            lock (Gate)
            {
                if (Factories.ContainsKey(transport))
                    throw new InvalidOperationException(
                        $"A realtime factory is already registered for {transport}.");
                Factories.Add(transport, factory);
            }
        }

        public static void ResetForTests()
        {
            lock (Gate) Factories.Clear();
        }

        private sealed class RegisteredFactoryRouter : IRealtimeChannelFactory
        {
            public IRealtimeChannel Create(RealtimeTransport transport)
            {
                IRealtimeChannelFactory factory;
                lock (Gate)
                {
                    if (!Factories.TryGetValue(transport, out factory))
                        throw new InvalidOperationException(
                            $"No realtime factory is registered for {transport}.");
                }
                return factory.Create(transport);
            }
        }
    }
}
```

Update the native bootstrap call:

```csharp
RealtimeChannelFactoryRegistry.Install(
    RealtimeTransport.NativeKcp, new NativeRealtimeChannelFactory());
```

Remove the obsolete one-parameter `Install` method.

- [ ] **Step 8: Run focused and broad GREEN gates**

Run the fixture command with `behavior-green.xml` / `behavior-green.log`, then run:

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform EditMode \
  -testFilter ZWebGLFrameWork.Network.Tests \
  -testResults /tmp/zweb-network-server-evidence/network/registry/network-green.xml \
  -logFile /tmp/zweb-network-server-evidence/network/registry/network-green.log
```

Expected: registry fixture passes; full Network suite passes with nonzero tests and no errors.

- [ ] **Step 9: Verify assembly boundaries and clean diff**

Run:

```bash
rg -n "Network\.WebGL|Network\.Native|NativeWebSocket|Kcp2k" \
  Assets/Scripts/Network/Runtime --glob '*.cs' --glob '*.asmdef'
git diff --check
```

Expected: the Runtime search has no matches; `git diff --check` exits 0.

- [ ] **Step 10: Commit**

```bash
git add Assets/Scripts/Network/Runtime/RealtimeChannelFactoryRegistry.cs \
  Assets/Scripts/Network/Native/NativeRealtimeChannelFactory.cs \
  Assets/Tests/Network/RealtimeChannelFactoryRegistryTests.cs \
  Assets/Tests/Network/RealtimeChannelFactoryRegistryTests.cs.meta
git commit -m "fix(network): route realtime factories by transport"
```
