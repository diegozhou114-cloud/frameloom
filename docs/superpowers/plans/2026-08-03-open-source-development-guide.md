# Open-Source Development Guide Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish an accurate Chinese developer guide that explains how to use and extend ZWebGLFrameWork for offline play, deterministic simulation, networking, and authoritative frame sync without overstating production readiness.

**Architecture:** Keep one canonical guide at `docs/DEVELOPMENT_GUIDE.md`, organized from first run through architecture, integration, diagnostics, and contribution rules. Keep `README.md` as a concise project entry point that links to the guide and exposes the reference server's maturity boundary.

**Tech Stack:** Markdown, Mermaid, Unity 2022.3.62f3c1, C# 9, .NET Standard 2.1, .NET 8, shell validation commands.

## Global Constraints

- Chinese is the primary language; preserve English type names, protocol names, paths, and commands.
- Use only public APIs and repository paths verified against `origin/master` at `1ea98c563a79ee2201d860fa4108fd44001bd6e5`.
- Label capabilities as `Ready`, `Reference`, or `Integration Required` when first introduced.
- Treat WebGL WSS, Native KCP, the .NET 8 hosts, and ReplayValidator as reference implementations rather than production services.
- State that match allocation, client tick send/ack handling, prediction/rollback reconciliation, and production business-content hot update require integration.
- Do not change runtime code, protocol behavior, build configuration, or test code.
- Keep future public API, runtime mode, protocol, build, and maturity changes synchronized with the same guide.

---

### Task 1: Write the canonical developer guide

**Files:**
- Create: `docs/DEVELOPMENT_GUIDE.md`
- Read: `Assets/Scripts/SimulationCore/`, `Assets/Scripts/Network/`, `Assets/Scripts/FSM/`, `Assets/Scripts/Combat/`, `Server/`

**Interfaces:**
- Consumes: Current public Unity and .NET APIs, the approved design at `docs/superpowers/specs/2026-08-03-open-source-development-guide-design.md`, and existing build commands.
- Produces: A single developer guide linked by the root README and maintained as part of the framework's public contract.

- [ ] **Step 1: Verify every API used by an example**

Run:

```bash
rg -n "class SimBattleSession|interface IRealtimeChannel|class FrameworkBattleAuthority|class RealtimeSessionRouter|class FrameSyncMatchActor|class AuthorityFrameBroadcaster|class ReplayValidator" Assets Server
rg -n "Start\(|Submit\(|Tick\(|CreateSnapshot|RestoreSnapshot|Checksum|PresentationFrame" Assets/Scripts/SimulationCore Server
```

Expected: each documented type and method resolves to current source; examples use exact signatures rather than inferred APIs.

- [ ] **Step 2: Write the framework positioning, quick start, architecture, and module boundaries**

Create the first four guide sections with:

- the three maturity labels and their exact initial classifications;
- Unity `2022.3.62f3c1`, C# 9, .NET Standard 2.1, .NET 8, target platforms, and `./ci_build.sh Validate`;
- one Mermaid overview showing offline Unity, online Unity, and the reference server sharing deterministic `SimulationCore`;
- module responsibilities and the acyclic dependency direction.

- [ ] **Step 3: Document offline mode and the SimulationCore minimum loop**

Describe the verified runtime path:

```text
GameLauncher -> GameFSM -> MainVillageState -> FrameworkBattleAuthority
             -> BattleState -> CombatManager -> SimBattleSession
```

Include compilable-shape examples for session construction, `Start`, command submission, ticking, presentation frames, snapshots, checksums, and replay. Explain rejection results and identify `FrameworkBattleAuthority` as a local fallback with reserved identities.

- [ ] **Step 4: Document networking and the authoritative frame-sync reference server**

Cover `IRealtimeChannel`, WebGL WSS, Native KCP, authenticated envelopes, connection lifecycle, and platform selection. Add a Mermaid data-flow diagram for:

```text
RealtimeSessionRouter -> FrameSyncMatchActor -> AuthorityFrameBroadcaster
```

Explain deadline NoOp injection, ack progress, checksum comparison, late/future input rejection, disconnect recovery boundaries, and why `UnavailableLoginAllocator` means `RealtimeHost` cannot currently allocate production matches.

- [ ] **Step 5: Document extension workflows, replay diagnostics, gates, and contribution rules**

Provide verified shortest paths for adding commands, deterministic content, combat modes, UI panels, and transports/providers. Document replay recording and validation, stable CLI exits, resource limits, the fixed troubleshooting order, test/build commands, known integration gaps, and the same-commit guide update contract.

- [ ] **Step 6: Validate the guide structure and repository references**

Run:

```bash
rg -n '^## ' docs/DEVELOPMENT_GUIDE.md
rg -n '^```(mermaid|csharp|bash|text)?$' docs/DEVELOPMENT_GUIDE.md
git diff --check -- docs/DEVELOPMENT_GUIDE.md
```

Expected: all 12 approved sections are present, code fences are balanced, and no whitespace errors are reported.

### Task 2: Publish the entry point and verify the documentation change

**Files:**
- Modify: `README.md`
- Verify: `docs/DEVELOPMENT_GUIDE.md`
- Verify: `docs/superpowers/specs/2026-08-03-open-source-development-guide-design.md`
- Verify: `docs/superpowers/plans/2026-08-03-open-source-development-guide.md`

**Interfaces:**
- Consumes: The canonical developer guide created by Task 1.
- Produces: A concise repository landing page and a verified documentation-only commit ready to push.

- [ ] **Step 1: Replace the placeholder README with a concise project entry point**

Include only:

- one project-positioning paragraph;
- an environment summary for Unity, C#, target platforms, and .NET 8 reference services;
- capability bullets for offline mode, deterministic simulation, WSS/KCP, and Replay;
- a prominent relative link to `docs/DEVELOPMENT_GUIDE.md`;
- a direct warning that the reference server is not production-ready.

- [ ] **Step 2: Check all local Markdown links and referenced repository paths**

Extract local Markdown link destinations from the changed Markdown files, strip anchors, resolve them relative to each document, and verify that every destination exists. Independently check every backticked path used as a source or command input.

Expected: no unresolved local link or nonexistent documented source path.

- [ ] **Step 3: Run the .NET reference-server test suite**

Run from a regular clone rather than a linked worktree because `FrameSyncFixtureTests` currently require `.git` to be a directory:

```bash
dotnet test Server/ZWebGLFrameWork.Server.sln --nologo --verbosity minimal
```

Expected: 336 tests pass, 0 fail.

- [ ] **Step 4: Run the Unity compilation gate when Unity is available**

Run:

```bash
./ci_build.sh Validate
```

Expected: Unity compilation validation passes. If Unity is unavailable or unlicensed, record the exact limitation in the delivery report instead of claiming validation.

- [ ] **Step 5: Review the final change boundary**

Run:

```bash
git diff --check origin/master...HEAD
git status --short
git diff --stat origin/master
```

Expected: the branch contains only the approved design, implementation plan, developer guide, and README changes, with no runtime or generated files.

- [ ] **Step 6: Commit and push the completed documentation**

Run:

```bash
git add README.md docs/DEVELOPMENT_GUIDE.md docs/superpowers/plans/2026-08-03-open-source-development-guide.md
git commit -m "docs: add framework development guide"
git push -u origin codex/development-guide
```

Expected: the documentation commit succeeds and `origin/codex/development-guide` points to the new commit.
