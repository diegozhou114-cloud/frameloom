# UI Lifecycle Reentry Design

**Date:** 2026-07-30
**Status:** Implemented.

## Goal

Keep a cached UI panel consistent when its `OnClose` callback reopens the
same panel. The new open lifecycle must retain its active GameObject,
interaction state, data, and UI stack ownership.

## Design

`UIBase` owns a monotonically increasing lifecycle version. Each open and
close captures a new version. Animation completion and synchronous lifecycle
callbacks run only when their captured version is still current. If `OnClose`
reopens the panel, that open advances the version, so the prior close skips
its inactive and data-clearing cleanup.

Opening also restores `CanvasGroup.interactable` before lifecycle callbacks.
This establishes the correct base state when a previously paused cached panel
is reopened.

## Scope

The change stays inside `UIBase`. `UIManager` continues to own caching and
stack admission, while `UIStack` continues to own pause and resume effects.
No global reentry lock is introduced because a close callback may legitimately
open an error or replacement panel.

## Verification

`UIOpenTransactionTests.Close_WhenOnCloseReopensSamePanel_PreservesLatestLifecycle`
reproduces the old inactive-panel failure and verifies the reopened panel is
active, interactive, carries the replacement data, and has exactly one stack
entry. The complete EditMode and PlayMode suites validate the surrounding
contracts.
