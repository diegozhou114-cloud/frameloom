# UI Close-All Reentry Design

**Date:** 2026-07-30
**Status:** Implemented.

## Goal

Make `UIManager.CloseAll` complete safely when a panel's `OnClose` callback
attempts to open another panel.

## Design

`CloseAll` is a terminal UI operation. While it is running, `UIManager.Open<T>`
returns `null`, preventing lifecycle callbacks from changing `_panelCache`
while `CloseAll` traverses it. Recursive `CloseAll` calls return immediately.

Because terminal cleanup destroys panel GameObjects, it bypasses close
animations and completes `OnClose` synchronously before destruction. Ordinary
single-panel closing continues to use its configured animation.

The cache destruction, stack discard, and sorting-order reset paths are in
`finally`, so an exception from a panel close callback cannot leave stale UI
ownership behind. Ordinary `Open`, `Close`, and `CloseLayer` behavior is
unchanged outside this short close-all window.

## Verification

`UIOpenTransactionTests.CloseAll_WhenOnCloseOpensPanel_RejectsReentryAndCleansCache`
reproduces the former `InvalidOperationException` from dictionary mutation and
verifies the close completes with no replacement panel or stack entry. A second
test verifies an exception during close cannot leave a destroyed panel in a UI
stack. A third test verifies an animated panel still runs `OnClose` during
terminal cleanup.
