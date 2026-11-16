# MUD Gist Manager

English | [简体中文](README.zh-CN.md)

Unity Editor tool that collects scattered GitHub Gist scripts into a project with one manifest (`gists.json`, moved with `targetRoot`), shared between the editor GUI and CI.

## Key decisions

- **`gists.json` is the single source of truth**: GUI add, manual edit, and CI sync all read/write this file. Its position tracks `targetRoot` and travels with it. File download is a separate explicit action.
- **Tool backups live in `{targetRoot}/Backup~/`**, ignored by Unity: package-disable snapshots (whole gist dir + `.meta` to `Backup~/Disabled/`, restored on enable), atomic `gists.json` rotation (`gists.json.bak`), corrupt-manifest backups (`gists.json.bad-*`), overwrite insurance (`Backup~/Overwritten/<timestamp>/`). Sync / switch / enable all route through `Backup~/`. CI may safely skip `**/Backup~/`.
- **Add ≠ Sync**: pasting a Gist URL only parses metadata; the file is downloaded only on explicit Sync.
- **Sync protects local changes**: each file records the `sha256` of the last state the tool wrote. Sync skips files whose disk contents differ (= you edited), never overwrites. The detail pane includes an **Overwrite** button (with confirmation) for force.
- **Package-level Enable / Disable**: one toggle per gist, controlling the entire gist directory (move to / restore from `Backup~/Disabled/<packageDir>`).
- **Two placement roots**: `Packages/gist-scripts` (embedded UPM package, single `package.json` at the root) or `Assets/GistScripts` (ordinary assets). Switch between them at runtime; the tool re-resolves the UPM embedded package automatically.
- **Editor / Runtime separation with auto asmdef**: each file lands under `{targetRoot}/{packageDir}/Editor|Runtime/` and an asmdef is generated per gist folder per placement actually used.

## Install

```
Window → Package Manager → + → Install package from git URL...
https://github.com/MUDV587/com.mud.modules.gistmanager.git
```

Requires Unity **2022.3 LTS** (UI Toolkit). The only dependency is `com.unity.nuget.newtonsoft-json` (3.0.2), resolved automatically.

## Quick start

1. **Tools → MUD → Gist Manager**.
2. Click **+** (or `Ctrl/Cmd+F` to focus search first) and paste a Gist URL — e.g. `https://gist.github.com/yasirkula/dfc43134` — then hit **Add**.
3. The tool calls the GitHub API once to parse author/description/file list/raw URLs and writes them to `{targetRoot}/gists.json` (default `Packages/gist-scripts/gists.json`). **No files are downloaded yet.**
4. Toggle **Enable whole package** per gist as desired, then click **Sync All** (or `Ctrl/Cmd+Shift+G`). The tool downloads the `enabled` files. Files whose `sha256` matches the manifest are okay to rewrite; mismatched (locally edited) files are skipped. To force, use Overwrite in the file row (with confirmation).
5. Disable a gist any time by unticking its package checkbox — the whole directory (`.cs`, `.meta`, asmdefs) moves to `Backup~/Disabled/`. Enable to move back, byte-for-byte.

## Settings

**Edit → Project Settings → MUD → Gist Manager**

| Setting | Description |
| --- | --- |
| Interface Language | System (auto) / 中文 / English. Default: `Application.systemLanguage`. |
| Target Root | `Packages/gist-scripts` (embedded UPM package) or `Assets/GistScripts` (plain assets). Switch moves the entire tree atomically; failed relocation rolls back completely; if the destination is occupied (or same-named `gists.json` / `package.json` / `.meta` exists), the move aborts with zero changes. |
| Manifest path | Shown for reference; lives at `{targetRoot}/gists.json`. |
| GitHub Token | Stored in `EditorPrefs` (machine-local). Anonymous calls limited to 60/hour; authenticated 5,000/hour. |
| Check upstream on sync | Fetches upstream metadata once per entry during Sync and reports `Upstream updated:` if file list or `rawUrl` differs. Does not mutate the manifest. |

## Manifest

```json
{
  "targetRoot": "Packages/gist-scripts",
  "gists": [
    {
      "id": "dfc43134fbfefb820d0adbc5d7c25fb3",
      "author": "yasirkula",
      "description": "Unitypackage Extractor",
      "packageDir": "yasirkula.unitypackage-extractor",
      "files": [
        {
          "filename": "UnitypackageExtractor.cs",
          "targetPath": "Packages/gist-scripts/yasirkula.unitypackage-extractor/Editor/UnitypackageExtractor.cs",
          "enabled": true,
          "rawUrl": "https://gist.githubusercontent.com/yasirkula/dfc43134/raw/UnitypackageExtractor.cs",
          "placement": "Editor",
          "sha256": "…"
        }
      ]
    }
  ]
}
```

`gists[].enabled` is the only on/off switch; per-file toggles do not exist.

## Authors

Hierarchy in the window groups by `author`. Anonymous URLs yield `author = anonymous`.

## Limitations

- Generated asmdef `references` is empty; add cross-assembly references manually if needed.
- Auto placement inspects for `UnityEditor` usage — non-editor scripts can still land in Editor if needed; use the file row's placement dropdown.
- Disable moves the entire gist directory into `Backup~/Disabled/<packageDir>` (ignored by Unity); enable moves it back. Local modifications are preserved.
- Sync **does not** add new upstream files; it only downloads files already in the manifest. To add, call Add with the new URL.
- Filenames removed upstream report `skipped` and the manifest entry remains.

## License

MIT. See [LICENSE](LICENSE).
