# Wiring up Sixth AI

Prerequisite: read `INSTALL_PROMPT.md`'s "Values you need" section first (port, HTTP endpoint,
stdio path, health check) if you haven't already.

Config file: `%APPDATA%\Code\User\globalStorage\sixth.sixth-ai\settings\sixth-mcp-settings.json`
(Sixth has its own settings file — it does **not** use the shared VS Code `mcp.json`) — root
key `mcpServers`. The `"disabled": false` field is required; Sixth ignores entries without it.

## Option A — stdio (Sixth manages the process directly, no separate server needed)

```json
{
  "mcpServers": {
    "TokenSaver": {
      "command": "C:\\Program Files\\TokenSaver\\TokenSaver.MCP.exe",
      "args": [],
      "disabled": false
    }
  }
}
```

## Option B — HTTP (against an already-running server)

```json
{
  "mcpServers": {
    "TokenSaver": {
      "url": "http://localhost:11436/mcp",
      "disabled": false
    }
  }
}
```

See `INSTALL_PROMPT.md`'s "Verifying it worked" section for the general fallback checklist if
this doesn't connect.
