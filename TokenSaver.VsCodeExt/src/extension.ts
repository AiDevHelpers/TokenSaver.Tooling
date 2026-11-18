import * as vscode from "vscode";
import { startIdeBridge } from "./ideBridge.js";
import {
    addAuthUser,
    authHeaders,
    buildUserPickItems,
    isServerAlive,
    isValidAccount,
    listAuthUsers,
    readAuthToken,
    removeAuthUser,
} from "./auth.js";
import { spawn } from "child_process";
import * as path from "path";
import * as fs from "fs";
import { ServiceEventLogPoller } from "./serviceEventLog.js";
import { isPortOpen, waitForPortOpen, findOpenPortInRange } from "./net.js";
import { fetchHealthOk, waitForHealthOk } from "./health.js";
import { isServiceInstalled, runSc, runMsiexec } from "./windowsServices.js";

let channelTokenSaver: vscode.OutputChannel;
let channelSimpleModelServer: vscode.OutputChannel;
let statusBar: vscode.StatusBarItem;
let logsStatusBar: vscode.StatusBarItem;

// TokenSaver.MCP process
let serverProcess: ReturnType<typeof spawn> | null = null;
let serverStartPromise: Promise<void> | null = null;
let stoppingServerProcess: Promise<void> | null = null;
let idleTimer: ReturnType<typeof setTimeout> | null = null;
let actualServerPort: number | null = null;
let serverStopIntentional = false; // true while stopServer() is in progress
let stdoutLineBuffer = "";

// Windows event-log mirrors for the two services (see serviceEventLog.ts).
const tokenSaverEventLog = new ServiceEventLogPoller({
    source: "TokenSaver",
    isActive: () => usingTokenSaverWindowsService,
    channel: () => channelTokenSaver,
    onError: (message) => log(`TokenSaver service log poll failed: ${message}`),
});
const simpleModelServerEventLog = new ServiceEventLogPoller({
    source: "SimpleModelServer",
    isActive: () => usingSimpleModelServerWindowsService,
    channel: () => channelSimpleModelServer,
    onError: (message) => log(`SimpleModelServer service log poll failed: ${message}`),
});

// Windows service mode — true when the TokenSaverMCP SCM service (installed by
// src/Installers/TokenSaver.Setup, running from Program Files\TokenSaver) is managing the
// process. In this mode we do not spawn/kill a child process or run an idle-shutdown timer;
// we use sc.exe to start/stop and adopt whatever is already listening on the configured port.
// Mirrors the equivalent SimpleModelServer service-mode handling below.
const TOKENSAVER_SERVICE_NAME = "TokenSaverMCP";
let usingTokenSaverWindowsService = false;

// Windows service log polling (TokenSaverMCP)

// SimpleModelServer process
let simpleModelServerProcess: ReturnType<typeof spawn> | null = null;
let simpleModelServerStartPromise: Promise<void> | null = null;
let stoppingSimpleModelServerProcess: Promise<void> | null = null;
let simpleModelServerHealthCheckInProgress = false;

// Windows service log polling

// Windows service mode — true when SimpleModelServer SCM service is managing the process.
// In this mode we do not spawn/kill a child process; we use sc.exe to start/stop.
const SIMPLEMODEL_SERVER_SERVICE_NAME = "SimpleModelServer";
let usingSimpleModelServerWindowsService = false;

// ─── .tokensaver/config.json behavior config ─────────────────────────────────
//
// Mirrors TokenSaver.Config's BehaviorConfigService/TokenSaverBehaviorConfig/GitignorePrompt
// (src/Core/TokenSaver.Config) on the extension side. Two files, local wins over global on any
// set property:
//   - {workspaceRoot}/.tokensaver/config.json  (local)
//   - %ProgramData%\TokenSaver\config.json     (global)
// The extension has direct UI access (showInformationMessage), so it asks the user itself
// instead of relaying through the AI the way the MCP-side GitignorePrompt has to.

interface RepoHelperBehaviorConfig {
    addTokenSaverToGitignore?: boolean | null;
    optimizeInstructions?: boolean | null;
}

interface TokenSaverBehaviorConfig {
    repoHelper?: RepoHelperBehaviorConfig | null;
}

function globalBehaviorConfigPath(): string {
    const programData = process.env["ProgramData"] ?? "C:\\ProgramData";
    return path.join(programData, "TokenSaver", "config.json");
}

function localBehaviorConfigPath(workspaceRoot: string): string {
    return path.join(workspaceRoot, ".tokensaver", "config.json");
}

function readBehaviorConfigFile(filePath: string): TokenSaverBehaviorConfig | null {
    try {
        if (!fs.existsSync(filePath)) return null;
        const text = fs.readFileSync(filePath, "utf8");
        if (!text.trim()) return null;
        return JSON.parse(text) as TokenSaverBehaviorConfig;
    } catch (err) {
        log(`Failed to read behavior config ${filePath}: ${(err as Error).message}`);
        return null;
    }
}

function readMergedBehaviorConfig(workspaceRoot: string): RepoHelperBehaviorConfig {
    const local = readBehaviorConfigFile(localBehaviorConfigPath(workspaceRoot));
    const global = readBehaviorConfigFile(globalBehaviorConfigPath());
    return {
        addTokenSaverToGitignore:
            local?.repoHelper?.addTokenSaverToGitignore ?? global?.repoHelper?.addTokenSaverToGitignore ?? null,
    };
}

function writeBehaviorConfigValue(filePath: string, mutate: (cfg: TokenSaverBehaviorConfig) => TokenSaverBehaviorConfig) {
    const current = readBehaviorConfigFile(filePath) ?? {};
    const updated = mutate(current);
    fs.mkdirSync(path.dirname(filePath), { recursive: true });
    fs.writeFileSync(filePath, JSON.stringify(updated, null, 2) + "\n", "utf8");
}

/** Mirrors GitignoreFilter.EntryIsCovered (TokenSaver.Library) — is `.tokensaver` already in .gitignore? */
function gitignoreCoversTokenSaver(workspaceRoot: string): boolean {
    const gitignorePath = path.join(workspaceRoot, ".gitignore");
    if (!fs.existsSync(gitignorePath)) return false;

    const normalised = ".tokensaver";
    let text: string;
    try {
        text = fs.readFileSync(gitignorePath, "utf8");
    } catch {
        return false;
    }

    for (const raw of text.split(/\r?\n/)) {
        const line = raw.trim();
        if (!line || line.startsWith("#") || line.startsWith("!")) continue;
        const stripped = line.replace(/\/+$/, "").replace(/^\/+/, "");
        if (!stripped) continue;
        if (
            normalised.toLowerCase() === stripped.toLowerCase() ||
            normalised.toLowerCase().startsWith(stripped.toLowerCase() + "/")
        ) {
            return true;
        }
    }
    return false;
}

/**
 * On activation, if no workspace/global config has ever answered whether `.tokensaver/` should
 * be added to `.gitignore`, and it's not already covered there, asks the user directly (native
 * VS Code prompt) and persists the answer to `.tokensaver/config.json` (local) or
 * `%ProgramData%\TokenSaver\config.json` (global), matching TokenSaver.Config's local-wins-over-
 * global merge rule. No-ops outside a workspace folder.
 */
async function maybePromptAddTokenSaverToGitignore(): Promise<void> {
    const workspaceRoot = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
    if (!workspaceRoot) return;

    const merged = readMergedBehaviorConfig(workspaceRoot);
    if (merged.addTokenSaverToGitignore !== null && merged.addTokenSaverToGitignore !== undefined) return;
    if (gitignoreCoversTokenSaver(workspaceRoot)) return;

    const choice = await vscode.window.showInformationMessage(
        "TokenSaver writes its cache to `.tokensaver/`, which isn't in this workspace's .gitignore. Add it?",
        "Add (this workspace only)",
        "Add (remember for all workspaces)",
        "Don't add",
    );
    if (choice === undefined) return; // dismissed — ask again next activation

    const add = choice !== "Don't add";
    const scopeGlobal = choice === "Add (remember for all workspaces)";
    const targetPath = scopeGlobal ? globalBehaviorConfigPath() : localBehaviorConfigPath(workspaceRoot);

    try {
        writeBehaviorConfigValue(targetPath, (cfg) => ({
            ...cfg,
            repoHelper: { ...cfg.repoHelper, addTokenSaverToGitignore: add },
        }));
    } catch (err) {
        log(`Failed to persist gitignore preference to ${targetPath}: ${(err as Error).message}`);
    }

    if (add) {
        const gitignorePath = path.join(workspaceRoot, ".gitignore");
        try {
            const existing = fs.existsSync(gitignorePath) ? fs.readFileSync(gitignorePath, "utf8") : "";
            const needsNewline = existing.length > 0 && !existing.endsWith("\n");
            fs.writeFileSync(gitignorePath, existing + (needsNewline ? "\n" : "") + ".tokensaver/\n", "utf8");
            log("Added `.tokensaver/` to .gitignore.");
        } catch (err) {
            log(`Failed to update .gitignore: ${(err as Error).message}`);
        }
    }
}

/**
 * One-time migration for the "localHelper.*" -> "tokenSaver.*" settings section rename.
 * Copies any explicitly-set value under the old section into the new one, provided the new
 * key hasn't already been explicitly set (so a user's later "tokenSaver.*" edit always wins).
 * Does not delete the old values — VS Code settings.json is user-owned, and leaving the old
 * keys in place is harmless now that nothing reads them.
 */
function migrateLegacySettings() {
    const oldCfg = vscode.workspace.getConfiguration("localHelper");
    const newCfg = vscode.workspace.getConfiguration("tokenSaver");
    const keys = [
        "autostart", "server.port", "server.startTimeout", "server.idleTimeout",
        "dotnet.command", "dotnet.projectPath", "notifications.enabled", "notifications.level",
        "verboseLogging", "simpleModelServer.exePath", "mcp.exePath", "simpleModelServer.healthUrl",
        "simpleModelServer.startTimeout", "autoCodeHelper.enabled", "autoInstallPrompt",
    ];
    for (const key of keys) {
        const oldInspect = oldCfg.inspect(key);
        const oldValue = oldInspect?.globalValue ?? oldInspect?.workspaceValue;
        if (oldValue === undefined) {
            continue;
        }

        const newInspect = newCfg.inspect(key);
        if (newInspect?.globalValue !== undefined || newInspect?.workspaceValue !== undefined) {
            continue;
        }

        const target = oldInspect?.workspaceValue !== undefined
            ? vscode.ConfigurationTarget.Workspace
            : vscode.ConfigurationTarget.Global;
        void newCfg.update(key, oldValue, target);
    }
}

export async function activate(context: vscode.ExtensionContext) {
    migrateLegacySettings();
    startIdeBridge(context);

    channelTokenSaver = vscode.window.createOutputChannel("TokenSaver");
    channelSimpleModelServer = vscode.window.createOutputChannel("SimpleModelServer");

    statusBar = vscode.window.createStatusBarItem(
        vscode.StatusBarAlignment.Left,
        100,
    );
    statusBar.command = "tokenSaver.showStatus";
    statusBar.tooltip = "Show TokenSaver status";
    statusBar.show();

    logsStatusBar = vscode.window.createStatusBarItem(
        vscode.StatusBarAlignment.Right,
        100,
    );
    logsStatusBar.command = "tokenSaver.showLogs";
    logsStatusBar.tooltip = "Open TokenSaver logs";
    logsStatusBar.text = "$(output) TokenSaver Logs";
    logsStatusBar.show();

    context.subscriptions.push(
        statusBar,
        logsStatusBar,
        channelTokenSaver,
        channelSimpleModelServer,
        vscode.commands.registerCommand("tokenSaver.startServer", async () => {
            await startSimpleModelServer(context);
            await startServer();
        }),
        vscode.commands.registerCommand("tokenSaver.stopServer", async () => {
            await stopServer();
            await stopSimpleModelServer();
            // (context available in closure for potential future use)
        }),
        vscode.commands.registerCommand(
            "tokenSaver.restartServer",
            async () => {
                await restartServer(context);
            },
        ),
        vscode.commands.registerCommand("tokenSaver.showLogs", () => {
            channelTokenSaver.show(true);
        }),
        vscode.commands.registerCommand("tokenSaver.showStatus", async () => {
            await startSimpleModelServer(context);
            await startServer();
            showStatusInLog();
        }),
        vscode.commands.registerCommand("tokenSaver.installService", async () => {
            await installSimpleModelServerService(context);
        }),
        vscode.commands.registerCommand("tokenSaver.uninstallService", async () => {
            await uninstallSimpleModelServerService(context);
        }),
        vscode.commands.registerCommand("tokenSaver.installMcpService", async () => {
            await installTokenSaverService(context);
        }),
        vscode.commands.registerCommand("tokenSaver.uninstallMcpService", async () => {
            await uninstallTokenSaverService(context);
        }),
        vscode.commands.registerCommand("tokenSaver.openInstallPrompt", async () => {
            await openInstallPrompt(context);
        }),
        vscode.commands.registerCommand("tokenSaver.runFullSetup", async () => {
            await runFullSetup(context);
        }),
        vscode.commands.registerCommand("tokenSaver.manageUsers", async () => {
            await manageUsers();
        }),
        vscode.commands.registerCommand("tokenSaver.copyAuthToken", async () => {
            const token = await currentAuthToken();
            if (!token) {
                void vscode.window.showWarningMessage(
                    "TokenSaver: no readable auth token. Install the service, or ask an administrator to add you to the TokenSaver group.",
                );
                return;
            }

            await vscode.env.clipboard.writeText(token);
            void vscode.window.showInformationMessage(
                "TokenSaver: auth token copied. Use it as the Bearer token in your AI client's MCP config.",
            );
        }),
        vscode.commands.registerCommand(
            "tokenSaver.toggleAutoCodeHelper",
            async () => {
                const cfg = vscode.workspace.getConfiguration("tokenSaver");
                const current = cfg.get<boolean>(
                    "autoCodeHelper.enabled",
                    false,
                );
                await cfg.update(
                    "autoCodeHelper.enabled",
                    !current,
                    vscode.ConfigurationTarget.Workspace,
                );
                const state = !current ? "enabled" : "disabled";
                log(`CodeHelper auto-workflow toggled: ${state}`);
                vscode.window.showInformationMessage(
                    `CodeHelper auto-workflow ${state}. Restart TokenSaver to apply.`,
                );
            },
        ),
    );

    // Legacy command-id aliases from before the "localHelper" -> "tokenSaver" rename. Not
    // contributed in package.json (so they don't clutter the Command Palette), but registered
    // here so a keybinding still bound to an old "localHelper.*" id keeps working.
    const legacyCommandIds = [
        "startServer", "stopServer", "restartServer", "showStatus", "showLogs",
        "toggleAutoCodeHelper", "installService", "uninstallService", "installMcpService",
        "uninstallMcpService", "openInstallPrompt", "runFullSetup",
    ];
    context.subscriptions.push(
        ...legacyCommandIds.map((id) =>
            vscode.commands.registerCommand(`localHelper.${id}`, (...args: unknown[]) =>
                vscode.commands.executeCommand(`tokenSaver.${id}`, ...args),
            ),
        ),
        vscode.workspace.onDidChangeConfiguration(async (e) => {
            if (e.affectsConfiguration("tokenSaver")) {
                await restartServer(context);
            }
        }),
    );

    // NOTE: TokenSaver no longer auto-writes its entry into any AI provider's config file
    // (Continue, Cursor, Claude Code, VS Code Copilot's mcp.json, Sixth, etc.) — that proved
    // unreliable across the different config formats/locations. Use the "TokenSaver: Open
    // Install Prompt" command instead. registerMcpProvider() below is unaffected: it uses
    // VS Code's own registerMcpServerDefinitionProvider API, not a config-file write.
    registerMcpProvider(context);
    void ensureInstalled(context);
    void maybePromptForServiceInstall(context);
    void maybePromptAddTokenSaverToGitignore();

    // Start listening to SimpleModelServer named pipe (independent of autostart)
    simpleModelServerEventLog.start();

    const autostart = vscode.workspace
        .getConfiguration("tokenSaver")
        .get<boolean>("autostart", true);
    if (autostart) {
        await startSimpleModelServer(context);
        await startServer();
        await checkMcpHealth();
        // Full /health self-test (fault-tracking + self-test checks), not just /mcp
        // reachability — see checkMcpFullHealth's doc comment for why this doesn't restart
        // on its own. Fire-and-forget so a slow/unreachable check doesn't hold up activation.
        void checkMcpFullHealth();
    } else {
        updateStatus("stopped");
    }
}

// ─── First-run service install prompt ────────────────────────────────────────

const SERVICE_INSTALL_PROMPT_ASKED_KEY = "tokenSaver.serviceInstallPromptAsked";

/**
 * On first activation (per machine/profile — tracked via globalState, not per-session), if
 * neither the TokenSaverMCP nor SimpleModelServer Windows services are installed and the
 * bundled MSIs are present (see build-vsix.ps1 / bin/*.Setup), asks the user once whether to
 * install them via the same msiexec path the "Install TokenSaver MCP" / "Install SimpleModelServer
 * as Windows Service" commands use (installTokenSaverService / installSimpleModelServerService).
 * Without this, a user has to know those commands exist and run them manually after installing
 * the VSIX — this closes that gap while still asking permission before an admin-elevating
 * msiexec install runs. Respects tokenSaver.autoInstallPrompt (default true) and never asks
 * again once answered, regardless of the answer, so declining doesn't nag on every reload.
 */
async function maybePromptForServiceInstall(context: vscode.ExtensionContext) {
    const cfg = vscode.workspace.getConfiguration("tokenSaver");
    if (!cfg.get<boolean>("autoInstallPrompt", true)) return;

    if (context.globalState.get<boolean>(SERVICE_INSTALL_PROMPT_ASKED_KEY, false)) {
        return;
    }

    const mcpMsi = tokenSaverMsiPath(context);
    const simpleModelServerMsi = simpleModelServerMsiPath(context);
    const mcpMsiAvailable = fs.existsSync(mcpMsi);
    const simpleModelServerMsiAvailable = fs.existsSync(simpleModelServerMsi);
    if (!mcpMsiAvailable && !simpleModelServerMsiAvailable) return; // no bundled installers — nothing to offer

    const [mcpInstalled, simpleModelServerInstalled] = await Promise.all([
        isServiceInstalled(TOKENSAVER_SERVICE_NAME),
        isServiceInstalled(SIMPLEMODEL_SERVER_SERVICE_NAME),
    ]);
    const needsMcpInstall = mcpMsiAvailable && !mcpInstalled;
    const needsSimpleModelServerInstall = simpleModelServerMsiAvailable && !simpleModelServerInstalled;
    if (!needsMcpInstall && !needsSimpleModelServerInstall) {
        // Already installed (or no bundled MSI for what's missing) — remember we don't need to ask.
        await context.globalState.update(SERVICE_INSTALL_PROMPT_ASKED_KEY, true);
        return;
    }

    // Mark as asked before showing the prompt so a reload/crash mid-dialog can't cause a loop.
    await context.globalState.update(SERVICE_INSTALL_PROMPT_ASKED_KEY, true);

    const choice = await vscode.window.showInformationMessage(
        "TokenSaver can install itself and SimpleModelServer as background Windows services (auto-starts at login, no VS Code window required). Install now? A UAC prompt may appear.",
        "Install",
        "Not now",
    );
    if (choice !== "Install") {
        log("Service auto-install declined by user.");
        return;
    }

    await runFullSetup(context);
}

/**
 * One-shot end-to-end setup, run automatically after the user confirms the first-run install
 * prompt (see maybePromptForServiceInstall above) and also available manually as the
 * "TokenSaver: Run Full Setup" command for re-running after a partial/failed attempt.
 *
 * Does, in order, only stopping early if a step fails:
 *   1. Install SimpleModelServer service (skipped if already installed).
 *   2. Wait for SimpleModelServer to report healthy on its /health endpoint — this can take
 *      several minutes on first run (model download/warmup), so it uses
 *      tokenSaver.simpleModelServer.startTimeout (default 600s), not the short server-restart-style
 *      timeout used elsewhere.
 *   3. Install TokenSaverMCP service (skipped if already installed).
 *   4. Wait for TokenSaver.MCP to respond on /mcp (tokenSaver.server.startTimeout, default 30s
 *      — this is a fast local process with no model warmup, unlike step 2).
 *   5. Open INSTALL_PROMPT.md so the user/AI can wire up the MCP client.
 *
 * Each step re-checks actual system state (isServiceInstalled/health polling) rather than
 * trusting that a previous call's promise resolving means the step truly succeeded — the same
 * defensive pattern installSimpleModelServerService/installTokenSaverService already use, since a
 * silently-swallowed UI interaction (e.g. a missed click, or msiexec failing after the promise
 * already resolved) must not be allowed to cascade into later steps running against a system
 * that isn't actually ready.
 */
async function runFullSetup(context: vscode.ExtensionContext): Promise<void> {
    log("=== TokenSaver full setup starting ===");

    // Step 1: SimpleModelServer service install.
    if (!(await isServiceInstalled(SIMPLEMODEL_SERVER_SERVICE_NAME))) {
        await installSimpleModelServerService(context);
        if (!(await isServiceInstalled(SIMPLEMODEL_SERVER_SERVICE_NAME))) {
            log("Full setup: SimpleModelServer service is still not installed after install step — aborting.");
            vscode.window.showErrorMessage(
                "TokenSaver setup failed: SimpleModelServer did not install. See the SimpleModelServer output channel for details.",
            );
            return;
        }
    } else {
        log("Full setup: SimpleModelServer service already installed — skipping install.");
    }

    // Step 2: wait for SimpleModelServer to actually be healthy, not just "installed".
    const cfg = vscode.workspace.getConfiguration("tokenSaver");
    const healthUrl = cfg.get<string>("simpleModelServer.healthUrl", "http://localhost:11435/health");
    const simpleModelServerTimeoutSec = cfg.get<number>("simpleModelServer.startTimeout", 600);
    log(`Full setup: waiting up to ${simpleModelServerTimeoutSec}s for SimpleModelServer to report healthy...`);
    vscode.window.showInformationMessage(
        "TokenSaver setup: waiting for SimpleModelServer to finish starting (first run can take several minutes while models download/warm up)...",
    );

    const firstWaitSec = Math.max(30, Math.floor(simpleModelServerTimeoutSec / 3));
    let simpleModelServerHealthy = await waitForHealthOk(healthUrl, firstWaitSec);
    if (!simpleModelServerHealthy) {
        log(
            `Full setup: SimpleModelServer still not healthy after ${firstWaitSec}s — restarting the service once ` +
            "and continuing to wait...",
        );
        await runSc(["stop", SIMPLEMODEL_SERVER_SERVICE_NAME]);
        await new Promise((r) => setTimeout(r, 2000));
        await runSc(["start", SIMPLEMODEL_SERVER_SERVICE_NAME]);
        simpleModelServerHealthy = await waitForHealthOk(healthUrl, simpleModelServerTimeoutSec - firstWaitSec);
    }
    if (!simpleModelServerHealthy) {
        log(`Full setup: SimpleModelServer did not become healthy within ${simpleModelServerTimeoutSec}s — aborting.`);
        vscode.window.showErrorMessage(
            `TokenSaver setup failed: SimpleModelServer did not become healthy within ${simpleModelServerTimeoutSec}s. Check the SimpleModelServer output channel.`,
        );
        return;
    }
    log("Full setup: SimpleModelServer is healthy.");

    // Step 3: TokenSaver.MCP service install.
    if (!(await isServiceInstalled(TOKENSAVER_SERVICE_NAME))) {
        await installTokenSaverService(context);
        if (!(await isServiceInstalled(TOKENSAVER_SERVICE_NAME))) {
            log("Full setup: TokenSaverMCP service is still not installed after install step — aborting.");
            vscode.window.showErrorMessage(
                "TokenSaver setup failed: TokenSaver.MCP did not install. See the TokenSaver output channel for details.",
            );
            return;
        }
    } else {
        log("Full setup: TokenSaverMCP service already installed — skipping install.");
    }

    // Step 4: wait for TokenSaver.MCP to actually respond, not just "installed".
    const port = cfg.get<number>("server.port", 11436);
    const mcpTimeoutSec = cfg.get<number>("server.startTimeout", 30);
    log(`Full setup: waiting up to ${mcpTimeoutSec}s for TokenSaver.MCP to become reachable...`);
    const mcpHealthy = await waitForMcpReachable(port, mcpTimeoutSec);
    if (!mcpHealthy) {
        log(`Full setup: TokenSaver.MCP did not become reachable within ${mcpTimeoutSec}s — aborting.`);
        vscode.window.showErrorMessage(
            `TokenSaver setup failed: TokenSaver.MCP did not respond on port ${port} within ${mcpTimeoutSec}s. Check the TokenSaver output channel.`,
        );
        return;
    }
    log("Full setup: TokenSaver.MCP is reachable.");

    // Now that it's reachable, also run the full /health self-test (fault-tracking + self-test
    // checks — see docs/tokensaver-mcp-health-contract.md) so a fresh install's config/wiring
    // problems surface here rather than silently waiting to be discovered by the first real
    // MCP tool call. Doesn't block/abort setup on a Degraded result — see checkMcpFullHealth's
    // doc comment — but does log and notify so the user sees it immediately.
    await checkMcpFullHealth();

    // Step 5: open the AI install instructions now that both services are confirmed healthy.
    await openInstallPrompt(context);
    log("=== TokenSaver full setup complete ===");
    vscode.window.showInformationMessage(
        "TokenSaver setup complete: SimpleModelServer and TokenSaver.MCP are both installed and running.",
    );
}

/** Polls a /health-style endpoint until it reports ok/degraded or the timeout elapses. */

/** Polls the TokenSaver /mcp endpoint until it responds or the timeout elapses. */
async function waitForMcpReachable(port: number, timeoutSec: number): Promise<boolean> {
    const deadline = Date.now() + timeoutSec * 1000;
    while (Date.now() < deadline) {
        if (await verifyMcpServer(port)) return true;
        await new Promise((r) => setTimeout(r, 1000));
    }
    return false;
}

// NOTE: TokenSaver used to auto-write .vscode/mcp.json (ensureMcpJson) so Copilot Chat
// would pick up the server without the user doing anything. Removed along with the other
// provider auto-install paths — see the "TokenSaver: Open Install Prompt" command /
// INSTALL_PROMPT.md for the manual replacement, which covers VS Code Copilot too.

/** Installs the bundled VSIX into VS Code if it exists and hasn't been installed yet.
 *  Runs silently in the background — a failed install is logged but not fatal. */
async function ensureInstalled(context: vscode.ExtensionContext) {
    const vsixPath = path.join(context.extensionPath, "tokensaver.vsix");
    if (!fs.existsSync(vsixPath)) return;

    // Check if already installed at this version
    const pkg = JSON.parse(
        fs.readFileSync(
            path.join(context.extensionPath, "package.json"),
            "utf8",
        ),
    ) as { version?: string };
    const installedExt = vscode.extensions.getExtension(
        "local.tokensaver-vscode",
    );
    if (installedExt?.packageJSON?.version === pkg.version) return;

    log(`Installing VSIX ${vsixPath}...`);
    try {
        const { execFile } = await import("child_process");
        await new Promise<void>((resolve, reject) => {
            execFile(
                "code",
                ["--install-extension", vsixPath, "--force"],
                (err) => {
                    if (err) reject(err);
                    else resolve();
                },
            );
        });
        log(
            "VSIX installed — restart VS Code to activate the updated extension.",
        );
        notify("info", "TokenSaver updated — please restart VS Code.");
    } catch (err) {
        log(`VSIX install failed: ${(err as Error).message}`);
    }
}

/** The endpoint token (tokenSaver.auth.tokenPath, else the default locations), or undefined when unreadable. */
async function currentAuthToken(): Promise<string | undefined> {
    const cfg = vscode.workspace.getConfiguration("tokenSaver");
    return readAuthToken(cfg.get<string>("auth.tokenPath", ""));
}

/** Quick pick over the TokenSaver user group: select a member to remove it, a local account to add it. */
async function manageUsers(): Promise<void> {
    const cfg = vscode.workspace.getConfiguration("tokenSaver");
    const port = actualServerPort ?? cfg.get<number>("server.port", 11436);
    const token = await currentAuthToken();
    if (!token) {
        void vscode.window.showWarningMessage(
            "TokenSaver: no readable auth token, so users cannot be managed from this account.",
        );
        return;
    }

    try {
        const info = await listAuthUsers(port, token);
        const items = buildUserPickItems(info);
        const picked = await vscode.window.showQuickPick(
            [
                ...items,
                { label: "$(add) Enter another account…", description: "DOMAIN\\name", account: "", action: "add" as const },
            ],
            { title: `TokenSaver users (group: ${info.group})`, placeHolder: "Select a member to remove or an account to add" },
        );
        if (!picked) {
            return;
        }

        let account = picked.account;
        if (account === "") {
            const typed = await vscode.window.showInputBox({
                prompt: "Account to add (DOMAIN\\name or name)",
                validateInput: (v) => (isValidAccount(v) ? undefined : "Not a valid account name"),
            });
            if (!typed) {
                return;
            }

            account = typed;
        }

        if (picked.action === "remove") {
            await removeAuthUser(port, token, account);
            void vscode.window.showInformationMessage(`TokenSaver: removed ${account}.`);
        } else {
            await addAuthUser(port, token, account);
            void vscode.window.showInformationMessage(`TokenSaver: added ${account}.`);
        }
    } catch (err) {
        void vscode.window.showErrorMessage(`TokenSaver: ${(err as Error).message}`);
    }
}

/** Hits the MCP /mcp endpoint after startup. If unreachable, restarts the MCP server. */
async function checkMcpHealth(): Promise<boolean> {
    const cfg = vscode.workspace.getConfiguration("tokenSaver");
    const port = actualServerPort ?? cfg.get<number>("server.port", 11436);
    // Use 127.0.0.1 instead of localhost to avoid IPv6 resolution on Windows.
    const url = `http://127.0.0.1:${port}/mcp`;

    try {
        const res = await fetch(url, {
            headers: authHeaders(await currentAuthToken()),
            signal: AbortSignal.timeout(5000),
        });
        // 401 means the server is up but did not accept (or we could not read) the token: not a reason to restart.
        if (isServerAlive(res.status)) {
            log("MCP server health: ok");
            return true;
        }
        log(`MCP server health check returned ${res.status} — restarting...`);
    } catch {
        log("MCP server unreachable after start — restarting...");
    }

    await stopServer();
    try {
        await startServer();
        return true;
    } catch {
        return false;
    }
}

/** Shape of TokenSaver.MCP's /health JSON response — see docs/tokensaver-mcp-health-contract.md. */
interface McpHealthCheckEntry {
    name?: string;
    status?: string;
    description?: string;
    error?: string | null;
}

interface McpHealthReport {
    status?: string;
    checks?: McpHealthCheckEntry[];
}

/**
 * Hits TokenSaver.MCP's own /health endpoint (not just /mcp reachability, which checkMcpHealth
 * above already covers) so the fault-tracking and self-test checks built into that endpoint —
 * see docs/tokensaver-mcp-health-contract.md — actually get consulted on startup instead of
 * only ever running when something else happens to poll /health manually. Unlike checkMcpHealth,
 * this never restarts the server: /health can legitimately report Degraded (models not
 * warmed up yet, self-test wiring hiccup, etc.) without the process itself being broken, so this
 * only logs and notifies — restarting on every Degraded would fight the same "restart-flapping"
 * problem checkAndHealSimpleModelServer's health-based restart deliberately avoids for the SimpleModelServer
 * side by tolerating 503/degraded there too. Returns true if the endpoint was reachable at all
 * (regardless of status), false only if it couldn't be reached/parsed.
 */
async function checkMcpFullHealth(): Promise<boolean> {
    const cfg = vscode.workspace.getConfiguration("tokenSaver");
    const port = actualServerPort ?? cfg.get<number>("server.port", 11436);
    const url = `http://127.0.0.1:${port}/health`;

    let report: McpHealthReport;
    try {
        const res = await fetch(url, { signal: AbortSignal.timeout(10000) });
        report = (await res.json()) as McpHealthReport;
    } catch (err) {
        log(`TokenSaver.MCP full self-test (/health) unreachable: ${(err as Error).message}`);
        return false;
    }

    const status = report.status ?? "Unknown";
    log(`TokenSaver.MCP full self-test (/health): ${status}`);

    const failingChecks = (report.checks ?? []).filter(
        (c) => c.status && c.status !== "Healthy",
    );
    if (failingChecks.length === 0) {
        return true;
    }

    for (const check of failingChecks) {
        const detail = check.error ?? check.description ?? "(no detail)";
        log(`  ${check.name ?? "(unnamed check)"}: ${check.status} — ${detail}`);
    }

    if (status !== "Healthy") {
        notify(
            "error",
            `TokenSaver.MCP self-test reported ${status}: ${failingChecks
                .map((c) => c.name ?? "check")
                .join(", ")}. See TokenSaver output for details.`,
        );
    }

    return true;
}

export function deactivate() {
    // Synchronous kills for the process.exit path — async stop won't run in time
    serverProcess?.kill();
    simpleModelServerProcess?.kill();
}

// Ensure child processes are killed even if the extension host is terminated
// without calling deactivate (e.g. VS Code reload, crash, SIGTERM)
process.on("exit", () => {
    try {
        serverProcess?.kill();
    } catch {
        // Already exited or never started; nothing to clean up.
    }
    try {
        simpleModelServerProcess?.kill();
    } catch {
        // Already exited or never started; nothing to clean up.
    }
});
process.on("SIGTERM", () => {
    try {
        serverProcess?.kill();
    } catch {
        // Already exited or never started; nothing to clean up.
    }
    try {
        simpleModelServerProcess?.kill();
    } catch {
        // Already exited or never started; nothing to clean up.
    }
    process.exit(0);
});
process.on("SIGINT", () => {
    try {
        serverProcess?.kill();
    } catch {
        // Already exited or never started; nothing to clean up.
    }
    try {
        simpleModelServerProcess?.kill();
    } catch {
        // Already exited or never started; nothing to clean up.
    }
    process.exit(0);
});

// ─── SimpleModelServer lifecycle ─────────────────────────────────────────────

/**
 * Path to the WiX-installed SimpleModelServer.Server.exe (see ../SimpleModelServer/build/wix/SimpleModelServer.Setup —
 * INSTALLFOLDER under ProgramFiles64Folder\SimpleModelServer). This is the exe the SimpleModelServer
 * Windows service runs; the extension no longer bundles a raw copy of this exe, so this is the
 * only path used once the MSI has been installed. Mirrors tokenSaverInstalledExePath() below.
 */
function simpleModelServerExePath(): string {
    const cfg = vscode.workspace.getConfiguration("tokenSaver");
    const override = cfg.get<string>("simpleModelServer.exePath", "");
    if (override) return override;
    const programFiles = process.env["ProgramFiles"] ?? "C:\\Program Files";
    return path.join(programFiles, "SimpleModelServer", "SimpleModelServer.Server.exe");
}

/**
 * Path to the bundled WiX MSI (see ../SimpleModelServer/build/wix/SimpleModelServer.Setup), copied into
 * <extensionDir>/bin/SimpleModelServer.Setup/SimpleModelServer-Setup.msi by scripts/build-vsix.ps1.
 * Used by installSimpleModelServerService()/uninstallSimpleModelServerService() so those commands invoke the
 * same installer a user would run standalone, rather than duplicating its sc.exe logic — same
 * pattern as tokenSaverMsiPath() below.
 */
function simpleModelServerMsiPath(context: vscode.ExtensionContext): string {
    return path.join(
        context.extensionPath,
        "bin",
        "SimpleModelServer.Setup",
        "SimpleModelServer-Setup.msi",
    );
}

async function startSimpleModelServer(context: vscode.ExtensionContext) {
    // ── Service mode ────────────────────────────────────────────────────────
    const cfg0 = vscode.workspace.getConfiguration("tokenSaver");
    const healthUrl0 = cfg0.get<string>("simpleModelServer.healthUrl", "http://localhost:11435/health");
    const port0 = parseInt(new URL(healthUrl0).port, 10);

    if (await isServiceInstalled(SIMPLEMODEL_SERVER_SERVICE_NAME)) {
        usingSimpleModelServerWindowsService = true;
        log("SimpleModelServer Windows service detected.");
        if (!(await isPortOpen("127.0.0.1", port0))) {
            log("Starting SimpleModelServer service...");
            await runSc(["start", SIMPLEMODEL_SERVER_SERVICE_NAME]);
        }
        const timeoutSec0 = cfg0.get<number>("simpleModelServer.startTimeout", 120);
        try { await waitForPortOpen("127.0.0.1", port0, timeoutSec0); }
        catch { log("SimpleModelServer service did not become reachable in time."); }
        await checkAndHealSimpleModelServer(healthUrl0, context);
        simpleModelServerEventLog.start();
        return;
    }
    usingSimpleModelServerWindowsService = false;

    // ── Process mode (legacy) ───────────────────────────────────────────────
    if (simpleModelServerProcess) return;
    if (simpleModelServerStartPromise) {
        await simpleModelServerStartPromise;
        return;
    }
    if (stoppingSimpleModelServerProcess) await stoppingSimpleModelServerProcess;
    if (simpleModelServerProcess) return;

    const cfg = vscode.workspace.getConfiguration("tokenSaver");
    const healthUrl = cfg.get<string>(
        "simpleModelServer.healthUrl",
        "http://localhost:11435/health",
    );
    const timeoutSec = cfg.get<number>("simpleModelServer.startTimeout", 120);
    const exe = simpleModelServerExePath();

    // If something is already listening at the health URL, adopt it
    const port = new URL(healthUrl).port;
    if (await isPortOpen("127.0.0.1", parseInt(port, 10))) {
        log(
            `SimpleModelServer already running on port ${port} — adopted.`,
        );
        await checkAndHealSimpleModelServer(healthUrl, context);
        return;
    }

    log(`Launching SimpleModelServer: ${exe}`);

    let proc: ReturnType<typeof spawn>;
    try {
        proc = spawn(exe, ["--service"], {
            stdio: ["ignore", "pipe", "pipe"],
            windowsHide: true,
        });
    } catch (err) {
        log(
            `Failed to spawn SimpleModelServer: ${(err as Error).message}`,
        );
        return;
    }

    if (!proc.pid) {
        log("SimpleModelServer failed to obtain a PID.");
        return;
    }

    simpleModelServerProcess = proc;

    proc.stdout?.on("data", (chunk: Buffer) => {
        for (const line of chunk.toString().split(/\r?\n/).filter(Boolean))
            channelSimpleModelServer.appendLine(line);
    });
    proc.stderr?.on("data", (chunk: Buffer) =>
        channelSimpleModelServer.append(chunk.toString()),
    );
    proc.on("exit", (code, signal) => {
        simpleModelServerProcess = null;
        if (code !== 0 && signal == null)
            log(
                `SimpleModelServer exited with code=${code} signal=${signal}`,
            );
    });
    proc.on("error", (err) => {
        simpleModelServerProcess = null;
        log(`SimpleModelServer error: ${err.message}`);
    });

    simpleModelServerStartPromise = (async () => {
        try {
            await waitForPortOpen("127.0.0.1", parseInt(port, 10), timeoutSec);
            log("SimpleModelServer is reachable.");
            await checkAndHealSimpleModelServer(healthUrl, context);
        } catch (err) {
            log(
                `SimpleModelServer startup timeout: ${(err as Error).message}`,
            );
            proc.kill();
            simpleModelServerProcess = null;
        } finally {
            simpleModelServerStartPromise = null;
        }
    })();

    await simpleModelServerStartPromise;
}

async function stopSimpleModelServer() {
    if (usingSimpleModelServerWindowsService) {
        log("Stopping SimpleModelServer service...");
        await runSc(["stop", SIMPLEMODEL_SERVER_SERVICE_NAME]);
        simpleModelServerEventLog.stop();
        return;
    }
    if (!simpleModelServerProcess) {
        if (stoppingSimpleModelServerProcess) await stoppingSimpleModelServerProcess;
        return;
    }
    if (stoppingSimpleModelServerProcess) {
        await stoppingSimpleModelServerProcess;
        return;
    }

    const proc = simpleModelServerProcess;
    simpleModelServerProcess = null;
    let resolved = false;

    stoppingSimpleModelServerProcess = new Promise<void>((resolve) => {
        const cleanup = () => {
            if (!resolved) {
                resolved = true;
                stoppingSimpleModelServerProcess = null;
                resolve();
            }
        };
        proc.once("exit", cleanup);
        proc.once("error", cleanup);
        proc.once("close", cleanup);
        proc.kill();
        setTimeout(() => {
            if (!proc.killed) proc.kill("SIGKILL");
            cleanup();
        }, 3000);
    });

    await stoppingSimpleModelServerProcess;
}

/**
 * Hits /health, and if the response is not 200 "ok", kills and restarts
 * SimpleModelServer. Called on initial start and after any slow MCP response.
 */
export async function checkAndHealSimpleModelServer(
    healthUrl: string,
    context: vscode.ExtensionContext,
): Promise<void> {
    if (simpleModelServerHealthCheckInProgress) return;
    simpleModelServerHealthCheckInProgress = true;

    try {
        const healthy = await fetchHealthOk(healthUrl);
        if (healthy) {
            log("SimpleModelServer health: ok");
            return;
        }

        log("SimpleModelServer health check failed — restarting...");
        await stopSimpleModelServer();
        await startSimpleModelServer(context);
    } catch (err) {
        log(`Health check error: ${(err as Error).message}`);
    } finally {
        simpleModelServerHealthCheckInProgress = false;
    }
}


function timestampPrefix(): string {
    // Matches the yyyy-MM-dd HH:mm:ss convention already used by SimpleModelServer/TokenSaver.MCP's
    // own console/EventLog logging (see Program.cs in both projects), so lifecycle lines in the
    // output channel line up with the service's own log lines instead of showing no timestamp at
    // all (as plain appendLine did before) or a raw epoch/wire-format value.
    const now = new Date();
    const pad = (n: number, len = 2) => String(n).padStart(len, "0");
    return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())} ${pad(now.getHours())}:${pad(now.getMinutes())}:${pad(now.getSeconds())}`;
}








// ─── log routing ─────────────────────────────────────────────────────────────

function routeStdoutLine(line: string) {
    if (!line.startsWith("{")) {
        channelTokenSaver.appendLine(line);
        return;
    }
    try {
        const entry = JSON.parse(line) as {
            ts?: string;
            lvl?: string;
            ch?: string;
            src?: string;
            msg?: string;
            ex?: string;
        };
        const channel = channelTokenSaver;
        const ts = entry.ts ? new Date(entry.ts).toLocaleTimeString() : "";
        const lvl = entry.lvl ?? "INF";
        const src = entry.src ? ` ${entry.src}:` : "";
        const msg = entry.msg ?? line;
        channel.appendLine(`${ts} ${lvl}${src} ${msg}`);
        if (entry.ex) channel.appendLine(entry.ex);
    } catch {
        channelTokenSaver.appendLine(line);
    }
}

function handleStdoutChunk(chunk: Buffer) {
    stdoutLineBuffer += chunk.toString();
    const lines = stdoutLineBuffer.split(/\r?\n/);
    stdoutLineBuffer = lines.pop() ?? "";
    for (const line of lines) {
        if (!line) continue;
        const portMatch = line.match(/Now listening on:.*?:(\d+)/);
        if (portMatch) {
            actualServerPort = parseInt(portMatch[1], 10);
            log(`Server reported port: ${actualServerPort}`);
        }
        routeStdoutLine(line);
    }
}

// ─── MCP server lifecycle ────────────────────────────────────────────────────

/**
 * Path to the WiX-installed TokenSaver.MCP.exe (see src/Installers/TokenSaver.Setup —
 * INSTALLFOLDER under ProgramFiles64Folder\TokenSaver). This is the exe the TokenSaverMCP
 * Windows service itself runs; used here as the last-resort fallback exe path when no
 * bundled copy exists.
 */
function tokenSaverInstalledExePath(): string {
    const cfg = vscode.workspace.getConfiguration("tokenSaver");
    const override = cfg.get<string>("mcp.exePath", "");
    if (override) return override;
    const programFiles = process.env["ProgramFiles"] ?? "C:\\Program Files";
    return path.join(programFiles, "TokenSaver", "TokenSaver.MCP.exe");
}

/**
 * Path to the bundled WiX MSI (see src/Installers/TokenSaver.Setup), copied into
 * <extensionDir>/bin/TokenSaver.Setup/TokenSaver-Setup.msi by scripts/build-vsix.ps1.
 * Used by installTokenSaverService()/uninstallTokenSaverService() so those commands invoke
 * the same installer a user would run standalone, rather than duplicating its sc.exe logic.
 */
function tokenSaverMsiPath(context: vscode.ExtensionContext): string {
    return path.join(
        context.extensionPath,
        "bin",
        "TokenSaver.Setup",
        "TokenSaver-Setup.msi",
    );
}

function mcpExePath(): string {
    const cfg = vscode.workspace.getConfiguration("tokenSaver");
    const override = cfg.get<string>("dotnet.projectPath", "");
    if (override) {
        // Legacy: treat as dotnet project path — caller uses dotnet run
        return override;
    }
    const explicitExe = cfg.get<string>("mcp.exePath", "");
    if (explicitExe) return explicitExe;

    // The extension no longer bundles a raw copy of TokenSaver.MCP.exe — the WiX MSI
    // (installTokenSaverService(), see src/Installers/TokenSaver.Setup) is the only path that
    // puts the exe on disk, at Program Files\TokenSaver.
    return tokenSaverInstalledExePath();
}

async function startServer() {
    const cfg = vscode.workspace.getConfiguration("tokenSaver");
    const port = cfg.get<number>("server.port", 11436);

    // ── Service mode ────────────────────────────────────────────────────────
    // If TokenSaverMCP is registered as a Windows service (installed via
    // src/Installers/TokenSaver.Setup, running from Program Files\TokenSaver), adopt it instead
    // of spawning our own bundled copy — same pattern as startSimpleModelServer's service-mode
    // branch above. No idle-shutdown timer is scheduled in this mode: a service is meant to
    // stay running independent of this VS Code window's lifecycle, unlike a
    // spawn()-managed child process this extension owns.
    if (await isServiceInstalled(TOKENSAVER_SERVICE_NAME)) {
        usingTokenSaverWindowsService = true;
        log("TokenSaverMCP Windows service detected.");
        if (!(await isPortOpen("127.0.0.1", port))) {
            log("Starting TokenSaverMCP service...");
            await runSc(["start", TOKENSAVER_SERVICE_NAME]);
        }
        try {
            await waitForPortOpen("127.0.0.1", port, cfg.get<number>("server.startTimeout", 30));
        } catch {
            log("TokenSaverMCP service did not become reachable in time.");
        }
        if (await verifyMcpServer(port)) {
            actualServerPort = port;
            updateStatus("running");
            log("TokenSaverMCP service is reachable.");
        } else {
            updateStatus("error");
            log(`TokenSaverMCP service is running but /mcp did not respond on port ${port}.`);
        }
        tokenSaverEventLog.start();
        return;
    }
    usingTokenSaverWindowsService = false;
    tokenSaverEventLog.stop();

    // ── Process mode (legacy: extension spawns and owns the child process) ──
    if (serverProcess) {
        updateStatus("running");
        scheduleIdleShutdown();
        return;
    }
    if (serverStartPromise) {
        await serverStartPromise;
        scheduleIdleShutdown();
        return;
    }
    if (stoppingServerProcess) await stoppingServerProcess;
    if (serverProcess) {
        updateStatus("running");
        scheduleIdleShutdown();
        return;
    }

    const existingPort = await findOpenPortInRange("127.0.0.1", port, 10);
    if (existingPort !== null) {
        if (await verifyMcpServer(existingPort)) {
            actualServerPort = existingPort;
            updateStatus("running");
            log(`Port ${existingPort} already open — adopting existing server.`);
            scheduleIdleShutdown();
            return;
        }
        log(
            `Port ${existingPort} is open but /mcp did not respond to verification — ` +
            `not adopting existing listener. Starting TokenSaver on port ${port}.`,
        );
    }

    updateStatus("starting");
    stdoutLineBuffer = "";

    const timeoutSec = cfg.get<number>("server.startTimeout", 30);
    const projectPathOverride = cfg.get<string>("dotnet.projectPath", "");
    const dotnet = cfg.get<string>("dotnet.command", "dotnet");

    let spawnArgs: { cmd: string; args: string[] };
    if (projectPathOverride) {
        // Dev mode: dotnet run --project <path>
        spawnArgs = {
            cmd: dotnet,
            args: ["run", "--project", projectPathOverride],
        };
    } else {
        // Installed mode: run the MSI-installed exe directly (Program Files\TokenSaver)
        spawnArgs = { cmd: mcpExePath(), args: [] };
    }

    log(`Launching: ${spawnArgs.cmd} ${spawnArgs.args.join(" ")}`);

    try {
        const workspaceRoot =
            vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ?? "";
        const autoCodeHelper = cfg.get<boolean>(
            "autoCodeHelper.enabled",
            false,
        );

        serverProcess = spawn(spawnArgs.cmd, spawnArgs.args, {
            env: {
                ...process.env,
                VSCODE_LOGS: "1",
                // Tells the server it was launched by the VS Code extension and should
                // use HTTP-only transport (no stdio MCP transport). Without this, the
                // server detects Console.IsInputRedirected=true (stdin=NUL from "ignore")
                // and adds the stdio transport, which immediately sees EOF and shuts down.
                TOKENSAVER_HTTP_ONLY: "1",
                // Tells TokenSaver.MCP where to write .local-mcp.
                // AddEnvironmentVariables("TOKENSAVER_") maps TOKENSAVER_TokenSaver__WorkspaceRoot
                // to the config key TokenSaver:WorkspaceRoot (double-underscore = section separator).
                // We also set the bare TOKENSAVER_WorkspaceRoot so GetWorkspaceRoot() can
                // read it directly as a fallback if section binding fails.
                ...(workspaceRoot
                    ? {
                          TOKENSAVER_TokenSaver__WorkspaceRoot: workspaceRoot,
                          TOKENSAVER_WorkspaceRoot: workspaceRoot,
                      }
                    : {}),
                // CodeHelper auto-workflow toggle
                TOKENSAVER_TokenSaver__CodeHelper__AutoEnabled:
                    String(autoCodeHelper),
            },
            stdio: ["ignore", "pipe", "pipe"],
            windowsHide: true,
        });
    } catch (err) {
        updateStatus("error");
        log(`Failed to spawn server: ${(err as Error).message}`);
        notify("error", "TokenSaver failed to start.");
        serverProcess = null;
        return;
    }

    if (!serverProcess.pid) {
        updateStatus("error");
        log("Server process failed to obtain a PID.");
        notify("error", "TokenSaver failed to start.");
        serverProcess = null;
        return;
    }

    serverProcess.stdout?.on("data", (chunk: Buffer) =>
        handleStdoutChunk(chunk),
    );
    serverProcess.stderr?.on("data", (chunk: Buffer) =>
        channelTokenSaver.append(chunk.toString()),
    );
    serverProcess.on("exit", (code, signal) => {
        actualServerPort = null;
        serverProcess = null;
        clearIdleTimer();
        // Treat as clean stop if: normal exit (code=0), no signal (signal=null, Windows),
        // or the stop was initiated by stopServer() (serverStopIntentional covers SIGTERM/SIGKILL
        // on macOS/Linux where proc.kill() results in code=null signal=SIGTERM rather than code=0).
        if (code === 0 || signal === null || serverStopIntentional) {
            updateStatus("stopped");
            notify("info", "TokenSaver stopped.");
        } else {
            updateStatus("error");
            log(`Server exited with code=${code} signal=${signal}`);
            notify("error", `TokenSaver exited with code ${code}`);
        }
        serverStopIntentional = false;
    });
    serverProcess.on("error", (err) => {
        serverProcess = null;
        clearIdleTimer();
        updateStatus("error");
        log(`Server error: ${err.message}`);
        notify("error", `TokenSaver error: ${err.message}`);
    });

    serverStartPromise = (async () => {
        try {
            await waitForPortOpen("127.0.0.1", port, timeoutSec);
            if (!(await verifyMcpServer(port))) {
                throw new Error(`Server process started but /mcp did not respond on port ${port}.`);
            }
            updateStatus("running");
            log("TokenSaver started and reachable.");
            notify("info", "TokenSaver started.");
            scheduleIdleShutdown();
        } catch (err) {
            updateStatus("error");
            log(`Startup timeout: ${(err as Error).message}`);
            serverProcess?.kill();
            serverProcess = null;
            throw err;
        } finally {
            serverStartPromise = null;
        }
    })();

    await serverStartPromise;
}

async function stopServer() {
    if (usingTokenSaverWindowsService) {
        log("Stopping TokenSaverMCP service...");
        await runSc(["stop", TOKENSAVER_SERVICE_NAME]);
        tokenSaverEventLog.stop();
        updateStatus("stopped");
        return;
    }
    clearIdleTimer();
    if (!serverProcess) {
        if (stoppingServerProcess) await stoppingServerProcess;
        updateStatus("stopped");
        return;
    }
    if (stoppingServerProcess) {
        await stoppingServerProcess;
        return;
    }

    const proc = serverProcess;
    serverProcess = null;
    serverStopIntentional = true; // suppress error notification for SIGTERM/SIGKILL on macOS/Linux
    let resolved = false;

    stoppingServerProcess = new Promise<void>((resolve) => {
        const cleanup = () => {
            if (resolved) return;
            resolved = true;
            stoppingServerProcess = null;
            updateStatus("stopped");
            log("TokenSaver stopped.");
            notify("info", "TokenSaver stopped.");
            resolve();
        };
        proc.once("exit", cleanup);
        proc.once("error", cleanup);
        proc.once("close", cleanup);
        proc.kill();
        setTimeout(() => {
            if (!proc.killed) proc.kill("SIGKILL");
            cleanup();
        }, 3000);
    });

    await stoppingServerProcess;
}

async function restartServer(context: vscode.ExtensionContext) {
    updateStatus("restarting");
    await stopServer();
    await stopSimpleModelServer();
    await startSimpleModelServer(context);
    await startServer();
}

// ─── Manual install prompt (VS Code Copilot, Claude Code, Cursor, Sixth, Continue) ──────────

/**
 * Opens INSTALL_PROMPT.md — instructions an AI agent (or the user) can follow to
 * manually wire up the TokenSaver MCP server in any supported provider (VS Code
 * Copilot, Claude Code, Cursor, Sixth AI, Continue), for either httpStreaming or
 * stdio transport. Replaces the old auto-write-every-provider's-config approach,
 * which proved unreliable across each provider's config format.
 */
async function openInstallPrompt(context: vscode.ExtensionContext) {
    const promptPath = path.join(context.extensionPath, "INSTALL_PROMPT.md");
    if (!fs.existsSync(promptPath)) {
        vscode.window.showErrorMessage(
            `TokenSaver: INSTALL_PROMPT.md not found at ${promptPath}`,
        );
        return;
    }
    const doc = await vscode.workspace.openTextDocument(promptPath);
    await vscode.window.showTextDocument(doc, { preview: false });
}

// NOTE: TokenSaver used to call /api/register-providers on startup (registerWithAiProviders)
// to auto-write its entry into every detected AI client config (Claude Desktop, Claude Code,
// VS Code mcp.json, Continue, Cursor, etc.). Removed along with the server-side auto-write —
// see the "TokenSaver: Open Install Prompt" command / INSTALL_PROMPT.md instead.

// ─── MCP provider ────────────────────────────────────────────────────────────

function registerMcpProvider(context: vscode.ExtensionContext) {
    try {
        if (
            typeof vscode.lm?.registerMcpServerDefinitionProvider === "function"
        ) {
            const provider: vscode.McpServerDefinitionProvider = {
                async provideMcpServerDefinitions() {
                    await startSimpleModelServer(context);
                    await startServer();
                    const port =
                        actualServerPort ??
                        vscode.workspace
                            .getConfiguration("tokenSaver")
                            .get<number>("server.port", 11436);
                    return [
                        new vscode.McpHttpServerDefinition(
                            "TokenSaver",
                            vscode.Uri.parse(`http://localhost:${port}`),
                            {},
                            "1.0.0",
                        ),
                    ];
                },
                async resolveMcpServerDefinition(
                    server,
                ) {
                    await startSimpleModelServer(context);
                    await startServer();
                    return server;
                },
            };
            context.subscriptions.push(
                vscode.lm.registerMcpServerDefinitionProvider(
                    "TokenSaver",
                    provider,
                ),
            );
        }
    } catch (err) {
        log(`Failed to register MCP provider: ${(err as Error).message}`);
    }
}

// ─── Slow-response health check ──────────────────────────────────────────────

const SLOW_RESPONSE_THRESHOLD_MS = 30_000;

/**
 * Wraps a fetch/call to the MCP server. If it takes longer than
 * SLOW_RESPONSE_THRESHOLD_MS, fires a SimpleModelServer health check in the
 * background after the call completes (or times out).
 */
export async function withSlowResponseWatch<T>(
    context: vscode.ExtensionContext,
    fn: () => Promise<T>,
): Promise<T> {
    const cfg = vscode.workspace.getConfiguration("tokenSaver");
    const healthUrl = cfg.get<string>(
        "simpleModelServer.healthUrl",
        "http://localhost:11435/health",
    );

    const start = Date.now();
    try {
        return await fn();
    } finally {
        if (Date.now() - start >= SLOW_RESPONSE_THRESHOLD_MS) {
            void checkAndHealSimpleModelServer(healthUrl, context);
        }
    }
}

// ─── status bar ──────────────────────────────────────────────────────────────

function updateStatus(
    state: "running" | "stopped" | "starting" | "restarting" | "error",
) {
    switch (state) {
        case "running": {
            const autoOn = vscode.workspace
                .getConfiguration("tokenSaver")
                .get<boolean>("autoCodeHelper.enabled", false);
            statusBar.text = autoOn
                ? "$(server) TokenSaver: Running $(rocket)"
                : "$(server) TokenSaver: Running";
            statusBar.tooltip = autoOn
                ? "TokenSaver running with CodeHelper auto-workflow active. Click for status."
                : "TokenSaver MCP server is running. Click for status.";
            break;
        }
        case "stopped":
            statusBar.text = "$(server) TokenSaver: Stopped";
            statusBar.tooltip =
                "TokenSaver MCP server is stopped. Click to start.";
            break;
        case "starting":
            statusBar.text = "$(sync~spin) TokenSaver: Starting…";
            statusBar.tooltip = "TokenSaver MCP server is starting.";
            break;
        case "restarting":
            statusBar.text = "$(sync~spin) TokenSaver: Restarting…";
            statusBar.tooltip = "TokenSaver MCP server is restarting.";
            break;
        case "error":
            statusBar.text = "$(issue-opened) TokenSaver: Error";
            statusBar.tooltip =
                "TokenSaver MCP server encountered an error. Click for logs.";
            break;
    }
    statusBar.show();
}

function showStatusInLog() {
    const cfg = vscode.workspace.getConfiguration("tokenSaver");
    channelTokenSaver.show(true);
    channelTokenSaver.appendLine("=== TokenSaver Status ===");
    channelTokenSaver.appendLine(
        `MCP server port (configured): ${cfg.get<number>("server.port", 11436)}`,
    );
    channelTokenSaver.appendLine(
        `MCP server actual port: ${actualServerPort ?? "(not running)"}`,
    );
    channelTokenSaver.appendLine(
        usingTokenSaverWindowsService
            ? "MCP server: running as Windows service (TokenSaverMCP)"
            : `MCP server process running: ${serverProcess !== null}`,
    );
    channelTokenSaver.appendLine(
        usingSimpleModelServerWindowsService
            ? "SimpleModelServer: running as Windows service (SimpleModelServer)"
            : `SimpleModelServer process running: ${simpleModelServerProcess !== null}`,
    );
    channelTokenSaver.appendLine(
        `SimpleModelServer health URL: ${cfg.get<string>("simpleModelServer.healthUrl", "http://localhost:11435/health")}`,
    );
    channelTokenSaver.appendLine("==========================");
}

// ─── notifications ───────────────────────────────────────────────────────────

function notify(type: "info" | "error", message: string) {
    const cfg = vscode.workspace.getConfiguration("tokenSaver.notifications");
    if (!cfg.get<boolean>("enabled", true)) return;
    if (cfg.get<string>("level", "all") === "errors" && type === "info") return;
    if (type === "error") {
        vscode.window.showErrorMessage(message);
    } else {
        vscode.window.showInformationMessage(message);
    }
}

function log(message: string) {
    channelTokenSaver?.appendLine(`${timestampPrefix()} ${message}`);
}

// ─── idle shutdown ───────────────────────────────────────────────────────────

function clearIdleTimer() {
    if (idleTimer) {
        clearTimeout(idleTimer);
        idleTimer = null;
    }
}

function scheduleIdleShutdown() {
    clearIdleTimer();
    const timeoutSec = vscode.workspace
        .getConfiguration("tokenSaver")
        .get<number>("server.idleTimeout", 0);
    if (!serverProcess || timeoutSec <= 0) return;
    idleTimer = setTimeout(async () => {
        log(`Idle timeout (${timeoutSec}s) reached — stopping server.`);
        await stopServer();
    }, timeoutSec * 1000);
}

// ─── port utilities ──────────────────────────────────────────────────────────



async function verifyMcpServer(
    port: number,
    timeoutMs = 5000,
): Promise<boolean> {
    try {
        // Use 127.0.0.1 instead of localhost to avoid Node.js resolving to ::1
        // (IPv6) when the server only listens on 0.0.0.0 (IPv4).
        const res = await fetch(
            `http://127.0.0.1:${port}/mcp`,
            { headers: authHeaders(await currentAuthToken()), signal: AbortSignal.timeout(timeoutMs) },
        );
        return isServerAlive(res.status);
    } catch {
        return false;
    }
}


// ── Windows Service helpers ───────────────────────────────────────────────────

/** Returns true if the given Windows service name is registered with the SCM. */

/** Runs sc.exe with the given args, returns exit code. */

/**
 * Installs SimpleModelServer as a Windows service by running the bundled
 * WiX MSI (see simpleModelServerMsiPath / ../SimpleModelServer/build/wix/SimpleModelServer.Setup) via msiexec, instead of
 * reimplementing the sc.exe create/config/failure sequence here — the MSI is the single
 * source of truth for how the service gets registered. Mirrors installTokenSaverService()
 * below. /qn runs the install silently; msiexec still shows its own UAC prompt since
 * installing a service requires admin rights.
 */
async function installSimpleModelServerService(context: vscode.ExtensionContext) {
    if (await isServiceInstalled(SIMPLEMODEL_SERVER_SERVICE_NAME)) {
        vscode.window.showInformationMessage("SimpleModelServer service is already installed.");
        return;
    }

    const msi = simpleModelServerMsiPath(context);
    if (!fs.existsSync(msi)) {
        vscode.window.showErrorMessage(
            `SimpleModelServer installer not found at: ${msi}\nRebuild the VSIX with build\\scripts\\build-vsix.ps1 (requires the wix CLI: dotnet tool install -g wix).`,
        );
        return;
    }

    log(`Running installer: ${msi}`);
    vscode.window.showInformationMessage(
        "Installing SimpleModelServer. A UAC prompt may appear.",
    );

    const code = await runMsiexec(["/i", msi, "/qn", "/norestart"]);
    if (code !== 0) {
        log(`Installer exited with code ${code}.`);
        vscode.window.showErrorMessage(
            `SimpleModelServer installer failed (msiexec exit code ${code}).`,
        );
        return;
    }

    usingSimpleModelServerWindowsService = true;
    log("SimpleModelServer service installed and started.");
    vscode.window.showInformationMessage(
        "SimpleModelServer installed as Windows service and started. It will now start automatically at login.",
    );

    // Mirror maybePromptForServiceInstall's chaining: once both services are up (this one just
    // finished, and TokenSaver.MCP either doesn't ship a bundled MSI or is already installed),
    // open INSTALL_PROMPT.md automatically so the user isn't left to discover that command
    // separately after running this one manually from the Command Palette.
    const mcpMsi = tokenSaverMsiPath(context);
    const mcpStillNeeded = fs.existsSync(mcpMsi) && !(await isServiceInstalled(TOKENSAVER_SERVICE_NAME));
    if (!mcpStillNeeded) {
        await openInstallPrompt(context);
    }
}

/**
 * Stops and removes the SimpleModelServer Windows service by running the bundled WiX MSI's
 * uninstall via msiexec (/x), mirroring uninstallTokenSaverService()'s approach of delegating
 * to the MSI rather than duplicating sc.exe stop/delete logic here.
 */
async function uninstallSimpleModelServerService(context?: vscode.ExtensionContext) {
    if (!(await isServiceInstalled(SIMPLEMODEL_SERVER_SERVICE_NAME))) {
        vscode.window.showInformationMessage("SimpleModelServer service is not installed.");
        return;
    }

    const msi = context ? simpleModelServerMsiPath(context) : "";
    if (!msi || !fs.existsSync(msi)) {
        // Bundled MSI unavailable (e.g. extension installed without it, or context missing) —
        // fall back to sc.exe stop/delete so uninstall still works.
        log("Bundled MSI not found — falling back to sc.exe stop/delete.");
        await runSc(["stop", SIMPLEMODEL_SERVER_SERVICE_NAME]);
        const scCode = await runSc(["delete", SIMPLEMODEL_SERVER_SERVICE_NAME]);
        if (scCode !== 0) {
            vscode.window.showErrorMessage(
                `sc.exe delete failed (code ${scCode}). Try running VS Code as Administrator.`,
            );
            return;
        }
    } else {
        log(`Running uninstaller: ${msi}`);
        vscode.window.showInformationMessage(
            "Uninstalling SimpleModelServer. A UAC prompt may appear.",
        );
        const code = await runMsiexec(["/x", msi, "/qn", "/norestart"]);
        if (code !== 0) {
            log(`Uninstaller exited with code ${code}.`);
            vscode.window.showErrorMessage(
                `SimpleModelServer uninstaller failed (msiexec exit code ${code}).`,
            );
            return;
        }
    }

    usingSimpleModelServerWindowsService = false;
    log("SimpleModelServer service uninstalled.");
    vscode.window.showInformationMessage(
        "SimpleModelServer Windows service removed. The extension will manage it as a process instead.",
    );

    // Reset the first-run install prompt's "already asked" flag so maybePromptForServiceInstall
    // offers again next activation. There's no reliable onWillUninstall-style hook for detecting
    // when the VSIX itself is removed, so this — the user explicitly uninstalling the service via
    // its Command Palette command — is the closest reliable signal that "ask again" makes sense
    // (e.g. reinstalling the extension fresh, or deliberately tearing the service down to redo
    // setup). Only clears if context is available (see the MSI-path fallback above for why this
    // can be undefined).
    if (context) {
        await context.globalState.update(SERVICE_INSTALL_PROMPT_ASKED_KEY, false);
    }
}

/**
 * Runs msiexec with the given arguments and waits for it to finish.
 *
 * IMPORTANT: msiexec does NOT show its own UAC prompt when invoked with /qn (quiet, no UI) —
 * /qn suppresses all UI, including the elevation dialog itself. A prior version of this
 * function called execFile("msiexec.exe", ...) directly, on the theory that Windows Installer
 * (a system service invoked via COM/RPC) elevates itself independent of the caller. That
 * theory was wrong in the /qn case: with no UI to show, there's nothing for the user to click
 * "Yes" on, so an unelevated caller just gets error 1925 ("insufficient privileges") for any
 * perMachine MSI — which is exactly what happened here (msiexec exit code 1603, no UAC prompt
 * ever appeared). See TokenSaver.Setup/Product.wxs's Scope="perMachine".
 *
 * Fix: shell out through PowerShell's Start-Process -Verb RunAs, which *does* trigger a real
 * UAC consent dialog regardless of /qn, then wait for it and propagate the MSI's exit code.
 * -Wait blocks until msiexec finishes; -PassThru + $proc.ExitCode gets us the real code (Start-
 * Process alone doesn't forward it to the parent process's own exit code the way execFile
 * expects). If the user declines the UAC prompt, Start-Process throws and we surface that as
 * a distinct failure instead of miscounting it as an MSI-side error.
 */

/**
 * Installs TokenSaver.MCP as a Windows service (TokenSaverMCP) by running the bundled WiX
 * MSI (see tokenSaverMsiPath / src/Installers/TokenSaver.Setup) via msiexec, instead of
 * reimplementing the sc.exe create/config/description/failure sequence here — the MSI is now
 * the single source of truth for how the service gets registered, and this stays in sync
 * with it automatically. /qn runs the install silently (no wizard UI); msiexec still shows
 * its own UAC prompt since installing a service requires admin rights.
 */
async function installTokenSaverService(context: vscode.ExtensionContext) {
    if (await isServiceInstalled(TOKENSAVER_SERVICE_NAME)) {
        vscode.window.showInformationMessage("TokenSaverMCP service is already installed.");
        return;
    }

    const msi = tokenSaverMsiPath(context);
    if (!fs.existsSync(msi)) {
        vscode.window.showErrorMessage(
            `TokenSaver installer not found at: ${msi}\nRebuild the VSIX with build\\scripts\\build-vsix.ps1 (requires the wix CLI: dotnet tool install -g wix).`,
        );
        return;
    }

    log(`Running installer: ${msi}`);
    vscode.window.showInformationMessage(
        "Installing TokenSaver MCP. A UAC prompt may appear.",
    );

    const code = await runMsiexec(["/i", msi, "/qn", "/norestart"]);
    if (code !== 0) {
        log(`Installer exited with code ${code}.`);
        vscode.window.showErrorMessage(
            `TokenSaver installer failed (msiexec exit code ${code}).`,
        );
        return;
    }

    usingTokenSaverWindowsService = true;
    log("TokenSaverMCP service installed and started.");
    vscode.window.showInformationMessage(
        "TokenSaver.MCP installed as Windows service and started. It will now start automatically at login.",
    );

    // Mirror maybePromptForServiceInstall's chaining: once both services are up (this one just
    // finished, and SimpleModelServer either doesn't ship a bundled MSI or is already installed), open
    // INSTALL_PROMPT.md automatically so the user isn't left to discover that command separately
    // after running this one manually from the Command Palette.
    const simpleModelServerMsi = simpleModelServerMsiPath(context);
    const simpleModelServerStillNeeded = fs.existsSync(simpleModelServerMsi) && !(await isServiceInstalled(SIMPLEMODEL_SERVER_SERVICE_NAME));
    if (!simpleModelServerStillNeeded) {
        await openInstallPrompt(context);
    }
}

/**
 * Stops and removes the TokenSaverMCP Windows service by running the bundled WiX MSI's
 * uninstall via msiexec (/x), mirroring installTokenSaverService's approach of delegating to
 * the MSI rather than duplicating sc.exe stop/delete logic here.
 */
async function uninstallTokenSaverService(context?: vscode.ExtensionContext) {
    if (!(await isServiceInstalled(TOKENSAVER_SERVICE_NAME))) {
        vscode.window.showInformationMessage("TokenSaverMCP service is not installed.");
        return;
    }

    const msi = context ? tokenSaverMsiPath(context) : "";
    if (!msi || !fs.existsSync(msi)) {
        // Bundled MSI unavailable (e.g. extension installed without it, or context missing) —
        // fall back to sc.exe stop/delete so uninstall still works.
        log("Bundled MSI not found — falling back to sc.exe stop/delete.");
        await runSc(["stop", TOKENSAVER_SERVICE_NAME]);
        const scCode = await runSc(["delete", TOKENSAVER_SERVICE_NAME]);
        if (scCode !== 0) {
            vscode.window.showErrorMessage(
                `sc.exe delete failed (code ${scCode}). Try running VS Code as Administrator.`,
            );
            return;
        }
    } else {
        log(`Running uninstaller: ${msi}`);
        vscode.window.showInformationMessage(
            "Uninstalling TokenSaver MCP. A UAC prompt may appear.",
        );
        const code = await runMsiexec(["/x", msi, "/qn", "/norestart"]);
        if (code !== 0) {
            log(`Uninstaller exited with code ${code}.`);
            vscode.window.showErrorMessage(
                `TokenSaver uninstaller failed (msiexec exit code ${code}).`,
            );
            return;
        }
    }

    usingTokenSaverWindowsService = false;
    log("TokenSaverMCP service uninstalled.");
    vscode.window.showInformationMessage(
        "TokenSaver.MCP Windows service removed. The extension will manage it as a process instead.",
    );

    // See the matching comment in uninstallSimpleModelServerService: reset the first-run prompt's
    // "already asked" flag so maybePromptForServiceInstall offers again next activation, since
    // there's no reliable hook for "the VSIX was uninstalled" and this is the closest signal.
    if (context) {
        await context.globalState.update(SERVICE_INSTALL_PROMPT_ASKED_KEY, false);
    }
}