"""Dumps the UI of a Hammer build from its PE resources into registry/ui/<build id>.json: menus, accelerators,
toolbars, dialogs (every control with its id) and the status / tooltip text of each command. Nothing is run - the
file is only read. Live-only knowledge (lists Hammer fills at run time, verified procedures) goes into the same file
by hand under "recipes"; this script keeps that part when it rewrites the file.

    python dump_ui.py [--public] <editor exe or dll> <build id> [out dir]      (needs: pip install pefile)

--public (what goes into this repository): only ids, classes, keys and short labels. Valve's own prose - status
line texts, long tooltips / control texts, message box texts, copyright / trademark lines - is dropped; the full
dump stays in the private workspace.
"""
import hashlib, json, os, re, struct, sys

import pefile

RT_MENU, RT_DIALOG, RT_STRING, RT_ACCEL, RT_TOOLBAR = 4, 5, 6, 9, 241
ATOMS = {0x80: "Button", 0x81: "Edit", 0x82: "Static", 0x83: "ListBox", 0x84: "ScrollBar", 0x85: "ComboBox"}
VK = {0x08: "Backspace", 0x09: "Tab", 0x0D: "Enter", 0x1B: "Esc", 0x20: "Space", 0x21: "PageUp", 0x22: "PageDown",
      0x23: "End", 0x24: "Home", 0x25: "Left", 0x26: "Up", 0x27: "Right", 0x28: "Down", 0x2D: "Insert", 0x2E: "Delete",
      0x6A: "Num*", 0x6B: "Num+", 0x6D: "Num-", 0x6E: "Num.", 0x6F: "Num/", 0xBA: ";", 0xBB: "=", 0xBC: ",", 0xBD: "-",
      0xBE: ".", 0xBF: "/", 0xC0: "`", 0xDB: "[", 0xDC: "\\", 0xDD: "]", 0xDE: "'"}


def resources(pe, type_id):
    """(resource id, bytes) of one type, first language of each"""
    for t in getattr(pe, "DIRECTORY_ENTRY_RESOURCE", None).entries if hasattr(pe, "DIRECTORY_ENTRY_RESOURCE") else []:
        if t.id != type_id:
            continue
        for n in t.directory.entries:
            lang = n.directory.entries[0]
            rid = n.id if n.id is not None else str(n.name)
            yield rid, pe.get_data(lang.data.struct.OffsetToData, lang.data.struct.Size)


def wstr(d, i):
    """null-terminated UTF-16 string at i -> (text, next offset)"""
    j = i
    while d[j:j + 2] != b"\0\0":
        j += 2
    return d[i:j].decode("utf-16-le", "replace"), j + 2


def sz_or_ord(d, i):
    """dialog sz_Or_Ord field -> (str or int, next offset)"""
    w = struct.unpack_from("<H", d, i)[0]
    if w == 0:
        return "", i + 2
    if w == 0xFFFF:
        return struct.unpack_from("<H", d, i + 2)[0], i + 4
    return wstr(d, i)


def align4(i):
    return (i + 3) & ~3


def parse_menu(d):
    items = []
    version, header = struct.unpack_from("<HH", d, 0)
    if version != 0:
        return items  # MENUEX: not used by the editors seen so far

    def level(i, path):
        while i < len(d):
            flags = struct.unpack_from("<H", d, i)[0]
            i += 2
            if flags & 0x10:  # MF_POPUP
                text, i = wstr(d, i)
                name = text.split("\t")[0].replace("&", "")
                i = level(i, path + [name])
            else:
                cid = struct.unpack_from("<H", d, i)[0]
                i += 2
                text, i = wstr(d, i)
                if cid or text:
                    name, _, key = text.partition("\t")
                    item = {"path": "/".join(path + [name.replace("&", "")]), "id": "0x%04X" % cid}
                    if key:
                        item["key"] = key
                    items.append(item)
            if flags & 0x80:  # MF_END
                return i
        return i

    level(4 + header, [])
    return items


def parse_dialog(d):
    ex = struct.unpack_from("<HH", d, 0) == (1, 0xFFFF)
    if ex:
        _, _, _, _, style = struct.unpack_from("<HHIII", d, 0)
        count = struct.unpack_from("<H", d, 16)[0]
        i = 26
    else:
        style, _, count = struct.unpack_from("<IIH", d, 0)
        i = 18
    _, i = sz_or_ord(d, i)  # menu
    _, i = sz_or_ord(d, i)  # class
    title, i = wstr(d, i)
    if style & 0x40:  # DS_SETFONT
        i += 6 if ex else 2
        _, i = wstr(d, i)
    controls = []
    for _ in range(count):
        i = align4(i)
        if ex:
            _, _, cstyle, _, _, _, _, cid = struct.unpack_from("<IIIhhhhI", d, i)
            i += 24
        else:
            cstyle, _, _, _, _, _, cid = struct.unpack_from("<IIhhhhH", d, i)
            i += 18
        cls, i = sz_or_ord(d, i)
        text, i = sz_or_ord(d, i)
        extra = struct.unpack_from("<H", d, i)[0]
        i += 2 + extra
        cls = ATOMS.get(cls, cls) if isinstance(cls, int) else cls
        c = {"id": cid if cid != 0xFFFF else -1, "class": cls}
        if cls == "Button":
            kind = {2: "check", 3: "check", 4: "radio", 5: "check", 6: "check", 7: "group", 9: "radio"}.get(cstyle & 0xF)
            if kind:
                c["kind"] = kind
        if isinstance(text, str) and text:
            c["text"] = text
        controls.append(c)
    return {"title": title, "controls": controls}


def parse_strings(pe):
    out = {}
    for rid, d in resources(pe, RT_STRING):
        i = 0
        for k in range(16):
            n = struct.unpack_from("<H", d, i)[0]
            i += 2
            if n:
                out[(rid - 1) * 16 + k] = d[i:i + 2 * n].decode("utf-16-le", "replace")
            i += 2 * n
    return out


def key_name(flags, key):
    mods = ("Ctrl+" if flags & 0x08 else "") + ("Shift+" if flags & 0x04 else "") + ("Alt+" if flags & 0x10 else "")
    if flags & 0x01:  # FVIRTKEY
        if 0x30 <= key <= 0x5A:
            k = chr(key)
        elif 0x70 <= key <= 0x87:
            k = "F%d" % (key - 0x6F)
        elif 0x60 <= key <= 0x69:
            k = "Num%d" % (key - 0x60)
        else:
            k = VK.get(key, "VK_0x%02X" % key)
    else:
        k = chr(key)
    return mods + k


def dump(path, build):
    pe = pefile.PE(path, fast_load=False)
    strings = parse_strings(pe)
    menus = [{"menu": rid, **item} for rid, d in resources(pe, RT_MENU) for item in parse_menu(d)]
    accels = []
    for rid, d in resources(pe, RT_ACCEL):
        for i in range(0, len(d) - 7, 8):
            flags, key, cid, _ = struct.unpack_from("<HHHH", d, i)
            accels.append({"table": rid, "key": key_name(flags, key), "id": "0x%04X" % cid})
    toolbars = []
    for rid, d in resources(pe, RT_TOOLBAR):
        version, w, h, count = struct.unpack_from("<HHHH", d, 0)
        ids = struct.unpack_from("<%dH" % count, d, 8)
        toolbars.append({"resource": rid, "buttons": ["0x%04X" % c if c else "-" for c in ids]})
    dialogs = []
    for rid, d in resources(pe, RT_DIALOG):
        try:
            dialogs.append({"resource": rid, **parse_dialog(d)})
        except struct.error as e:
            dialogs.append({"resource": rid, "error": str(e)})
    # status line / tooltip ("status\ntooltip") of every command that a menu, toolbar or accelerator uses
    used = {int(x["id"], 16) for x in menus} | {int(a["id"], 16) for a in accels} | \
           {int(b, 16) for t in toolbars for b in t["buttons"] if b != "-"}
    commands = {}
    for cid in sorted(used):
        s = strings.get(cid)
        if s:
            status, _, tip = s.partition("\n")
            commands["0x%04X" % cid] = {k: v for k, v in (("tip", tip), ("status", status)) if v}
    with open(path, "rb") as f:
        sha = hashlib.sha256(f.read()).hexdigest()
    return {"build": build, "file": os.path.basename(path), "sha256": sha, "menus": menus, "accelerators": accels,
            "toolbars": toolbars, "commands": commands, "dialogs": dialogs}


SHORT = 40  # longest label kept by --public
LEGAL = re.compile(r"copyright|trademark|all rights reserved|©|\(c\)", re.I)


def short(text):
    return isinstance(text, str) and len(text) <= SHORT and "\n" not in text and not LEGAL.search(text)


def public(doc):
    """drops Valve's prose, keeps ids / classes / keys / short labels (see --public)"""
    for m in doc.get("menus", []):
        if not short(m.get("path", "").rsplit("/", 1)[-1]):
            m["path"] = m["path"].rsplit("/", 1)[0] + "/" + m["id"]
    commands = {}
    for cid, c in doc.get("commands", {}).items():
        if short(c.get("tip")):
            commands[cid] = {"tip": c["tip"]}
    doc["commands"] = commands
    for d in doc.get("dialogs", []):
        if "title" in d and not short(d["title"]):
            d["title"] = ""
        for c in d.get("controls", []):
            if "text" in c and not short(c["text"]):
                del c["text"]

    def scrub(n):  # recipes: message box texts quoted from the editor
        if isinstance(n, dict):
            for k in list(n):
                if k == "message" and isinstance(n[k], str):
                    n[k] = True
                else:
                    scrub(n[k])
        elif isinstance(n, list):
            for x in n:
                scrub(x)
    scrub(doc.get("recipes"))
    doc["public"] = True
    return doc


def write(doc, out, is_public=False):
    """one entry per line: diffable, still short"""
    # hand-written parts survive a re-dump: "recipesFrom" (a build with the same UI whose recipes apply) and "recipes"
    if os.path.exists(out):
        with open(out, encoding="utf-8") as f:
            old = json.load(f)
        for k in ("recipesFrom", "recipes"):
            if k in old:
                doc[k] = old[k]
    if is_public:
        public(doc)
    lines = ["{"]
    keys = list(doc)
    for n, k in enumerate(keys):
        v = doc[k]
        end = "," if n < len(keys) - 1 else ""
        if isinstance(v, list):
            lines.append('  "%s": [' % k)
            lines += ["    " + json.dumps(x, ensure_ascii=False) + ("," if j < len(v) - 1 else "") for j, x in enumerate(v)]
            lines.append("  ]" + end)
        elif isinstance(v, dict) and k in ("commands", "recipes"):
            lines.append('  "%s": {' % k)
            items = list(v.items())
            lines += ['    %s: %s%s' % (json.dumps(a), json.dumps(b, ensure_ascii=False), "," if j < len(items) - 1 else "")
                      for j, (a, b) in enumerate(items)]
            lines.append("  }" + end)
        else:
            lines.append('  %s: %s%s' % (json.dumps(k), json.dumps(v, ensure_ascii=False), end))
    lines.append("}")
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if a != "--public"]
    is_public = len(args) < len(sys.argv) - 1
    if len(args) < 2:
        sys.exit(__doc__)
    exe, build = args[0], args[1]
    out_dir = args[2] if len(args) > 2 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "ui")
    os.makedirs(out_dir, exist_ok=True)
    out = os.path.join(out_dir, build + ".json")
    doc = dump(exe, build)
    write(doc, out, is_public)
    print("%s: %d menu items, %d accelerators, %d toolbar buttons, %d dialogs, %d commands with text" % (
        out, len(doc["menus"]), len(doc["accelerators"]),
        sum(len(t["buttons"]) for t in doc["toolbars"]), len(doc["dialogs"]), len(doc["commands"])))
