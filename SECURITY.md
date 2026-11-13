# Security policy

## Reporting a vulnerability

Please **do not** open a public issue for security problems. Use GitHub's private reporting instead:
[Report a vulnerability](https://github.com/EpicMorg/Hammer-MCP/security/advisories/new).

Include the Hammer-MCP version (title of the main window or *Help → About*), what you did and what happened.
We will answer as soon as we can and agree on a fix and disclosure with you.

## Scope worth reporting

- the MCP endpoint answering without a valid Bearer token when it listens on a non-localhost address;
- an agent getting more than its mode allows (live edits in *Basic* mode, menu commands, commands outside the
  `source2_console` allowlist, file access outside map / game files);
- anything that writes to, hooks or injects into a **game process** (Hammer-MCP must never do that - see
  *Safety and VAC* in the README).

## Supported versions

Only the latest release gets fixes.
