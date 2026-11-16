# Sync & Local Changes

![Sync](images/sync-en.png)

## What `Sync` does

For every `enabled` gist in `gists.json`:

1. Downloads `raw_url` into the resolved `targetPath`.
2. Auto-creates/regenerates `asmdef` files in each gist folder.
3. If the target root sits under `Packages/`, also ensures a single
   `com.gistscripts.gists` `package.json` at the root (the whole folder
   is one embedded package — Unity only sees direct children of `Packages/`).

## Local modifications are protected

Every file the tool writes records a `sha256` into `gists.json`. Before
writing again the tool hashes the on-disk file and compares:

- **Matches** — safe to overwrite.
- **Differs** (or empty / unknown) — the file is **skipped**. `skipped: N (locally modified)` is reported in the status bar. Use the detail pane → file row → **Overwrite** to force (the UI asks for confirmation).

## When files can't be downloaded

- `raw_url` missing → `skipped` reason reported.
- The upstream gist was renamed or deleted → `skipped: file not found`.
- Rate-limit hit → `HTTP 403` wrapped into a `GistApiException` with an actionable message.

## Sync vs Add

Adding a gist **only writes the manifest**; the files are not downloaded until Sync runs.
