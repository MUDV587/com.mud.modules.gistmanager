# Troubleshooting & FAQ

![FAQ](images/troubleshoot-en.png)

## UPM: `com.gistscripts.gists` doesn't appear

The embedded package is re-registered by `PackageManager.Client.Resolve()`
on Target Root switch or on disable/enable. If the Project Settings
window was opened before the package was on disk, press `Ctrl+R` or
`Window → Sequencing Plugin`. (A clean re-import is also triggered by
switching once and switching back.)

## Sync: `skipped (locally modified)`

The on-disk file differs from the fingerprint recorded in `gists.json`.
That means you or another tool changed it. The tool won't overwrite.

- If the local change is junk → **Restore** via **Overwrite** in the file row.
- If the local change matters → keep it; disable the gist if you're not ready to merge upstream.

## Asmdef references are empty

Auto-generated asmdef `references` is always `[]`. If your gist references
project-internal types across assembly boundaries:

- Add the reference manually in the generated `.asmdef`, or
- Use **Target Root → Assets/GistScripts** — ordinary project scripts
  (no asmdef boundary) call into each other freely.

## `github.com rate limit 403`

Anonymous GitHub API calls are limited to 60/hour. Go to
**Project Settings → MUD → Gist Manager → GitHub Token**, paste a token.
The tool will pick it up immediately.

## gists.json is corrupt / not valid JSON

The tool shows the parse error in the status bar and **refuses to overwrite**
the manifest. Fix it by hand. The tool will automatically:

1. Back up the corrupt file to `Backup~/gists.json.bad-<timestamp>`.
2. Load an empty manifest to keep the GUI running.
3. When you fix the file, the editor picks it up on focus or on the next
   **Reload Manifest**.

## `Target Root` switch says "Cannot switch: destination occupied"

The destination already has a `gists.json`, `package.json` (or their
`.meta`), or a gist with the same `packageDir` (non-empty). The tool
does **not** touch them. Move/rename the conflicting files first, or
cancel the switch.

## Disabled gist doesn't reappear after enable

Because Unity ignores `Backup~/`, switching out and back in is fine — but
if after enabling, the files don't appear, run **Sync** at least once
(this also refreshes AssetDatabase and asmdefs).
