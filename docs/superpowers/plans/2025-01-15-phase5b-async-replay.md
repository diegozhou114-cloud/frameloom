# Phase 5b: AsyncReplay — Implementation Plan

**Goal:** Build end-to-end server replay validation pipeline.

**Dependencies:** Task 1 → Task 3+4, Task 2 → Task 3+4. Task 5 after all.

## Task 1: ReplayFile JSON Serialization
Replace pipe-delimited StringBuilder with System.Text.Json. ReplayFile → JSON round-trip tested.

## Task 2: Archetype System
SimEntityArchetype defines component templates per ArchetypeId. SimOrchestrator.StartBattle uses it.

## Task 3: ReplayValidator (.NET Core Console)
Standalone console project referencing SimulationCore source. Validates ReplayFile JSON.

## Task 4: SimOrchestrator Replay Mode
New constructor/method to replay from ReplayFile instead of live CommandBuffer.

## Task 5: End-to-End Tests
Full battle → export JSON → ReplayValidator → PASS. Tampered replay → FAIL.
