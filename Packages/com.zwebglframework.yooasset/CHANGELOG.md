# Changelog

## [0.1.0]

- Added asynchronous business resource loading with independent YooAsset handles and UniTask
  cancellation through the core resource lease API.
- Added compatibility caching for existing bare-object loads, with protection against stale
  completions after unloading or provider retirement.
- Added business version/manifest preparation and documented cancellation and package ownership.
- Kept resource collection, package initialization, runtime file systems, and global shutdown
  under application control.
