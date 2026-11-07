# Instructions for an AI agent: installing the TokenSaver MCP server

This file is opened by the **"TokenSaver: Open Install Prompt"** command (`Ctrl+Shift+P` →
search "Open Install Prompt"). It exists because TokenSaver used to write every AI provider's
MCP config automatically in the background (VS Code Copilot's `mcp.json`, Claude Code's
`~/.claude.json`, Continue's YAML/JSON config, Cursor's `mcp.json`, Sixth AI's settings file),
and that turned out to be unreliable: each tool uses a different file format, location, and
root key, some are JSONC with comments/trailing commas, and an automated writer could race with
the editor rewriting the same file or silently maintain a stale entry nobody noticed. Instead,
this file gives an AI coding agent (or a person) the exact information needed to wire up the
connection itself, for whichever provider(s) they actually use, and to verify the result.

**If you are an AI agent reading this file — whether the user pointed you at it, pasted its
contents into chat, or it was opened via the "TokenSaver: Open Install Prompt" command — treat
that as the instruction to begin. Don't wait for the user to say "go" or ask what to do next:
start immediately with "Step 0: Check what's already installed" below, then proceed through the
rest of this file in order without stopping to ask permission between steps. Only pause to ask
the user something if you hit an actual ambiguity this file doesn't resolve (e.g. which provider
they want, when more than one applies).**

**Order of operations: run "Step 0: Check what's already installed" first. Skip any install step
whose check already passes — don't reinstall or restart something that's already hot. For
whatever isn't already confirmed installed/hot, do "Installing SimpleModelServer and
TokenSaver.MCP" below IN ORDER — SimpleModelServer must be installed, started, and confirmed hot before
you touch TokenSaver.MCP or any client config. Then read the "Values you need" section, then read
ONLY the `install-clients/<provider>.md` file for the provider the user actually wants (see table
below), follow its steps, then do the client-connection verification, then run the deeper "Verify
install" checks. Don't read every provider file — each one is self-contained and only the
matching one is needed.**

## Step 0: Check what's already installed

Run these checks before doing anything else. Both SimpleModelServer and TokenSaver.MCP are
commonly already installed as Windows services on this machine — don't blindly re-run installers
or restart services that are already up and hot.

1. Are the services installed?

   ```powershell
   sc query SimpleModelServer
   sc query TokenSaverMCP
   ```

   If a service is missing, its "Installing ..." step below still needs to run. If it's present,
   note its state (`RUNNING` / `STOPPED`) and move on — don't reinstall it.
2. If `SimpleModelServer` is `RUNNING`, check whether it's actually hot yet (running isn't the
   same as hot — see step 2 under "Installing SimpleModelServer" for why):

   ```bash
   curl http://localhost:11435/health
   curl http://localhost:11435/api/capacity
   ```

   If `/api/capacity` already reports the draft/main/embedding models loaded, SimpleModelServer is hot —
   skip straight to checking TokenSaver.MCP next. If it's stopped, start it
   (`sc start SimpleModelServer`) and wait for hot per step 2 below before touching TokenSaver.MCP.
3. If `TokenSaverMCP` is `RUNNING`, confirm it's actually reachable:

   ```bash
   curl http://localhost:11436/health
   ```

   A non-error response means TokenSaver.MCP is already installed and up — skip "Install
   TokenSaver.MCP" below and go straight to "Values you need" / client config. If it's stopped,
   start it (`sc start TokenSaverMCP`) rather than reinstalling.
4. Only fall through to the full "Installing SimpleModelServer and TokenSaver.MCP" section below
   for whichever of the two is actually missing (not installed at all) per step 1.

| Provider | File |
| --- | --- |
| VS Code Copilot Chat (and Cline / Roo-Code, same config file) | `install-clients/copilot.md` |
| Claude Code | `install-clients/claude-code.md` |
| Cursor | `install-clients/cursor.md` |
| Sixth AI | `install-clients/sixth-ai.md` |
| Continue | `install-clients/continue.md` |

## Installing SimpleModelServer and TokenSaver.MCP

The VS Code extension no longer bundles raw `.exe` binaries — it bundles WiX **MSI**
installers instead, and installs them via `msiexec` when you run its "Install ... as Windows
Service" commands. **SimpleModelServer must be installed, started, and confirmed hot before you
install or configure TokenSaver.MCP** — TokenSaver.MCP's own tools route through SimpleModelServer's
router, so wiring up a client against TokenSaver first just gets you a client that connects but
whose tool calls silently degrade or fail while SimpleModelServer is still loading models. Do these in
order:

### 1. Install SimpleModelServer

`Ctrl+Shift+P` → **"TokenSaver: Install SimpleModelServer as Windows Service"** — runs the bundled
`SimpleModelServer-Setup.msi` via `msiexec /i ... /qn`, installing to
`C:\Program Files\SimpleModelServer\SimpleModelServer.Server.exe` and registering the `SimpleModelServer`
Windows service. Shows a UAC prompt (service installation requires admin rights). If you'd
rather install standalone outside VS Code: `msiexec /i "<path-to>\SimpleModelServer-Setup.msi" /qn
/norestart`.

### 2. Start the service and wait for it to be hot

The service starts (and reports "running" to the SCM) almost immediately, but that is **not**
the same as being ready — `SimpleModelServerWorker` returns control to the SCM right away and does the
actual Ollama/model bring-up in the background, so a service that shows as "Running" can still
be minutes away from being usable. Don't skip the wait:

1. Confirm the router process is up:

   ```bash
   curl http://localhost:11435/health
   ```

   Retry (don't give up) for up to ~2 minutes if this doesn't return a non-error response —
   this is normal immediately after install/start, not a failure.
2. Once `/health` responds, confirm the models are actually loaded (this is the "hot" check —
   `/health` alone only proves the process is alive, not that inference is ready):

   ```bash
   curl http://localhost:11435/api/capacity
   ```

   Only treat SimpleModelServer as hot once this reports the draft/main/embedding models as loaded. If
   it reports models still loading/pulling, keep polling — do not proceed to step 3 yet.
3. Only after `/api/capacity` confirms models are loaded, move on to installing TokenSaver.MCP
   below.

If you need to restart it later: `sc stop SimpleModelServer; sc start SimpleModelServer`, then
repeat the same `/health` → `/api/capacity` wait — a restart is not instantly hot either.

### 3. Install TokenSaver.MCP

Only once SimpleModelServer is confirmed hot: `Ctrl+Shift+P` → **"TokenSaver: Install TokenSaver.MCP
as Windows Service"** — runs the bundled `TokenSaver-Setup.msi` via `msiexec /i ... /qn`,
installing to `C:\Program Files\TokenSaver\TokenSaver.MCP.exe` and registering the
`TokenSaverMCP` Windows service (auto-starts at login). Shows a UAC prompt. Standalone
alternative: `msiexec /i "<path-to>\TokenSaver-Setup.msi" /qn /norestart`.

To uninstall either service, use the matching "Uninstall ..." command, or
`msiexec /x <path-to-msi> /qn /norestart`.

### Removing everything (uninstalling the extension does NOT remove the services)

**Important:** VS Code has no hook that runs extension code when a VSIX is uninstalled from the
Extensions panel — there's no `onWillUninstall` equivalent, and `deactivate()` only fires on
disable/reload, which is indistinguishable from a real uninstall. That means simply removing the
TokenSaver extension leaves the `TokenSaverMCP` and `SimpleModelServer` Windows services fully
installed and running (they'll keep auto-starting at login) — they're managed by Windows'
Service Control Manager independently of the extension that installed them.

If you want a full teardown, run these two commands **before** uninstalling the extension itself:

- `Ctrl+Shift+P` → **"TokenSaver: Uninstall TokenSaver.MCP Windows Service"**
- `Ctrl+Shift+P` → **"TokenSaver: Uninstall SimpleModelServer Windows Service"**

Only after both services report as uninstalled should you remove the extension from the
Extensions panel. (These commands also reset the extension's internal "already asked about
install" flag, so a future reinstall offers the first-run install prompt again instead of
silently staying quiet.)

## Values you need

- **Port**: TokenSaver.MCP listens on `11436` by default (configurable via the
  `tokenSaver.server.port` VS Code setting — check `.vscode/settings.json` or the user's
  global settings if unsure).
- **HTTP endpoint**: `http://localhost:<port>/mcp` (e.g. `http://localhost:11436/mcp`).
- **stdio binary path**: `C:\Program Files\TokenSaver\TokenSaver.MCP.exe` — this is where the
  bundled MSI installs it (see "Installing TokenSaver.MCP" above). If the user is running
  TokenSaver in dev mode (via `dotnet run`), there is no installed exe to point stdio at; use
  the HTTP option instead.

Before editing any config, confirm the server is reachable if you're using an HTTP-based
option:

```bash
curl http://localhost:11436/health
```

A non-error response means it's up. If not, start it from VS Code
(`Ctrl+Shift+P` → "TokenSaver: Start Server") first.

---

## Verifying the client connection worked (do this for whichever provider you configured)

Don't just assume the config was picked up:

1. Open that tool's MCP/tools panel (or equivalent) and confirm `TokenSaver` shows as
   connected.
2. Run one real tool call through it and confirm you get a response, not a connection error.
3. If it fails: for HTTP-based options, re-check the server is running
   (`curl http://localhost:11436/health`) and that the port in the config matches the actual
   configured port; for stdio options, check the exe path is correct, exists, and isn't already
   running elsewhere holding a lock on the port.

## Verify install (deep — test the servers themselves, not just the client connection)

The check above only proves a client *can reach* TokenSaver.MCP. It doesn't prove SimpleModelServer is
actually serving working completions, or that TokenSaver's individual tools (chat, code
indexing, context building, file editing) work end-to-end. For that, use the two dedicated
verification prompts, in order:

1. `SimpleModelServer/SimpleModelServer/VERIFY_INSTALL.md` — tests SimpleModelServer's router directly:
   health/readiness, capacity, model listing, chat (streaming and non-streaming), the
   Ollama-native generate endpoint, and embeddings. Run this first — most TokenSaver.MCP tools
   depend on SimpleModelServer, so a SimpleModelServer problem will otherwise look like a TokenSaver bug.
2. `TokenSaver/TokenSaver.MCP/VERIFY_INSTALL.md` — tests TokenSaver.MCP's own surface: health,
   the `Help` tool, `CleanAndChat` (which exercises the full client→MCP→SimpleModelServer chain),
   endpoint listing, code indexing, context diagnostics/building, and a dry-run file edit.

## Why this file exists instead of a script doing it automatically

Writing MCP client configuration files programmatically is fragile: five different tools here
mean five different formats/locations/root keys, edits can race with the editor rewriting the
same file, and a stale auto-written entry is harder to notice and fix than one you added
yourself with full context. For a one-time setup step like this, it's more reliable to hand an
AI agent (or a person) precise, unambiguous instructions and let them make the edit directly,
than to have a background service guess which format to write and silently maintain it across
every server startup.
