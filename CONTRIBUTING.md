# Contributing

Thanks for helping! Hammer-MCP talks to many Hammer builds, so reports from real setups are the most valuable thing.

## Report a Hammer build we do not know

1. Attach Hammer-MCP to your Hammer and press **Start**.
2. Ask your agent for `hammer_status` (or look at *Meta info*). For an unknown build it shows `editorSha256`,
   the editor DLL path and whether the shell channel exists.
3. Open an issue with the **New Hammer build** template and paste that, plus the game / mod and where it came from.

Builds are recognised by the sha256 of the editor DLL (`hammer_dll.dll`, `hammerplusplus_dll.dll`, Source 2
`tools/hammer.dll`) or of the exe for GoldSrc editors; they live in [`src/registry/hammers.json`](src/registry/hammers.json).
A copy of `hammers.json` next to `hammer-mcp.exe` overrides the built-in one, so you can try an entry before a release.

## Report a bug

Use the **Bug report** template. The *Logs* tab of the app (and `hammer_status`) usually tells us most of it.
Please do not attach maps you are not allowed to share.

## Code

- .NET 10 SDK; `cd src && dotnet build HammerMcp.sln && dotnet test --solution HammerMcp.sln` (builds on Linux too).
- Branch from `develop`, keep pull requests small, add a test for parsers and validators.
- No Valve source code (leaked or otherwise) and no third-party binaries in the repo; the editors are reached only through
  their built-in interfaces (shell channel, menu commands, files, the Source 2 developer console).
- Never write to, hook or inject into a game process (see *Safety and VAC* in the README and `InjectPolicy`).
