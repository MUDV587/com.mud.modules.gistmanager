# Settings Page

**Edit → Project Settings → MUD → Gist Manager**

![Settings](images/settings-en.png)

## Interface language

`System (automatic)` / `中文` / `English`. Default: follow Unity's
`Application.systemLanguage` (Chinese → Chinese). Changes take effect
immediately for the main window; the Project Settings page updates on
its next repaint.

## Target Root

`Packages/gist-scripts` (default, embedded UPM package) or
`Assets/GistScripts` (plain assets). Switching the Target Root moves
**everything** (manifest, package marker, all gist files and their
`.meta`) to the new root; the old root is deleted after moving.

If the destination already contains a gist with the same `packageDir`
(non-empty) or the same names — `gists.json`, `package.json`, or any
`.meta` files — the move aborts with **no changes**. See
[Target Root Switching](target-root.md) for details.

## GitHub Token

Stored in `EditorPrefs` (machine-local, never committed). Anonymous
requests are rate-limited to 60/hour; with a token, 5,000/hour.

## Upstream update check

On Sync, the tool also calls the GitHub Gist API once per entry and
compares `rawUrl` (which contains a commit SHA) with the upstream file
list. If anything differs, it reports `Upstream updated:` — it does
**not** modify your manifest. Disable to skip these calls.

## Manifest path

Auto-detected from `ProjectSettings/GistManager.asset` →
`manifestRelativePath`, defaulting to `Packages/gist-scripts/gists.json`.
Shown here for convenience. The path follows the active Target Root.
