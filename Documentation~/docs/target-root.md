# Target Root Switching

![TargetRoot](images/target-root-en.png)

## Two possible roots

- `Packages/gist-scripts` (default) — the gists live inside Unity's
  `Packages/` as an **embedded package** (`com.gistscripts.gists`).
  `package.json` is auto-generated at the root if missing.
  Unity treats the whole folder as a single package.
- `Assets/GistScripts` — gists live in Assets as plain project assets.

## Switching

**Edit → Project Settings → MUD → Gist Manager → Target Root**

You choose between:

- **Switch and move** — current target root + the manifest + the embedded
  package marker + all gist files (and their `.meta`) move to the new
  root, byte-for-byte. The old root is deleted once fully emptied.
- **Switch only (no move)** — only flips the recorded `targetRoot`; the
  files stay in place. Followed by a warning that the next Sync will
  re-download from scratch and stale copies in the old location will
  break compilation due to duplicate asmdefs.
- **Cancel** — nothing changes.

## Safety rails

- If the destination already contains a gist with the same `packageDir`
  (non-empty), or already has `gists.json` / `package.json` / `.meta` files
  with the same names, the move **aborts with zero changes**.
- If IO fails mid-move, the tool **rolls back** — everything goes back to
  the original locations; manifest unchanged.
- After a successful move, the UPM is told to re-resolve so the embedded
  package shows up correctly (`PackageManager.Client.Resolve()`).
  That's the mechanism behind "now I can see it without Ctrl+R".

## After switching

Depending on the direction:

- **Packages → Assets** — `com.gistscripts.gists` disappears from UPM;
  the files stay in place; scripts behave like normal project assets.
- **Assets → Packages** — `com.gistscripts.gists` reappears in UPM; the
  embedded package is back. No manual Ctrl+R required.
