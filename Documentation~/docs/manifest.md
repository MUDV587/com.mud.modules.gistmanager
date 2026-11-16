# Manifest Format

![Manifest](images/manifest-en.png)

`gists.json` is the **single source of truth**. The GUI writes to it;
CI reads it. Manual editing is fine and the editor picks changes up on
focus (or on the next **Reload Manifest**).

```json
{
  "targetRoot": "Packages/gist-scripts",
  "gists": [
    {
      "id": "dfc43134fbfefb820d0adbc5d7c25fb3",
      "author": "yasirkula",
      "description": "unitypackage extractor",
      "packageDir": "yasirkula.unitypackage-extractor",
      "files": [
        {
          "filename": "UnitypackageExtractor.cs",
          "targetPath": "Packages/gist-scripts/yasirkula.unitypackage-extractor/Editor/UnitypackageExtractor.cs",
          "enabled": true,
          "rawUrl": "https://gist.githubusercontent.com/yasirkula/dfc43134/raw/UnitypackageExtractor.cs",
          "meta": "",
          "placement": "Editor",
          "sha256": "…"
        }
      ]
    }
  ]
}
```

## Top-level

- `targetRoot` — path of the root of all gists (relative to project).
- `gists` — array of gist entries.

## Per gist

- `id` — gist ID (required, unique).
- `author` — GitHub username (defaults to `anonymous`).
- `description` — free text shown in the window.
- `packageDir` — folder name under `targetRoot` (defaults to `{author}.{filename-short}`).
- `files` — array of per-file records.

## Per file

- `filename` — display name.
- `targetPath` — where the file lives on disk (forward slashes).
- `enabled` — package-level enable bit; whole-gist switch.
- `rawUrl` — source-of-truth URL for the file (GitHub Gist's `raw_url`).
- `sha256` — fingerprint of the file at last write; drives local-change detection.
- `placement` — `Auto` (detect from content) / `Editor` / `Runtime`.
- `meta` — **legacy**, unneeded for new entries. Records the `.meta`
  GUID of the file at disable time (when the file was moved into
  `Backup~/Disabled/`).
