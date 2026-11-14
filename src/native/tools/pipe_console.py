"""Stand-in for the Hammer-MCP server while the C# client is not ready: talks to an injected adapter over its pipe.
Windows only:  python src/native/tools/pipe_console.py <hammer pid>
Prints everything the adapter sends; answers the MCP menu (pause/resume, log, pending, approval, undo, disconnect)
and pushes a state, so every menu item can be clicked in Hammer. Ctrl+C to quit (the adapter stays loaded)."""
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

def send(obj):
    pipe.write((json.dumps(obj) + "\n").encode("utf-8"))
    print(">>", obj)

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
            send(state)
        elif t == "menu" and item in ("pause", "resume"):
            state["agent"] = "paused" if item == "pause" else "active"; log.append(time.strftime("%H:%M ") + f"agent {state['agent']}"); send(state)
        elif t == "menu" and item == "log":
            send({"cmd": "log", "id": msg.get("id"), "lines": log})
        elif t == "menu" and item == "pending":
            send({"cmd": "pending", "id": msg.get("id"), "items": pending})
        elif t == "approval":
            log.append(time.strftime("%H:%M ") + f"applied {msg.get('apply')} rejected {msg.get('reject')}")
            pending.clear(); state["pending"] = 0; send(state); send({"cmd": "notify", "text": "approval received"})
        elif t == "menu" and item == "undo_agent_step":
            log.append(time.strftime("%H:%M ") + "undo requested"); state["canUndo"] = False; send(state)
        elif t == "ask":
            log.append(time.strftime("%H:%M ") + "user asked: " + msg.get("text", "")); send({"cmd": "notify", "text": "request queued for the agent"})
        elif t == "menu" and item == "disconnect":
            send({"cmd": "unload"})
