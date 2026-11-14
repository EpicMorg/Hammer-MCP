# Adapter ↔ server protocol (named pipe)

Pipe `\\.\pipe\hammer-mcp-adapter-<hammer pid>`, created by the adapter (server side of the pipe, local clients only);
the Hammer-MCP server connects as the client. One UTF-8 JSON object per line (`\n`). Either side may send at any time.

- Messages **to the adapter** have `"cmd"`, messages **from the adapter** have `"type"`.
- A request that expects an answer carries `"id"` (any string); the answer repeats it.
- Unknown fields are ignored; unknown `cmd` / `type` → `{"type":"error","message":...}` (adapter) or ignored (server).

## Layer 0 — lifecycle (implemented)

| direction | message | meaning |
|---|---|---|
| adapter → | `{"type":"hello","adapter":"x64","version":"0.1.0","pid":N,"build":"hpp-tf2-8873-x64","support":"menu","editorSha256":"…"}` | first line after connect; `adapter` = bitness of the DLL, `build` = registry id it found, `support` = registry `injectAdapter` level |
| → adapter | `{"cmd":"ping"}` | heartbeat (server: every ~5 s; no pong for ~15 s = adapter gone) |
| adapter → | `{"type":"pong"}` | |
| → adapter | `{"cmd":"unload"}` | adapter answers `{"type":"bye"}`, removes its menu, restores the window procedure and unloads itself |

## Layer 1 — the MCP menu in Hammer

The adapter adds an **MCP** menu to Hammer's main menu bar. Items:

| item | enabled when | on click, adapter sends |
|---|---|---|
| Agent: active / **paused** (check mark = paused) | always | `{"type":"menu","item":"pause"}` or `"resume"` |
| Send selection to agent | layer 2 (greyed until then) | `{"type":"selection","items":[…]}` (layer 2) |
| Ask agent… | always | dialog with a text box → `{"type":"ask","text":"…"}` |
| Pending changes (N)… | approval mode is `ask` and N > 0 | `{"type":"menu","item":"pending","id":"…"}` → server answers `{"cmd":"pending","id":"…","items":[{"id":"c1","text":"create light …"}]}` → dialog → `{"type":"approval","apply":["c1"],"reject":["c2"]}` |
| Undo agent's last step | server says `canUndo` | `{"type":"menu","item":"undo_agent_step"}` (the server reverts its own last edit group) |
| Activity log… | always | `{"type":"menu","item":"log","id":"…"}` → server answers `{"cmd":"log","id":"…","lines":["12:01 create light …", …]}` (last ~200) → adapter shows them |
| Status… | always | shows the last `state` (no message) |
| Disconnect adapter | always | `{"type":"menu","item":"disconnect"}` → the server sends `unload` |
| About Hammer-MCP adapter… | always | no message: icon, adapter version, server version (from `state`), build id + first 12 hex of sha256, state, links |

The server pushes its state right after `hello` and whenever it changes; the adapter uses it for check marks, enabling,
the pending counter, Status and About:

```json
{"cmd":"state","server":"0.6.0","mode":"Auto","agent":"active","approval":"auto","pending":0,"canUndo":true}
```

- `agent`: `active` | `paused` — paused means the server refuses every edit from the agent (shell channel too).
- `approval`: `auto` | `ask` (approval mode; `pending` = changes waiting for the user).
- The server may also send `{"cmd":"notify","text":"…"}` — the adapter flashes the Hammer window and shows the text in the
  status of the MCP menu (no modal popups from the server side).

The adapter never decides anything itself: pause, approval and undo live in the server, the menu only asks for them.
