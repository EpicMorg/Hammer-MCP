// Layer 2: reads what Hammer holds in memory (the selection, the 3D camera) by a per-build layout that the server sends
// in its profile ("features": {"activeDoc": "0x…", "doc.selection": "0x…", …}, see PROTOCOL.md). Read-only: every read
// goes through ReadProcessMemory on our own process, so a wrong address gives an error, never a crash. Call on Hammer's
// UI thread (the editor changes these structures there).
#pragma once
#include <string>

namespace live {

// takes the layout from the server's {"cmd":"profile"} line; false = no layout for this build (layer 2 off)
bool Load(const std::string& profileLine);

bool Ready();

// whole JSON lines for the server: {"type":"selection",…} / {"type":"camera",…} or {"type":"error","message":…};
// id (if not empty) is repeated in the answer
std::string Selection(const std::string& id);
std::string Camera(const std::string& id);

}  // namespace live
