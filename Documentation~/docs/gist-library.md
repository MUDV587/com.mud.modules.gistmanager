# Gist Library

![Library](images/library-en.png)

## Sections

- **yasirkula** — grouped by author (auto-collapsible)
- **tomkail** — separate groups always coexist

## Interactions

| Action | Behavior |
| --- | --- |
| Click row | Select |
| Ctrl/Cmd + Click | Toggle selection |
| Shift + Click | Range-select from anchor |
| Up / Down / PageUp / PageDown / Home / End | Move / jump |
| Ctrl/Cmd + F | Focus search |
| `Esc` in the add panel | Dismiss |

## Search

Search (1/3 second debounce) filters by author, gist ID, description,
or filename. Empty query restores all.

## Multi-select

With multiple rows selected, the right pane shows `N gists selected`
and the file editor is hidden; batch operations act on the selection.
