"""Stand-in for the Hammer-MCP server while the C# client is not ready: talks to an injected adapter over its pipe.
Windows only:  python src/native/tools/pipe_console.py <hammer pid>
Prints everything the adapter sends; answers the MCP menu (pause/resume, chat, live log, pending, approval, undo,
disconnect), sends a profile after hello and pushes a state, so every menu item and window can be tried in Hammer.
Chat: every user line is echoed and answered by a fake agent a second later. Ctrl+C to quit (the adapter stays loaded)."""
import json, sys, time

pid = int(sys.argv[1])
path = rf"\\.\pipe\hammer-mcp-adapter-{pid}"
for attempt in range(50):            # the adapter creates the pipe a moment after the injection
    try:
        pipe = open(path, "r+b", buffering=0)
        break
    except (FileNotFoundError, OSError):
        time.sleep(0.1)
else:
    sys.exit(f"no adapter pipe {path} (is the adapter injected?)")
state = {"cmd": "state", "server": "pipe_console", "mode": "Auto", "agent": "active", "approval": "ask", "pending": 2, "canUndo": True}
pending = [{"id": "c1", "text": "create light \"corner_light_1\" at 128 64 96"}, {"id": "c2", "text": "set mcp_relay.OnTrigger = \"door1,Open,,0,-1\""}]
log = [time.strftime("%H:%M ") + "pipe_console connected"]
chat = []

def now(fmt="%H:%M:%S"):
    return time.strftime(fmt)

def send(obj):
    pipe.write((json.dumps(obj) + "\n").encode("utf-8"))
    print(">>", obj)

def add_log(text):
    line = now("%H:%M ") + text
    log.append(line); send({"cmd": "log_line", "line": line})

def say(frm, text):
    item = {"from": frm, "text": text, "at": now()}
    chat.append(item); send({"cmd": "chat", **item})

buf = b""
while True:
    try:
        chunk = pipe.read(4096)
    except OSError:                  # the adapter closed the pipe (bye / unload)
        chunk = b""
    if not chunk:
        print("pipe closed"); break
    buf += chunk
    while b"\n" in buf:
        line, buf = buf.split(b"\n", 1)
        msg = json.loads(line.decode("utf-8"))
        print("<<", msg)
        t, item = msg.get("type"), msg.get("item")
        if t == "hello":
            send({"cmd": "profile", "build": msg.get("build", ""), "support": msg.get("support", "menu"), "features": {}})
            send(state)
        elif t == "menu" and item in ("pause", "resume"):
            state["agent"] = "paused" if item == "pause" else "active"; add_log(f"agent {state['agent']}"); send(state)
        elif t == "menu" and item == "log":
            send({"cmd": "log", "id": msg.get("id"), "lines": log})
        elif t == "menu" and item == "pending":
            send({"cmd": "pending", "id": msg.get("id"), "items": pending})
        elif t == "approval":
            decided = set(msg.get("apply", [])) | set(msg.get("reject", []))
            add_log(f"applied {msg.get('apply')} rejected {msg.get('reject')}")
            pending[:] = [p for p in pending if p["id"] not in decided]; state["pending"] = len(pending)
            send(state); send({"cmd": "notify", "text": "approval received"})
        elif t == "menu" and item == "undo_agent_step":
            add_log("undo requested"); state["canUndo"] = False; send(state)
        elif t in ("chat", "ask"):
            text = msg.get("text", "")
            say("user", text); add_log("user wrote: " + text)
            time.sleep(1); say("agent", f"(pipe_console) got it: {text!r}. Line 2 of the answer\nshows that new lines work.")
        elif t == "menu" and item == "chat_history":
            send({"cmd": "chat_history", "id": msg.get("id"), "items": chat[-200:]})
        elif t == "menu" and item == "disconnect":
            send({"cmd": "unload"})
