import * as vscode from "vscode";
import { isValidAccount, readAuthToken } from "./auth.js";
import * as path from "path";
import * as fs from "fs";
import { ServiceEventLogPoller } from "./serviceEventLog.js";
import { McpServerProcess, TOKENSAVER_SERVICE_NAME } from "./mcpServerProcess.js";
import { SimpleModelServerProcess, SIMPLEMODEL_SERVER_SERVICE_NAME } from "./simpleModelServerProcess.js";
import { checkMcpFullHealth as checkMcpFullHealthFlow, checkMcpHealth as checkMcpHealthFlow, verifyMcpServer as verifyMcpServerFn, type McpHealthDeps } from "./mcpHealth.js";
import { applyStatusBar, shouldNotify, statusReportLines, timestampPrefix, type ServerStatusState } from "./statusUi.js";
import { manageUsers as manageUsersFlow, type UserManagementDeps, type UserPick } from "./userManagement.js";
import { simpleModelServerMsiPath as simpleModelServerMsiPathFor, tokenSaverMsiPath as tokenSaverMsiPathFor } from "./installPaths.js";
import {
    SERVICE_INSTALL_PROMPT_ASKED_KEY,
    ensureInstalled as ensureInstalledFlow,
    maybePromptForServiceInstall as maybePromptForServiceInstallFlow,
    runFullSetup as runFullSetupFlow,
    type SetupDeps,
} from "./setupFlow.js";
import { installWindowsService, uninstallWindowsService, type ServiceInstallerDeps, type WindowsServiceSpec } from "./serviceInstaller.js";
import { waitForHealthOk } from "./health.js";
import { isServiceInstalled, runSc, runMsiexec } from "./windowsServices.js";
import { maybePromptAddTokenSaverToGitignore as maybePromptGitignore, migrateLegacySettings } from "./configPrompts.js";
import { openInstallPrompt, registerMcpProvider as registerMcpProviderFlow } from "./installPrompt.js";

let channelTokenSaver: vscode.OutputChannel;
let channelSimpleModelServer: vscode.OutputChannel;
let statusBar: vscode.StatusBarItem;
let logsStatusBar: vscode.StatusBarItem;

// TokenSaver.MCP process

// Windows event-log mirrors for the two services (see serviceEventLog.ts).
const tokenSaverEventLog: ServiceEventLogPoller = new ServiceEventLogPoller({
    source: "TokenSaver",
    isActive: (): boolean => mcpServer.usingWindowsService,
    channel: () => channelTokenSaver,
    onError: (message) => log(`TokenSaver service log poll failed: ${message}`),
});
const simpleModelServerEventLog = new ServiceEventLogPoller({
    source: "SimpleModelServer",
    isActive: (): boolean => simpleModelServer.usingWindowsService,
    channel: () => channelSimpleModelServer,
    onError: (message) => log(`SimpleModelServer service log poll failed: ${message}`),
});

// The SimpleModelServer router process, as seen from VS Code (see simpleModelServerProcess.ts).
const simpleModelServer: SimpleModelServerProcess = new SimpleModelServerProcess({
    log: (message) => log(message),
    output: () => channelSimpleModelServer,
    serviceEvents: simpleModelServerEventLog,
    exePath: () => simpleModelServerExePath(),
});

// The TokenSaver.MCP server, as seen from VS Code (see mcpServerProcess.ts).
const mcpServer: McpServerProcess = new McpServerProcess({
    log: (message) => log(message),
    notify: (type, message) => notify(type, message),
    updateStatus: (state) => updateStatus(state),
    output: () => channelTokenSaver,
    serviceEvents: tokenSaverEventLog,
    verify: (port) => verifyMcpServer(port),
    exePath: () => mcpExePath(),
});

// Windows service mode — true when the TokenSaverMCP SCM service (installed by
// src/Installers/TokenSaver.Setup, running from Program Files\TokenSaver) is managing the
// process. In this mode we do not spawn/kill a child process or run an idle-shutdown timer;
// we use sc.exe to start/stop and adopt whatever is already listening on the configured port.
// Mirrors the equivalent SimpleModelServer service-mode handling below.

// Windows service log polling (TokenSaverMCP)

// SimpleModelServer process

// Windows service log polling

// Windows service mode — true when SimpleModelServer SCM service is managing the process.
// In this mode we do not spawn/kill a child process; we use sc.exe to start/stop.

// ─── .tokensaver/config.json behavior config ─────────────────────────────────
//
// Mirrors TokenSaver.Config's BehaviorConfigService/TokenSaverBehaviorConfig/GitignorePrompt
// (src/Core/TokenSaver.Config) on the extension side. Two files, local wins over global on any
// set property:
//   - {workspaceRoot}/.tokensaver/config.json  (local)
//   - %ProgramData%\TokenSaver\config.json     (global)
// The extension has direct UI access (showInformationMessage), so it asks the user itself
// instead of relaying through the AI the way the MCP-side GitignorePrompt has to.

// Config files and first-run prompts live in behaviorConfigFile.ts, configPrompts.ts.

function maybePromptAddTokenSaverToGitignore(): Promise<void> {
    return maybePromptGitignore((message) => log(message));
}

export async function activate(context: vscode.ExtensionContext) {
    migrateLegacySettings();

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
            await simpleModelServer.start();
            await mcpServer.start();
        }),
        vscode.commands.registerCommand("tokenSaver.stopServer", async () => {
            await mcpServer.stop();
            await simpleModelServer.stop();
            // (context available in closure for potential future use)
        }),
        vscode.commands.registerCommand(
            "tokenSaver.restartServer",
            async () => {
                await restartServer();
            },
        ),
        vscode.commands.registerCommand("tokenSaver.showLogs", () => {
            channelTokenSaver.show(true);
        }),
        vscode.commands.registerCommand("tokenSaver.showStatus", async () => {
            await simpleModelServer.start();
            await mcpServer.start();
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
                await restartServer();
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
        await simpleModelServer.start();
        await mcpServer.start();
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





// NOTE: TokenSaver used to auto-write .vscode/mcp.json (ensureMcpJson) so Copilot Chat
// would pick up the server without the user doing anything. Removed along with the other
// provider auto-install paths — see the "TokenSaver: Open Install Prompt" command /
// INSTALL_PROMPT.md for the manual replacement, which covers VS Code Copilot too.


/** The endpoint token (tokenSaver.auth.tokenPath, else the default locations), or undefined when unreadable. */
async function currentAuthToken(): Promise<string | undefined> {
    const cfg = vscode.workspace.getConfiguration("tokenSaver");
    return readAuthToken(cfg.get<string>("auth.tokenPath", ""));
}






export function deactivate() {
    // Synchronous kills for the process.exit path — async stop won't run in time
    mcpServer.killChild();
    simpleModelServer.killChild();
}

// Ensure child processes are killed even if the extension host is terminated
// without calling deactivate (e.g. VS Code reload, crash, SIGTERM)
process.on("exit", () => {
    try {
        mcpServer.killChild();
    } catch {
        // Already exited or never started; nothing to clean up.
    }
    try {
        simpleModelServer.killChild();
    } catch {
        // Already exited or never started; nothing to clean up.
    }
});
process.on("SIGTERM", () => {
    try {
        mcpServer.killChild();
    } catch {
        // Already exited or never started; nothing to clean up.
    }
    try {
        simpleModelServer.killChild();
    } catch {
        // Already exited or never started; nothing to clean up.
    }
    process.exit(0);
});
process.on("SIGINT", () => {
    try {
        mcpServer.killChild();
    } catch {
        // Already exited or never started; nothing to clean up.
    }
    try {
        simpleModelServer.killChild();
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














// ─── log routing ─────────────────────────────────────────────────────────────



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



async function restartServer() {
    updateStatus("restarting");
    await mcpServer.stop();
    await simpleModelServer.stop();
    await simpleModelServer.start();
    await mcpServer.start();
}

// ─── Service and setup helpers (the flows live in setupFlow.ts) ───────────────

function tokenSaverMsiPath(context: vscode.ExtensionContext): string {
    return tokenSaverMsiPathFor(context.extensionPath);
}

function simpleModelServerMsiPath(context: vscode.ExtensionContext): string {
    return simpleModelServerMsiPathFor(context.extensionPath);
}

const setupDeps: SetupDeps = {
    log: (message) => log(message),
    notify: (type, message) => notify(type, message),
    info: (message) => void vscode.window.showInformationMessage(message),
    error: (message) => void vscode.window.showErrorMessage(message),
    ask: (message, ...labels) => Promise.resolve(vscode.window.showInformationMessage(message, ...labels)),
    settings: () => {
        const cfg = vscode.workspace.getConfiguration("tokenSaver");
        return {
            healthUrl: cfg.get<string>("simpleModelServer.healthUrl", "http://localhost:11435/health"),
            simpleModelServerTimeoutSec: cfg.get<number>("simpleModelServer.startTimeout", 600),
            port: cfg.get<number>("server.port", 11436),
            mcpTimeoutSec: cfg.get<number>("server.startTimeout", 30),
            autoInstallPrompt: cfg.get<boolean>("autoInstallPrompt", true),
        };
    },
    isServiceInstalled: (serviceName) => isServiceInstalled(serviceName),
    runSc: (args) => runSc(args),
    waitForHealthOk: (healthUrl, timeoutSec) => waitForHealthOk(healthUrl, timeoutSec),
    verifyMcp: (port) => verifyMcpServer(port),
    installSimpleModelServer: (context) => installSimpleModelServerService(context),
    installTokenSaver: (context) => installTokenSaverService(context),
    checkMcpFullHealth: () => checkMcpFullHealth(),
    openInstallPrompt: (context) => openInstallPrompt(context),
    installedVsixVersion: () =>
        vscode.extensions.getExtension("local.tokensaver-vscode")?.packageJSON?.version as string | undefined,
    installVsix: async (vsixPath) => {
        const { execFile } = await import("child_process");
        await new Promise<void>((resolve, reject) => {
            execFile("code", ["--install-extension", vsixPath, "--force"], (err) => (err ? reject(err) : resolve()));
        });
    },
};

async function maybePromptForServiceInstall(context: vscode.ExtensionContext): Promise<void> {
    await maybePromptForServiceInstallFlow(context, setupDeps);
}

async function runFullSetup(context: vscode.ExtensionContext): Promise<void> {
    await runFullSetupFlow(context, setupDeps);
}

async function ensureInstalled(context: vscode.ExtensionContext): Promise<void> {
    await ensureInstalledFlow(context, setupDeps);
}

// ─── Health, users and status (the logic lives in mcpHealth.ts, userManagement.ts and statusUi.ts) ─

function registerMcpProvider(context: vscode.ExtensionContext): void {
    registerMcpProviderFlow(context, {
        log: (message) => log(message),
        startServers: async () => {
            await simpleModelServer.start();
            await mcpServer.start();
        },
        port: () => mcpPort(),
    });
}

function mcpPort(): number {
    return mcpServer.port ?? vscode.workspace.getConfiguration("tokenSaver").get<number>("server.port", 11436);
}

function mcpHealthDeps(): McpHealthDeps {
    return {
        log: (message) => log(message),
        notify: (type, message) => notify(type, message),
        port: () => mcpPort(),
        token: () => currentAuthToken(),
        stop: () => mcpServer.stop(),
        start: () => mcpServer.start(),
    };
}

function checkMcpHealth(): Promise<boolean> {
    return checkMcpHealthFlow(mcpHealthDeps());
}

function checkMcpFullHealth(): Promise<boolean> {
    return checkMcpFullHealthFlow({
        log: (message) => log(message),
        notify: (type, message) => notify(type, message),
        port: () => mcpPort(),
    });
}

async function verifyMcpServer(port: number, timeoutMs = 5000): Promise<boolean> {
    return verifyMcpServerFn(port, await currentAuthToken(), timeoutMs);
}

function manageUsers(): Promise<void> {
    return manageUsersFlow({
        port: () => mcpPort(),
        token: () => currentAuthToken(),
        warn: (message) => void vscode.window.showWarningMessage(message),
        info: (message) => void vscode.window.showInformationMessage(message),
        error: (message) => void vscode.window.showErrorMessage(message),
        pick: async (items, title) =>
            vscode.window.showQuickPick(items as UserPick[], {
                title,
                placeHolder: "Select a member to remove or an account to add",
            }),
        input: async () =>
            vscode.window.showInputBox({
                prompt: "Account to add (DOMAIN\\name or name)",
                validateInput: (v) => (isValidAccount(v) ? undefined : "Not a valid account name"),
            }),
    } satisfies UserManagementDeps);
}

function updateStatus(state: ServerStatusState): void {
    const autoOn = vscode.workspace.getConfiguration("tokenSaver").get<boolean>("autoCodeHelper.enabled", false);
    applyStatusBar(statusBar, state, autoOn);
}

function showStatusInLog(): void {
    const cfg = vscode.workspace.getConfiguration("tokenSaver");
    channelTokenSaver.show(true);
    for (const line of statusReportLines({
        configuredPort: cfg.get<number>("server.port", 11436),
        actualPort: mcpServer.port,
        mcpWindowsService: mcpServer.usingWindowsService,
        mcpChildRunning: mcpServer.hasChildProcess,
        simpleModelServerWindowsService: simpleModelServer.usingWindowsService,
        simpleModelServerChildRunning: simpleModelServer.hasChildProcess,
        simpleModelServerHealthUrl: cfg.get<string>("simpleModelServer.healthUrl", "http://localhost:11435/health"),
    })) {
        channelTokenSaver.appendLine(line);
    }
}

function notify(type: "info" | "error", message: string): void {
    const cfg = vscode.workspace.getConfiguration("tokenSaver.notifications");
    if (!shouldNotify(type, cfg.get<boolean>("enabled", true), cfg.get<string>("level", "all"))) return;
    if (type === "error") {
        vscode.window.showErrorMessage(message);
    } else {
        vscode.window.showInformationMessage(message);
    }
}

// ─── Manual install prompt (VS Code Copilot, Claude Code, Cursor, Sixth, Continue) ──────────


// NOTE: TokenSaver used to call /api/register-providers on startup (registerWithAiProviders)
// to auto-write its entry into every detected AI client config (Claude Desktop, Claude Code,
// VS Code mcp.json, Continue, Cursor, etc.). Removed along with the server-side auto-write —
// see the "TokenSaver: Open Install Prompt" command / INSTALL_PROMPT.md instead.

// ─── MCP provider ────────────────────────────────────────────────────────────


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
            void simpleModelServer.checkAndHeal(healthUrl);
        }
    }
}

// ─── status bar ──────────────────────────────────────────────────────────────



// ─── notifications ───────────────────────────────────────────────────────────


function log(message: string) {
    channelTokenSaver?.appendLine(`${timestampPrefix()} ${message}`);
}

// ─── idle shutdown ───────────────────────────────────────────────────────────



// ─── port utilities ──────────────────────────────────────────────────────────





// ── Windows Service helpers ───────────────────────────────────────────────────

/** Returns true if the given Windows service name is registered with the SCM. */

/** Runs sc.exe with the given args, returns exit code. */

// ─── Service install and uninstall (the flows live in serviceInstaller.ts) ───────────

const SIMPLEMODEL_SERVER_INSTALL: WindowsServiceSpec = {
    serviceName: SIMPLEMODEL_SERVER_SERVICE_NAME,
    label: "SimpleModelServer",
    logName: "SimpleModelServer",
    alreadyInstalled: "SimpleModelServer service is already installed.",
    installing: "Installing SimpleModelServer. A UAC prompt may appear.",
    installed: "SimpleModelServer installed as Windows service and started. It will now start automatically at login.",
    notInstalled: "SimpleModelServer service is not installed.",
    uninstalling: "Uninstalling SimpleModelServer. A UAC prompt may appear.",
    removed: "SimpleModelServer Windows service removed. The extension will manage it as a process instead.",
};

const TOKENSAVER_INSTALL: WindowsServiceSpec = {
    serviceName: TOKENSAVER_SERVICE_NAME,
    label: "TokenSaver",
    logName: "TokenSaverMCP",
    alreadyInstalled: "TokenSaverMCP service is already installed.",
    installing: "Installing TokenSaver MCP. A UAC prompt may appear.",
    installed: "TokenSaver.MCP installed as Windows service and started. It will now start automatically at login.",
    notInstalled: "TokenSaverMCP service is not installed.",
    uninstalling: "Uninstalling TokenSaver MCP. A UAC prompt may appear.",
    removed: "TokenSaver.MCP Windows service removed. The extension will manage it as a process instead.",
};

/** The VS Code side of the installers: messages, logging, process flags, and the follow-up steps. */
function installerDeps(hooks: {
    onInstalled?: () => void;
    onUninstalled?: () => void;
    afterInstall?: () => Promise<void>;
}): ServiceInstallerDeps {
    return {
        log: (message) => log(message),
        info: (message) => void vscode.window.showInformationMessage(message),
        error: (message) => void vscode.window.showErrorMessage(message),
        isInstalled: (serviceName) => isServiceInstalled(serviceName),
        runMsiexec: (args) => runMsiexec(args),
        runSc: (args) => runSc(args),
        onInstalled: hooks.onInstalled ?? (() => {}),
        onUninstalled: hooks.onUninstalled ?? (() => {}),
        afterInstall: hooks.afterInstall ?? (async () => {}),
    };
}

async function installSimpleModelServerService(context: vscode.ExtensionContext) {
    await installWindowsService(SIMPLEMODEL_SERVER_INSTALL, simpleModelServerMsiPath(context), installerDeps({
        onInstalled: () => {
            simpleModelServer.usingWindowsService = true;
        },
        // Once both services are done, open INSTALL_PROMPT.md so the user isn't left to find it themselves.
        afterInstall: async () => {
            const mcpMsi = tokenSaverMsiPath(context);
            const mcpStillNeeded = fs.existsSync(mcpMsi) && !(await isServiceInstalled(TOKENSAVER_SERVICE_NAME));
            if (!mcpStillNeeded) {
                await openInstallPrompt(context);
            }
        },
    }));
}

async function uninstallSimpleModelServerService(context?: vscode.ExtensionContext) {
    await uninstallWindowsService(
        SIMPLEMODEL_SERVER_INSTALL,
        context ? simpleModelServerMsiPath(context) : undefined,
        installerDeps({
            onUninstalled: () => {
                simpleModelServer.usingWindowsService = false;
            },
        }),
        context ? async () => { await context.globalState.update(SERVICE_INSTALL_PROMPT_ASKED_KEY, false); } : undefined,
    );
}

async function installTokenSaverService(context: vscode.ExtensionContext) {
    await installWindowsService(TOKENSAVER_INSTALL, tokenSaverMsiPath(context), installerDeps({
        onInstalled: () => {
            mcpServer.usingWindowsService = true;
        },
        afterInstall: async () => {
            const simpleModelServerMsi = simpleModelServerMsiPath(context);
            const simpleModelServerStillNeeded =
                fs.existsSync(simpleModelServerMsi) && !(await isServiceInstalled(SIMPLEMODEL_SERVER_SERVICE_NAME));
            if (!simpleModelServerStillNeeded) {
                await openInstallPrompt(context);
            }
        },
    }));
}

async function uninstallTokenSaverService(context?: vscode.ExtensionContext) {
    await uninstallWindowsService(
        TOKENSAVER_INSTALL,
        context ? tokenSaverMsiPath(context) : undefined,
        installerDeps({
            onUninstalled: () => {
                mcpServer.usingWindowsService = false;
            },
        }),
        context ? async () => { await context.globalState.update(SERVICE_INSTALL_PROMPT_ASKED_KEY, false); } : undefined,
    );
}




