# MUD Gist Manager — Design

See [`docs/design.md`](docs/design.md) for the full design documentation.

This file exists at the head of `Documentation~/` for parity with other
MUD packages and to give AI agents a short entry point.

## TL;DR

- Package: `com.mud.modules.gistmanager` (editor-only, Unity 2022.3 LTS)
- Storage:       `gists.json` (manifest), `Backup~/` (backups)
- Operations:    Add (no download), Sync (download + sha256 check),
                Enable/Disable (move gist directory), Target Root switch
                (move everything)
- Failure mode:  local modifications are never overwritten; if the
                 manifest is corrupt the GUI keeps working but refuses
                 to overwrite the file
- Considerations: opt-in network calls only; Local modify protected by
                  sha256; backup-or-nothing philosophy for destructive
                  operations
