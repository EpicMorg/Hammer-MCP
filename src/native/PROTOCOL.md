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
| Chat with the agent… (was "Ask agent…" before layer 1.1) | always | opens the chat window, see Layer 1.1 |
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

## Layer 1.1 — chat, live log, approval list, build profile (agreed 2026-11-14)

**Chat replaces "Ask agent…"**: the menu item **Chat…** opens a tool window (a plain window owned by Hammer, opened from the menu; docking into Hammer's side panels comes with the MFC work later)
where the user writes to the agent and sees its answers.

| direction | message | meaning |
|---|---|---|
| adapter → | `{"type":"chat","text":"…"}` | the user wrote in the chat (old `{"type":"ask"}` is still accepted, same meaning) |
| → adapter | `{"cmd":"chat","from":"user"\|"agent","text":"…","at":"HH:mm:ss"}` | a chat line to show; the server echoes the user's own lines too, so every window shows the same history |
| adapter → | `{"type":"menu","item":"chat_history","id":"…"}` | sent when the chat window opens |
| → adapter | `{"cmd":"chat_history","id":"…","items":[{"from":"user","text":"…","at":"…"}, …]}` | last ~200 lines |

The agent gets the user's lines through `user_requests` / `hammer_status` as before and answers with a new MCP tool
(`say_to_user(text)`), which the server turns into `{"cmd":"chat","from":"agent",…}`.

**Live activity log**: **Activity log…** opens a tool window; it asks once (`{"type":"menu","item":"log","id":…}` →
`{"cmd":"log",…}` as before) and then the server pushes every new line while connected:
`{"cmd":"log_line","line":"12:01 create light …"}` (the adapter keeps the newest ~500).

**Approval list**: **Pending changes (N)…** opens a list with a check box per change (like the app's window):
Apply selected / Apply all / Reject all → the same `{"type":"approval","apply":[…],"reject":[…]}`.

**Build profile**: right after `hello` the server sends the profile of the build the adapter reported (from its registry,
which a `registry/hammers.json` next to `hammer-mcp.exe` overrides):
`{"cmd":"profile","build":"hpp-tf2-8873-x64","support":"menu","features":{}}`. The adapter uses it instead of the
copy embedded in its resources (the fallback when no server sent one). `features` (signatures / offsets per feature)
arrive with layer 2; until then it is empty.

All adapter windows (chat, log, approval, status, about) show the Hammer-MCP icon in the title and in the body.

**Adapter side (implemented 2026-11-14):**
- Menu items carry icons (glyphs of Segoe Fluent Icons / Segoe MDL2 Assets in the menu text colour; About = the Hammer-MCP logo); no icon font (Windows 7/8) = text-only items.
- A chat line `from:"agent"` opens the chat window if it is closed, without taking the focus from the map. The user's own line shows up only when the server echoes it.
- Enter sends, Shift+Enter is a new line, Esc closes a window, Tab moves between controls. Keys typed in adapter windows never reach Hammer's accelerators (a `WH_GETMESSAGE` hook handles them first), so Delete / Ctrl+Z in the chat do not touch the map.
- Pending list: **Apply selected** applies the checked rows and leaves the unchecked ones pending; **Later** closes the window. While the list is open, a `state` with another `pending` count makes the adapter ask for the list again.
- `profile` for a `build` other than the one in `hello` is ignored; `support` from the profile replaces the built-in level (Status shows "profile from the server").
- Activity log and chat keep the newest 500 / 200 lines.
