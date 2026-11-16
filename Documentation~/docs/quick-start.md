# Quick Start

## 1. Install

```
Window → Package Manager → + → Install package from git URL...
https://github.com/MUDV587/com.mud.modules.gistmanager.git
```

## 2. Open the window

Menu: **Tools → MUD → Gist Manager**

## 3. Add your first gist

Paste a Gist URL into the **+** bar's input panel (click **+** or press
**Ctrl/Cmd+F** to focus search first):

```
https://gist.github.com/yasirkula/dfc43134
```

The window parses the URL, fetches the gist's file list (one API call),
and writes the result into `Packages/gist-scripts/gists.json`. **Nothing is
downloaded yet.**

## 4. Sync

Click **Sync All** (or **Ctrl/Cmd+Shift+G**). The tool downloads every
enabled gist's files into the target root. Files whose content differs from
the recorded `sha256` are **skipped**, not overwritten — a force option in
the detail pane's file editor panel (**Overwrite**, behind a confirmation
dialog) exists for explicit force.

## 5. Enable / Disable

Tick the package checkbox to disable the whole package: the whole gist
directory — `.cs`, `.meta`, asmdef, everything — moves into
`Packages/gist-scripts/Backup~/Disabled/<packageDir>`, visible only to the
tool. Unity ignores it. Re-enabling moves it back identically.

## 6. Verify in CI

```bash
# Example: download every enabled gist file by reading gists.json
MANIFEST=$(grep -oP '^manifestRelativePath:\s*\K.+' ProjectSettings/GistManager.asset | head -1)
MANIFEST=${MANIFEST:-Packages/gist-scripts/gists.json}
# …iterate enabled files, curl `rawUrl`, write to `targetPath`
```

`Packages/gist-scripts/gists.json` is your single source of truth —
commit it, .gitignore generated artifacts, restore with CI or one Sync.
