# Installation

## Install from Git URL

```
Window → Package Manager → + → Install package from git URL...
https://github.com/MUDV587/com.mud.modules.gistmanager.git
```

Requires Unity **2022.3 LTS** (UI Toolkit). The only package dependency is
`com.unity.nuget.newtonsoft-json` (3.0.2) — the Unity Package Manager
resolves it automatically.

## Install via manifest.json

Add the following to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.mud.modules.gistmanager": "https://github.com/MUDV587/com.mud.modules.gistmanager.git"
  }
}
```

## Pin to a tag

```json
"com.mud.modules.gistmanager": "https://github.com/MUDV587/com.mud.modules.gistmanager.git#v0.3.0"
```

## What gets created

```
Packages/com.mud.modules.gistmanager/    ← the package itself
Packages/gist-scripts/                   ← default target root
  gists.json                             ← auto-created on first Sync
  package.json                            ← auto-created embedded-package marker (Packages targetRoot)
```

The `Backup~` directory at the target root is Unity-ignored and is where
backups live. CI may safely `**/Backup~/` the directory away.

## Resolving Git URL failures

If the install fails with `Package Manager error`, check:

1. Unity can reach `github.com` (proxy / firewall).
2. The `.git` URL is correct.
3. The `auth` is right — anonymous GitHub requests are rate-limited to
   60/hour. Add a GitHub token in **Project Settings → MUD → Gist Manager
   → GitHub Token** to lift the limit to 5,000/hour.
