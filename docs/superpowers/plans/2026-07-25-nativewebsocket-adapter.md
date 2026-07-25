# NativeWebSocket Adapter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Vendor the pinned NativeWebSocket source and add secure HTTPS guest login plus an authenticated WSS realtime channel for Editor/WebGL.

**Architecture:** An embedded package reproduces only the reviewed upstream revision. `UnityHttpApi` owns HTTPS policy and UnityWebRequest adaptation; `WebGlRealtimeChannel` owns protocol authentication and queues complete messages; `NativeWebSocketClientAdapter` is the only direct upstream consumer and owns the native long-lived connect task.

**Tech Stack:** Unity 2022.3.62f3c1, C#, NUnit, UnityWebRequest, NativeWebSocket commit `cbbf04d42522d5f5b466224a0b8e3936676f9b32`

## Global Constraints

- Archive SHA-256 is `b89e5afd349b091c7861993fa4267fa5e1423bd64fe1187936c082ff92d46538`.
- Canonical root license is Apache-2.0 and remains verbatim in `ThirdPartyNotices/NativeWebSocket-LICENSE.txt`.
- `integrations/Unity/package.json` declaring MIT is recorded as a metadata conflict and formal-project-review TODO; neither upstream fact is rewritten.
- Use the actual `src/NativeWebSocket` and `integrations/Unity` layout and upstream `colyseus.nativewebsocket` asmdef.
- Online HTTP accepts absolute `https` only; realtime accepts absolute `wss` with exact path `/v1/realtime` only.
- Authentication readiness requires `OnOpen`, AuthHello first, and a matching AuthAccepted.
- No handwritten file is created under `Assets/Plugins/WebGL`; no browser result is fabricated.
- Every created Unity asset and directory has its Unity-generated `.meta` file staged in the same commit.
- C1 remains unchanged: real source endpoint rebinding belongs to Task 13 server `KcpIngress`.
- Evidence writes beneath `/tmp/zweb-network-server-evidence/network/task-05`.

---

### Task 1: Reproduce the pinned embedded package

**Files:**
- Create: `scripts/vendor-nativewebsocket.sh`
- Create: `scripts/vendor-nativewebsocket.test.sh`
- Create at execution: `Packages/com.zwebglframework.nativewebsocket/`
- Modify: `docs/dependencies/README.md`

**Interfaces:**
- Consumes: `docs/dependencies/nativewebsocket.lock` and `ThirdPartyNotices/NativeWebSocket-LICENSE.txt`.
- Produces: deterministic embedded package with upstream `colyseus.nativewebsocket` assembly.

- [ ] **Step 1: Write the failing vendor contract test**

Create `scripts/vendor-nativewebsocket.test.sh`:

```bash
#!/usr/bin/env bash
set -euo pipefail

test -x scripts/vendor-nativewebsocket.sh
rg -q 'src/NativeWebSocket' scripts/vendor-nativewebsocket.sh
rg -q 'integrations/Unity' scripts/vendor-nativewebsocket.sh
rg -q 'SOURCE_ARCHIVE_SHA256' scripts/vendor-nativewebsocket.sh
rg -q 'LICENSE_SHA256' scripts/vendor-nativewebsocket.sh
rg -q 'colyseus.nativewebsocket' scripts/vendor-nativewebsocket.sh
rg -q 'Apache-2.0' docs/dependencies/README.md
rg -q 'integrations/Unity/package.json.*MIT' docs/dependencies/README.md
```

- [ ] **Step 2: Run the shell test and verify RED**

Run: `bash scripts/vendor-nativewebsocket.test.sh`

Expected: exit 1 because `scripts/vendor-nativewebsocket.sh` does not exist.

- [ ] **Step 3: Implement the strict vendor script**

Create an executable script with these complete operations:

```bash
#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "$0")/.." && pwd)
cd "$repo_root"
lock=docs/dependencies/nativewebsocket.lock
test -s "$lock"

expected=(UPSTREAM_URL UPSTREAM_COMMIT SOURCE_SUBTREE SOURCE_ARCHIVE_SHA256 \
  UPSTREAM_LICENSE_PATH LICENSE_SHA256 LICENSE_FILE)
values=()
index=0
while IFS= read -r line || [[ -n "$line" ]]; do
  [[ "$index" -lt "${#expected[@]}" ]]
  key=${expected[$index]}
  [[ "$line" == "$key="* ]]
  value=${line#"$key="}
  [[ "$value" =~ ^[A-Za-z0-9._:/-]+$ ]]
  values+=("$value")
  index=$((index + 1))
done < "$lock"
[[ "$index" -eq 7 ]]

UPSTREAM_URL=${values[0]}
UPSTREAM_COMMIT=${values[1]}
SOURCE_SUBTREE=${values[2]}
SOURCE_ARCHIVE_SHA256=${values[3]}
UPSTREAM_LICENSE_PATH=${values[4]}
LICENSE_SHA256=${values[5]}
LICENSE_FILE=${values[6]}
[[ "$UPSTREAM_URL" == https://github.com/endel/NativeWebSocket.git ]]
[[ "$UPSTREAM_COMMIT" == cbbf04d42522d5f5b466224a0b8e3936676f9b32 ]]
[[ "$SOURCE_SUBTREE" == . ]]
[[ "$SOURCE_ARCHIVE_SHA256" == b89e5afd349b091c7861993fa4267fa5e1423bd64fe1187936c082ff92d46538 ]]
[[ "$UPSTREAM_LICENSE_PATH" == LICENSE ]]
[[ "$(shasum -a 256 "$LICENSE_FILE" | awk '{print $1}')" == "$LICENSE_SHA256" ]]

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
git -c http.version=HTTP/1.1 clone --filter=blob:none "$UPSTREAM_URL" "$work/repo"
git -C "$work/repo" cat-file -e "$UPSTREAM_COMMIT^{commit}"
git -C "$work/repo" archive --format=tar "$UPSTREAM_COMMIT" > "$work/source.tar"
[[ "$(shasum -a 256 "$work/source.tar" | awk '{print $1}')" == "$SOURCE_ARCHIVE_SHA256" ]]
git -C "$work/repo" show "$UPSTREAM_COMMIT:$UPSTREAM_LICENSE_PATH" > "$work/LICENSE"
cmp -s "$work/LICENSE" "$LICENSE_FILE"
git -C "$work/repo" archive "$UPSTREAM_COMMIT" \
  src/NativeWebSocket integrations/Unity | tar -x -C "$work"

package=Packages/com.zwebglframework.nativewebsocket
saved_meta="$work/meta"
if [[ -d "$package" ]]; then
  (cd "$package" && find . -name '*.meta' -print | while IFS= read -r file; do
    mkdir -p "$saved_meta/$(dirname "$file")"; cp "$file" "$saved_meta/$file"; done)
fi
rm -rf "$package"
mkdir -p "$package/Runtime/Upstream/src" "$package/Runtime/Upstream/integrations"
cp -R "$work/src/NativeWebSocket" "$package/Runtime/Upstream/src/"
cp -R "$work/integrations/Unity" "$package/Runtime/Upstream/integrations/"
cp "$LICENSE_FILE" "$package/LICENSE.txt"
if [[ -d "$saved_meta" ]]; then
  (cd "$saved_meta" && find . -name '*.meta' -print | while IFS= read -r file; do
    asset=${file%.meta}; [[ -e "$repo_root/$package/$asset" ]] || continue
    mkdir -p "$repo_root/$package/$(dirname "$file")"; cp "$file" "$repo_root/$package/$file"; done)
fi
printf '%s\n' '{' \
  '  "name": "com.zwebglframework.nativewebsocket",' \
  '  "version": "0.0.0",' \
  '  "displayName": "ZWebGLFrameWork Vendored NativeWebSocket",' \
  '  "unity": "2022.3",' \
  '  "license": "SEE LICENSE IN LICENSE.txt"' \
  '}' > "$package/package.json"
rg -q '"name"[[:space:]]*:[[:space:]]*"colyseus.nativewebsocket"' \
  "$package/Runtime/Upstream/integrations/Unity"
```

Add this exact dependency note to `docs/dependencies/README.md`:

```markdown
## NativeWebSocket licensing metadata

The pinned repository root `LICENSE` is Apache-2.0 and governs this vendored
source. Upstream `integrations/Unity/package.json` declares MIT. Both facts are
preserved. TODO(formal-project-review): resolve the upstream metadata conflict
before a production legal release review; do not rewrite either upstream file.
```

- [ ] **Step 4: Run vendor and provenance GREEN checks**

Run:

```bash
chmod +x scripts/vendor-nativewebsocket.sh scripts/vendor-nativewebsocket.test.sh
bash scripts/vendor-nativewebsocket.test.sh
bash scripts/vendor-nativewebsocket.sh
bash scripts/verify-realtime-dependency-provenance.sh
```

Expected: shell test passes; vendor succeeds; provenance prints the pinned NativeWebSocket and kcp2k commits. Network access uses the host approval UI.

- [ ] **Step 5: Commit the reproducible dependency**

```bash
git add scripts/vendor-nativewebsocket.sh scripts/vendor-nativewebsocket.test.sh \
  docs/dependencies/README.md Packages/com.zwebglframework.nativewebsocket
git commit -m "build: vendor pinned NativeWebSocket source"
```

---

### Task 2: Add HTTPS guest login with strict parsing

**Files:**
- Create: `Assets/Scripts/Network/UnityHttp/ZWebGLFrameWork.Network.UnityHttp.asmdef`
- Create: `Assets/Scripts/Network/UnityHttp/UnityHttpApi.cs`
- Create: `Assets/Scripts/Network/UnityHttp/GuestLoginModels.cs`
- Create: `Assets/Tests/Network/UnityHttpApiTests.cs`
- Modify: `Assets/Tests/Network/ZWebGLFrameWork.Network.Tests.asmdef`

**Interfaces:**
- Consumes: `IHttpApi`, `HttpRequest`, `HttpResponse`, and `RealtimeCredentials`.
- Produces: `UnityHttpApi.ValidateOnlineUri(Uri)`, `GuestLoginRequest.Create(Uri)`, and `GuestLoginResult.Parse(HttpResponse)`.

- [ ] **Step 1: Add a reflection RED for the new public contract**

Create a test fixture that resolves `ZWebGLFrameWork.Network.UnityHttp.UnityHttpApi, ZWebGLFrameWork.Network.UnityHttp` and asserts the type and public static `ValidateOnlineUri(Uri)` exist. Use `Type.GetType` so the test assembly compiles before the production assembly exists.

- [ ] **Step 2: Run the reflection test and verify RED**

Run Unity EditMode with filter `ZWebGLFrameWork.Network.Tests.UnityHttpApiTests` and output `http-api-red.xml` / `http-api-red.log`.

Expected: the executed assertion fails because the assembly/type is absent.

- [ ] **Step 3: Add the assembly and minimal contract**

Create the asmdef with name `ZWebGLFrameWork.Network.UnityHttp`, references `ZWebGLFrameWork.Network.Contracts`, platforms `Editor` and `WebGL`, and `autoReferenced: false`. Add:

```csharp
public static void ValidateOnlineUri(Uri uri)
{
    if (uri == null || !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps)
        throw new NetworkPolicyException("Online HTTP endpoint must use HTTPS.");
}
```

`NetworkPolicyException` is a sealed `InvalidOperationException` in the same file with a public string constructor.

- [ ] **Step 4: Run the contract test and verify GREEN**

Run the Step 2 command with `http-api-contract-green.xml` / `.log`.

Expected: contract test passes.

- [ ] **Step 5: Write behavior RED tests**

Add tests that call the real types and assert:

```csharp
Assert.Throws<NetworkPolicyException>(() =>
    UnityHttpApi.ValidateOnlineUri(new Uri("http://game.example/v1/login")));
Assert.DoesNotThrow(() =>
    UnityHttpApi.ValidateOnlineUri(new Uri("https://game.example/v1/login")));

HttpRequest request = GuestLoginRequest.Create(new Uri("https://game.example"));
Assert.That(request.Uri, Is.EqualTo(new Uri("https://game.example/v1/login/guest")));
Assert.That(request.Method.Value, Is.EqualTo("POST"));

foreach (byte[] malformed in new[] {
    Array.Empty<byte>(), Encoding.UTF8.GetBytes("{"),
    Encoding.UTF8.GetBytes("{\"sessionId\":\"not-a-guid\"}"),
    Encoding.UTF8.GetBytes("{\"sessionId\":\"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee\",\"ticket\":\"!\"}") })
    Assert.That(GuestLoginResult.Parse(new HttpResponse { StatusCode = 200, Body = malformed }).Success,
        Is.False);
```

Also add one valid JSON vector containing playerId, sessionId, ticket, resumeToken, macKey, and expiresAtUnixSeconds; assert every decoded field exactly.

- [ ] **Step 6: Run behavior tests and verify RED**

Expected: tests compile after the contract shell exists and fail because request creation, parsing, and sending are not implemented.

- [ ] **Step 7: Implement Unity HTTP and non-throwing login parsing**

Implement `IHttpApi.SendAsync` with `UnityWebRequest`, cancellation registration calling `Abort`, and a `while (!operation.isDone) { cancellationToken.ThrowIfCancellationRequested(); await Task.Yield(); }` loop. Return status code, downloaded bytes, and error.

Use field-based `[Serializable] GuestLoginDto` because Unity 2022 `JsonUtility` does not support `init` DTOs. Public result models use ordinary `{ get; set; }`. Decode JSON with `new UTF8Encoding(false, true)` and catch exactly `DecoderFallbackException`, `ArgumentException`, `FormatException`, and `OverflowException`. Validate nonempty playerId, nonempty ticket and resume token, a 32-byte MacKey, a nonempty GUID, and positive expiry, then return `Success=false` with `Malformed guest login response.` rather than throwing.

- [ ] **Step 8: Run focused GREEN tests**

Run the fixture command to `http-green.xml` / `http-green.log`.

Expected: all HTTPS, request, valid decode, and malformed-input tests pass.

- [ ] **Step 9: Commit**

```bash
git add Assets/Scripts/Network/UnityHttp Assets/Tests/Network/UnityHttpApiTests.cs \
  Assets/Tests/Network/ZWebGLFrameWork.Network.Tests.asmdef
git commit -m "feat(network): add strict HTTPS guest login"
```

---

### Task 3: Add the authenticated WSS channel

**Files:**
- Create: `Assets/Scripts/Network/WebGL/ZWebGLFrameWork.Network.WebGL.asmdef`
- Create: `Assets/Scripts/Network/WebGL/INativeWebSocketClient.cs`
- Create: `Assets/Scripts/Network/WebGL/NativeWebSocketClientAdapter.cs`
- Create: `Assets/Scripts/Network/WebGL/WebGlRealtimeChannel.cs`
- Create: `Assets/Scripts/Network/WebGL/WebGlRealtimeChannelFactory.cs`
- Create: `Assets/Tests/Network/WebGlRealtimeChannelTests.cs`
- Create: `Assets/Tests/Network/WebGlAssemblyBoundaryTests.cs`
- Modify: `Assets/Tests/Network/ZWebGLFrameWork.Network.Tests.asmdef`

**Interfaces:**
- Produces `INativeWebSocketClientFactory.Create(Uri, IReadOnlyList<string>)` and an `IRealtimeChannel` whose connection completes only after matching AuthAccepted.
- Registers `RealtimeTransport.WebSocket` through the R1 registry.

- [ ] **Step 1: Add a reflection RED for `WebGlRealtimeChannel`**

Assert `Type.GetType("ZWebGLFrameWork.Network.WebGL.WebGlRealtimeChannel, ZWebGLFrameWork.Network.WebGL")` is not null. Run the fixture and observe one assertion failure.

- [ ] **Step 2: Add assembly and interface shells**

The WebGL asmdef references Contracts, Runtime, and `colyseus.nativewebsocket`, includes Editor/WebGL, and is not auto-referenced. Define:

```csharp
internal interface INativeWebSocketClient : IDisposable
{
    event Action Opened;
    event Action<byte[]> MessageReceived;
    event Action<string> Error;
    event Action<ushort> Closed;
    Task ConnectAsync();
    Task SendAsync(byte[] completeMessage);
    Task CloseAsync();
    void DispatchMessageQueue();
}

internal interface INativeWebSocketClientFactory
{
    INativeWebSocketClient Create(Uri uri, IReadOnlyList<string> subprotocols);
}
```

Add an internal constructor to `WebGlRealtimeChannel` receiving the factory and a public parameterless constructor using `NativeWebSocketClientFactory`.

- [ ] **Step 3: Run contract GREEN, then write behavior RED tests**

Use a real fake client implementing the interface. Tests must cover: insecure scheme, wrong path, wrong transport, exact subprotocol, AuthHello first, matching AuthAccepted required, mismatched session rejection, malformed frame rejection, cancellation, connect error, close-before-connect, complete-message FIFO receive, idempotent close, and duplicate receiver rejection.

Core connection assertion:

```csharp
Task connect = channel.ConnectAsync(
    new RealtimeEndpoint(RealtimeTransport.WebSocket,
        new Uri("wss://game.example/v1/realtime")),
    TestCredentials.Valid, CancellationToken.None);
client.RaiseOpened();
await client.WaitForSendAsync();
Assert.That(RealtimeCodec.Decode(client.Sent[0]).Kind,
    Is.EqualTo(RealtimeMessageKind.AuthHello));
client.RaiseMessage(RealtimeCodec.Encode(new AuthAccepted {
    SessionId = TestCredentials.Valid.SessionId,
    LastAuthorityTick = -1,
    TimelineRevision = 0
}));
await connect;
Assert.That(factory.Subprotocols, Is.EqualTo(
    new[] { RealtimeProtocol.WebSocketSubprotocol }));
```

- [ ] **Step 4: Run behavior tests and verify RED**

Expected: contract test passes; behavior tests fail because the channel methods are not implemented.

- [ ] **Step 5: Implement the upstream adapter**

Construct `NativeWebSocket.WebSocket` with absolute URL and `new List<string>(subprotocols)`. Forward `OnOpen`, `OnMessage`, `OnError`, and `OnClose`. `ConnectAsync` returns the upstream `Connect()` task without treating completion as readiness; `SendAsync` and `CloseAsync` await upstream methods. `DispatchMessageQueue` calls upstream dispatch only for Editor/non-WebGL. Convert close enum to `ushort` without changing the value.

- [ ] **Step 6: Implement channel state and authentication**

Use a lock, one receive queue, one pending receiver, and `TaskCompletionSource<bool>(RunContinuationsAsynchronously)` for authentication. Validate endpoint before allocating a client. On open, send encoded AuthHello. Decode every message; before authentication accept only a matching AuthAccepted, otherwise fail and close. After authentication, enqueue complete messages or complete the single pending receiver. Store and observe the native connect lifetime task; route its fault into the authentication/close path. Cancellation calls the same idempotent close path. Never hold the lock while invoking client methods or completing user continuations.

Factory registration is:

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
private static void Install()
{
    RealtimeChannelFactoryRegistry.Install(
        RealtimeTransport.WebSocket, new WebGlRealtimeChannelFactory());
}
```

- [ ] **Step 7: Run focused and full Network GREEN tests**

Run WebGl tests, assembly-boundary tests, then the full `ZWebGLFrameWork.Network.Tests` filter. Expected: all tests pass, including R1 coexistence and the existing 33 Network tests.

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/Network/WebGL Assets/Tests/Network/WebGlRealtimeChannelTests.cs \
  Assets/Tests/Network/WebGlAssemblyBoundaryTests.cs \
  Assets/Tests/Network/ZWebGLFrameWork.Network.Tests.asmdef
git commit -m "feat(network): add pinned NativeWebSocket adapter"
```

---

### Task 4: Verify WebGL compilation and package boundaries

**Files:**
- Create: `Assets/Editor/NetworkVerificationBuildScript.cs`
- Create: `Assets/Tests/Network/NativeWebSocketPackageTests.cs`

**Interfaces:**
- Produces deterministic package layout checks and an external-root WebGL verification build.

- [ ] **Step 1: Write package and assembly boundary RED tests**

Assert the package contains the three `src/NativeWebSocket` C# files, four Unity integration files, Apache license, and asmdef name `colyseus.nativewebsocket`; assert no `Assets/Plugins/WebGL` replacement exists, Runtime asmdef contains no concrete transport reference, and only `NativeWebSocketClientAdapter.cs` beneath `Assets/Scripts` contains the token `NativeWebSocket.WebSocket`.

- [ ] **Step 2: Run and observe RED if any required layout is absent**

Run the package fixture to `package-red.xml` / `.log`.

- [ ] **Step 3: Add verification build entry point**

Create:

```csharp
namespace ZWebGLFrameWork.Editor
{
    public static class NetworkVerificationBuildScript
    {
        public static void BuildWebGL() => BuildScript.BuildWebGL();
    }
}
```

Regenerate the package only through `scripts/vendor-nativewebsocket.sh` if a layout test fails; do not hand-patch upstream bytes.

- [ ] **Step 4: Run final gates**

```bash
bash scripts/vendor-nativewebsocket.test.sh
bash scripts/verify-realtime-dependency-provenance.sh
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform EditMode \
  -testFilter ZWebGLFrameWork.Network.Tests \
  -testResults /tmp/zweb-network-server-evidence/network/task-05/network-green.xml \
  -logFile /tmp/zweb-network-server-evidence/network/task-05/network-green.log
ZWEB_EVIDENCE_ROOT=/tmp/zweb-network-server-evidence/network/task-05 \
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath "$PWD" \
  -executeMethod ZWebGLFrameWork.Editor.NetworkVerificationBuildScript.BuildWebGL \
  -logFile /tmp/zweb-network-server-evidence/network/task-05/webgl.log -quit
git diff --check
```

Expected: provenance passes, Network tests are nonzero/pass-only, WebGL build succeeds beneath the external root, and diff check passes.

- [ ] **Step 5: Commit**

```bash
git add Assets/Editor/NetworkVerificationBuildScript.cs \
  Assets/Tests/Network/NativeWebSocketPackageTests.cs
git commit -m "test(network): verify WebSocket package and WebGL build"
```
