# Phase 5c: FrameSync — Implementation Plan

**Goal:** Real-time PvP frame synchronization with prediction, rollback, and KCP transport.

**Tasks:**
1. Full component snapshot (Pool export/restore + TickSnapshot)
2. RingBuffer<TickSnapshot> (64-tick window)
3. Rollback pipeline (SimOrchestrator rollback + replay)
4. Network protocol (FrameInput + InputPacket + KCP transport)
5. FrameSyncClient (predict → compare → rollback state machine)
6. Server-side design document
