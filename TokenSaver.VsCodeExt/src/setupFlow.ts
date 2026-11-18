import * as fs from "fs";
import * as path from "path";
import type { ExtensionContext } from "vscode";
import { SIMPLEMODEL_SERVER_SERVICE_NAME, TOKENSAVER_SERVICE_NAME, simpleModelServerMsiPath, tokenSaverMsiPath } from "./installPaths.js";

/** globalState key: the first-run install prompt has been answered (or found unnecessary). */
export const SERVICE_INSTALL_PROMPT_ASKED_KEY = "tokenSaver.serviceInstallPromptAsked";

export interface SetupSettings {
    healthUrl: string;
    simpleModelServerTimeoutSec: number;
    port: number;
    mcpTimeoutSec: number;
    /** tokenSaver.autoInstallPrompt. */
    autoInstallPrompt: boolean;
}

/** What the setup flows need from the extension. Nothing here imports the vscode API at runtime. */
export interface SetupDeps {
    log(message: string): void;
    notify(type: "info" | "error", message: string): void;
    info(message: string): void;
    error(message: string): void;
    /** Asks the user a question with buttons; resolves to the chosen label. */
    ask(message: string, ...labels: string[]): Promise<string | undefined>;
    settings(): SetupSettings;
    isServiceInstalled(serviceName: string): Promise<boolean>;
    runSc(args: string[]): Promise<number>;
    waitForHealthOk(healthUrl: string, timeoutSec: number): Promise<boolean>;
    verifyMcp(port: number): Promise<boolean>;
    installSimpleModelServer(context: ExtensionContext): Promise<void>;
    installTokenSaver(context: ExtensionContext): Promise<void>;
    checkMcpFullHealth(): Promise<boolean>;
    openInstallPrompt(context: ExtensionContext): Promise<void>;
    /** Version of the VSIX currently installed, or undefined when it is not installed. */
    installedVsixVersion(): string | undefined;
    installVsix(vsixPath: string): Promise<void>;
}

/**
 * First-run prompt: when neither service is installed and a bundled MSI is present, asks once whether to run the full
 * setup. Never asks again once answered, so declining does not nag on every reload.
 */
export async function maybePromptForServiceInstall(context: ExtensionContext, deps: SetupDeps): Promise<void> {
    if (!deps.settings().autoInstallPrompt) return;
    if (context.globalState.get<boolean>(SERVICE_INSTALL_PROMPT_ASKED_KEY, false)) return;

    const mcpMsi = tokenSaverMsiPath(context.extensionPath);
    const simpleModelServerMsi = simpleModelServerMsiPath(context.extensionPath);
    const mcpMsiAvailable = fs.existsSync(mcpMsi);
    const simpleModelServerMsiAvailable = fs.existsSync(simpleModelServerMsi);
    if (!mcpMsiAvailable && !simpleModelServerMsiAvailable) return; // no bundled installers: nothing to offer

    const [mcpInstalled, simpleModelServerInstalled] = await Promise.all([
        deps.isServiceInstalled(TOKENSAVER_SERVICE_NAME),
        deps.isServiceInstalled(SIMPLEMODEL_SERVER_SERVICE_NAME),
    ]);
    const needsMcpInstall = mcpMsiAvailable && !mcpInstalled;
    const needsSimpleModelServerInstall = simpleModelServerMsiAvailable && !simpleModelServerInstalled;
    if (!needsMcpInstall && !needsSimpleModelServerInstall) {
        // Already installed (or nothing bundled for what is missing): remember that we need not ask.
        await context.globalState.update(SERVICE_INSTALL_PROMPT_ASKED_KEY, true);
        return;
    }

    // Marked before the dialog so a reload or crash mid-dialog cannot cause a loop.
    await context.globalState.update(SERVICE_INSTALL_PROMPT_ASKED_KEY, true);

    const choice = await deps.ask(
        "TokenSaver can install itself and SimpleModelServer as background Windows services (auto-starts at login, no VS Code window required). Install now? A UAC prompt may appear.",
        "Install",
        "Not now",
    );
    if (choice !== "Install") {
        deps.log("Service auto-install declined by user.");
        return;
    }

    await runFullSetup(context, deps);
}

/**
 * One-shot setup: installs SimpleModelServer, waits for it to be healthy, installs TokenSaver.MCP, waits for it to answer
 * on /mcp, then opens the install instructions. Stops at the first failed step. Each step re-checks the real system state
 * rather than trusting that the previous step's promise resolved.
 */
export async function runFullSetup(context: ExtensionContext, deps: SetupDeps): Promise<void> {
    deps.log("=== TokenSaver full setup starting ===");

    // Step 1: SimpleModelServer service.
    if (!(await deps.isServiceInstalled(SIMPLEMODEL_SERVER_SERVICE_NAME))) {
        await deps.installSimpleModelServer(context);
        if (!(await deps.isServiceInstalled(SIMPLEMODEL_SERVER_SERVICE_NAME))) {
            deps.log("Full setup: SimpleModelServer service is still not installed after install step — aborting.");
            deps.error(
                "TokenSaver setup failed: SimpleModelServer did not install. See the SimpleModelServer output channel for details.",
            );
            return;
        }
    } else {
        deps.log("Full setup: SimpleModelServer service already installed — skipping install.");
    }

    // Step 2: SimpleModelServer must actually be healthy. First runs can take minutes (model download and warm-up).
    const settings = deps.settings();
    deps.log(`Full setup: waiting up to ${settings.simpleModelServerTimeoutSec}s for SimpleModelServer to report healthy...`);
    deps.info(
        "TokenSaver setup: waiting for SimpleModelServer to finish starting (first run can take several minutes while models download/warm up)...",
    );

    const firstWaitSec = Math.max(30, Math.floor(settings.simpleModelServerTimeoutSec / 3));
    let healthy = await deps.waitForHealthOk(settings.healthUrl, firstWaitSec);
    if (!healthy) {
        deps.log(
            `Full setup: SimpleModelServer still not healthy after ${firstWaitSec}s — restarting the service once ` +
                "and continuing to wait...",
        );
        await deps.runSc(["stop", SIMPLEMODEL_SERVER_SERVICE_NAME]);
        await new Promise((r) => setTimeout(r, 2000));
        await deps.runSc(["start", SIMPLEMODEL_SERVER_SERVICE_NAME]);
        healthy = await deps.waitForHealthOk(settings.healthUrl, settings.simpleModelServerTimeoutSec - firstWaitSec);
    }
    if (!healthy) {
        deps.log(`Full setup: SimpleModelServer did not become healthy within ${settings.simpleModelServerTimeoutSec}s — aborting.`);
        deps.error(
            `TokenSaver setup failed: SimpleModelServer did not become healthy within ${settings.simpleModelServerTimeoutSec}s. Check the SimpleModelServer output channel.`,
        );
        return;
    }
    deps.log("Full setup: SimpleModelServer is healthy.");

    // Step 3: TokenSaver.MCP service.
    if (!(await deps.isServiceInstalled(TOKENSAVER_SERVICE_NAME))) {
        await deps.installTokenSaver(context);
        if (!(await deps.isServiceInstalled(TOKENSAVER_SERVICE_NAME))) {
            deps.log("Full setup: TokenSaverMCP service is still not installed after install step — aborting.");
            deps.error("TokenSaver setup failed: TokenSaver.MCP did not install. See the TokenSaver output channel for details.");
            return;
        }
    } else {
        deps.log("Full setup: TokenSaverMCP service already installed — skipping install.");
    }

    // Step 4: TokenSaver.MCP must answer on /mcp.
    deps.log(`Full setup: waiting up to ${settings.mcpTimeoutSec}s for TokenSaver.MCP to become reachable...`);
    if (!(await waitForMcpReachable(settings.port, settings.mcpTimeoutSec, deps))) {
        deps.log(`Full setup: TokenSaver.MCP did not become reachable within ${settings.mcpTimeoutSec}s — aborting.`);
        deps.error(
            `TokenSaver setup failed: TokenSaver.MCP did not respond on port ${settings.port} within ${settings.mcpTimeoutSec}s. Check the TokenSaver output channel.`,
        );
        return;
    }
    deps.log("Full setup: TokenSaver.MCP is reachable.");

    // The full /health self-test reports problems but does not abort setup (see checkMcpFullHealth).
    await deps.checkMcpFullHealth();

    // Step 5: both services are confirmed; open the install instructions.
    await deps.openInstallPrompt(context);
    deps.log("=== TokenSaver full setup complete ===");
    deps.info("TokenSaver setup complete: SimpleModelServer and TokenSaver.MCP are both installed and running.");
}

/** Polls the MCP endpoint until it answers or the timeout elapses. */
export async function waitForMcpReachable(port: number, timeoutSec: number, deps: Pick<SetupDeps, "verifyMcp">): Promise<boolean> {
    const deadline = Date.now() + timeoutSec * 1000;
    while (Date.now() < deadline) {
        if (await deps.verifyMcp(port)) return true;
        await new Promise((r) => setTimeout(r, 1000));
    }
    return false;
}

/**
 * Installs the bundled VSIX into VS Code when it is missing or a different version. A failed install is logged, not
 * fatal.
 */
export async function ensureInstalled(context: ExtensionContext, deps: SetupDeps): Promise<void> {
    const vsixPath = path.join(context.extensionPath, "tokensaver.vsix");
    if (!fs.existsSync(vsixPath)) return;

    const pkg = JSON.parse(fs.readFileSync(path.join(context.extensionPath, "package.json"), "utf8")) as { version?: string };
    if (deps.installedVsixVersion() === pkg.version) return;

    deps.log(`Installing VSIX ${vsixPath}...`);
    try {
        await deps.installVsix(vsixPath);
        deps.log("VSIX installed — restart VS Code to activate the updated extension.");
        deps.notify("info", "TokenSaver updated — please restart VS Code.");
    } catch (err) {
        deps.log(`VSIX install failed: ${(err as Error).message}`);
    }
}
