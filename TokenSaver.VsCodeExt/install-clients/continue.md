# Wiring up Continue

Prerequisite: read `INSTALL_PROMPT.md`'s "Values you need" section first (port, HTTP endpoint,
stdio path, health check) if you haven't already.

Continue reads, in order of precedence: `~/.continue/config.yaml` (current format),
`~/.continue/config.json` (legacy), or any `.yaml` file dropped into `~/.continue/mcpServers/`.
Open whichever of `config.yaml` / `config.json` already exists; if neither does, create
`~/.continue/config.yaml`.

## Option A — httpStreaming (recommended default; requires the server already running)

`config.yaml`:

```yaml
mcpServers:
  - name: TokenSaver
    type: streamable-http
    url: http://localhost:11436/mcp
    requestOptions:
      headers:
        Authorization: Bearer <TOKEN>
```

`config.json`:

```json
{
  "mcpServers": {
    "TokenSaver": {
      "type": "streamable-http",
      "url": "http://localhost:11436/mcp",
      "requestOptions": { "headers": { "Authorization": "Bearer <TOKEN>" } }
    }
  }
}
```

## Option B — stdio (Continue launches and owns the process; needs a real installed exe)

`config.yaml`:

```yaml
mcpServers:
  - name: TokenSaver
    command: "C:\\Program Files\\TokenSaver\\TokenSaver.MCP.exe"
    args: []
```

`config.json`:

```json
{
  "mcpServers": {
    "TokenSaver": {
      "command": "C:\\Program Files\\TokenSaver\\TokenSaver.MCP.exe",
      "args": []
    }
  }
}
```

Restart Continue (reload the VS Code window) afterward.

See `INSTALL_PROMPT.md`'s "Verifying it worked" section for the general fallback checklist if
this doesn't connect.
