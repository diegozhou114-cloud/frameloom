# Changelog

## [0.1.0]

- Consolidated the framework runtime and retained transport sources into one canonical Unity
  package.
- Declared Unity 2022.3 LTS support with 2022.3.62f3c1 as the tested editor.
- Included deterministic simulation and tick output, presentation and view pooling, and Replay
  validation.
- Added public buffered presentation and command-recording APIs with explicit borrowed and owning
  lifetime contracts.
- Required consumers to pin UniTask directly because Unity 2022.3 cannot express Git dependencies
  transitively in package metadata.
- Kept `Server/` projects as repository-only reference and validation hosts, not package runtime or
  production server infrastructure.
- Kept buffered Replay capture outside allocation gates; WebGL managed allocation unmeasured by
  editor evidence.
- Preserved existing namespaces, assembly names, package ID, GUIDs, and serialized compatibility.
