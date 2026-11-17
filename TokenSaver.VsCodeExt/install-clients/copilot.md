# Wiring up VS Code Copilot Chat (and Cline / Roo-Code, which share the same file)

Prerequisite: read `INSTALL_PROMPT.md`'s "Values you need" section first (port, HTTP endpoint,
stdio path, health check) if you haven't already.

Config file: `%APPDATA%\Code\User\mcp.json` (Windows global) — root key is **`servers`**, not
`mcpServers`. Alternatively, a workspace-local `.vscode/mcp.json` also works and takes
precedence for that workspace.

```json
{
  "servers": {
    "TokenSaver": {
      "type": "http",
      "url": "http://localhost:11436/mcp",
      "headers": { "Authorization": "Bearer <TOKEN>" }
    }
  }
}
```

Reload the window afterward. Verify via the Copilot Chat tools/MCP panel that `TokenSaver`
shows connected, then run one real tool call to confirm.

See `INSTALL_PROMPT.md`'s "Verifying it worked" section for the general fallback checklist if
this doesn't connect.
