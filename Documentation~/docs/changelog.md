# Changelog

The full history lives in [`CHANGELOG.md`](https://github.com/MUDV587/com.mud.modules.gistmanager/blob/main/CHANGELOG.md).
This page mirrors its top section for convenience.

## What's new in 0.3.0

- **Target Root switching** — change between `Packages/gist-scripts` and `Assets/GistScripts` at runtime: everything moves (manifest, embedded package marker, all gist files and their `.meta`), atomic rollback on failure.
- **Package-level enable/disable** — disable a gist package by moving the whole directory into `Backup~/Disabled/`; enable restores it byte-for-byte.
- **`.meta` follows the package** — no empty directory ghosts.
- **`Client.Resolve()` on target root switch** — UPM re-registers the embedded package automatically; no more manual Ctrl+R to see it.
- **Atomic gists.json write** — `gists.json.tmp-*` → `File.Replace`; the previous version rotates to `Backup~/gists.json.bak`.
- **Corruption guard** — invalid JSON never overwrites your manifest; a `Backup~/gists.json.bad-*` copy is preserved and the editor shows the error.
- **Request timeout** — 30s cap on network calls; `Sync All` can't wedge permanently.

See [`CHANGELOG.md`](https://github.com/MUDV587/com.mud.modules.gistmanager/blob/main/CHANGELOG.md) for complete history.
