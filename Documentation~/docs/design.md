# Design & Internals

![Design](images/design-en.png)

## Overview

Gist Script Manager is a Unity 2022.3 LTS editor package; its job is to
mirror GitHub Gists faithfully into your project, with a small and
auditable footprint under `Packages/`.

```
MUD.Modules.GistManager (assembly, Editor-only)
├── GistManagerWindow          ← the UI (UI Toolkit)
├── GistManifestStore          ← read/write + atomic replace + backup rotation
├── GistCatalog                ← merge / packageDir / migration logic (pure)
├── GistSyncService            ← download, remove, asmdef generation, .meta recovery
├── GistApiClient              ← async GitHub API via UnityWebRequest
├── GistSearch                 ← filtering / empty-state copy (pure)
├── GistRelocateService        ← Target Root switching (move everything)
└── GistManagerSettings        ← ScriptableSingleton (ProjectSettings/GistManager.asset)
```

## Data flow

```
Add (Add panel) → GistUrlParser → FetchGistAsync → MergeMetadata → gists.json
Sync button      → GistSyncService.SyncAsync     → gists.json + files on disk
Target Root tidy → GistRelocateService.RelocateAt → atomic move + re-register
```

## Key decisions

1. **`gists.json` single source of truth.** Every write path goes through
   this file, strongly typed and validated. No other persisted state
   encodes the package list.

2. **Add ≠ Sync.** Adding a gist never touches the network beyond the
   initial metadata fetch (owner/description/files/raw_url). Downloading
   is always an explicit Sync — the GUI can't accidentally blast a gist
   into your project.

3. **Date-independent heuristic for Editor/Runtime placement**: `Auto`
   placement inspects for `UnityEditor` references; the asmdef scope is
   generated accordingly.

4. **Per-file fingerprinting**: every file the tool writes records its
   `sha256`; sync skips the file if disk matches the record or the first
   load back-fills the baseline (`Normalize`). Force overwrite is a
   separate, explicit operation with a confirmation dialog.

5. **Package-level Enable / Disable**: disable moves the whole gist
   directory into `Backup~/Disabled/` (Unity-ignored) in one move;
   enable restores it by moving back. GUIDs never get re-issued, your
   local edits are never dropped.

6. **Backup~/ as staging area**: tool-private, Unity-ignored. Used for:
   marker (`.bak`), corrupt-load backups (`.bad-*`), disabled packages,
   overwritten locals, stray migrated-on-load.

7. **Relocating Target Root** is the most invasive operation in the
   package; it runs a full preflight (dest occupied or invalid → zero
   changes), moves the entire tree, and calls `PackageManager.Client.Resolve()`
   to re-register the embedded package in UPM.

## Assembly

A single `MUD.Modules.GistManager.Editor` assembly covers everything:
Model is testable in isolation ("without Unity" in principle,
all services run inside the editor anyway).
