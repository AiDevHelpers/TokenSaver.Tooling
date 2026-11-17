# Wiring up Claude Code

Prerequisite: read `INSTALL_PROMPT.md`'s "Values you need" section first (port, HTTP endpoint,
stdio path, health check) if you haven't already.

Config file: `~/.claude.json` (global, applies across all projects) — root key `mcpServers`.
A project-level `.mcp.json` also works if you only want it scoped to one repo.

```json
{
  "mcpServers": {
    "TokenSaver": {
      "type": "http",
      "url": "http://localhost:11436/mcp",
      "headers": { "Authorization": "Bearer <TOKEN>" }
    }
  }
}
```

Restart Claude Code (or reload) afterward. Verify with `/mcp` inside Claude Code, or by asking
it to list available tools and confirming TokenSaver's tools appear.

See `INSTALL_PROMPT.md`'s "Verifying it worked" section for the general fallback checklist if
this doesn't connect.
