# Wiring up Cursor

Prerequisite: read `INSTALL_PROMPT.md`'s "Values you need" section first (port, HTTP endpoint,
stdio path, health check) if you haven't already.

Config file: `~/.cursor/mcp.json` (preferred) or `%APPDATA%\Cursor\User\mcp.json` — root key
`mcpServers`, bare URL entry (no `type` field needed).

```json
{
  "mcpServers": {
    "TokenSaver": {
      "url": "http://localhost:11436/mcp"
    }
  }
}
```

Restart Cursor afterward. Verify in Cursor's MCP settings panel that the server shows as
connected.

See `INSTALL_PROMPT.md`'s "Verifying it worked" section for the general fallback checklist if
this doesn't connect.
