# MUD Gist Manager

Manage GitHub Gist snippets as versioned, Unity-ready scripts. Gist scripts
live in your repository as plain source files, downloaded on explicit sync,
and share a manifest that doubles as both the editor tool and CI input.

![MUD Gist Manager](images/menu.png)

## What it does

- **Add** a GitHub Gist: paste the URL, GistManager parses metadata, writes a
  `gists.json` manifest, and downloads the files on sync — not on add.
- **Sync**: downloads all enabled gists. Files that differ from their
  recorded `sha256` (i.e. locally modified) are **skipped**, not overwritten;
  a dedicated **Overwrite** button exists for explicit force.
- **Enable / Disable** at the *package* level (whole gist, not individual
  files): disabling moves the entire gist directory (`.cs`, `.meta`, `asmdef`)
  into a `Backup~/Disabled/` folder that Unity ignores; enabling restores it
  byte-for-byte, preserving GUIDs and your local modifications.
- **Two placement roots**: `{targetRoot}` may be `Packages/gist-scripts`
  (an embedded UPM package, mirrors `Packages/manifest.json` `file:` semantics)
  or `Assets/GistScripts` (plain assets). Switching between them moves the
  current mode and re-resolves `packages-lock.json` automatically.
- **Multiple selection**, **foldable author groups**, **search**, **keyboard
  navigation**, **bilingual UI** (English / 简体中文).

## Design principles

- `gists.json` is the single source of truth; the editor GUI and GitHub
  Actions both read it.
- **Add ≠ Sync**: adding a gist only parses metadata; files are written
  only on explicit sync.
- **Sync protects your local changes** via per-file `sha256`; the manifest
  records the fingerprint of the last state written by the tool.
- **Package-level enable/disable**: whole gist directory moves in and out
  of `Backup~/Disabled/`; the `.meta` is moved along with the rest, so GUIDs
  never break.

## Quick installation

```
Window → Package Manager → + → Install package from git URL...
https://github.com/MUDV587/com.mud.modules.gistmanager.git
```

Requires Unity 2022.3 LTS (UI Toolkit). No other dependencies — the only
runtime dependency is `com.unity.nuget.newtonsoft-json`, resolved by the
Unity Package Manager.
