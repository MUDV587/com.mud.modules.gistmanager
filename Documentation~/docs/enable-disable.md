# Enable & Disable

![Enable](images/enable-en.png)

## Semantics

Each gist has one toggle — **Enable whole package** — that applies to the
entire gist directory (files, `.meta`, `asmdef`, subfolders).

- **Enabled** — files on disk match the target path; Sync downloads them.
- **Disabled** — the whole gist directory is moved into `Backup~/Disabled/<packageDir>`. Unity ignores the `~` folder, so disabled gists don't compile or appear in the project. The `.meta` is moved along, preserving GUIDs.

## Safe by construction

The tool **moves** directories, it doesn't copy them. That means:

1. Local modifications are preserved byte-for-byte regardless of what
   the tool writes next.
2. After re-enable, AssetDatabase sees exactly the directories that existed
   before — the Unity compiler finds the scripts, asmdefs pick them up,
   no GUID churn, no broken references.
3. Disabling never touches `Backup~/` — it's treated as a staging area
   reserved for disabled packages.

## Enable vs Disable via Sync

Sync will download **only enabled** files. Disabled gists are not touched
(but they are reported as `empty` since there's nothing to do for them).

## Backup location

`{targetRoot}/Backup~/Disabled/<packageDir>`. The whole Backup~ directory
is Unity-ignored; CI may safely skip it.
