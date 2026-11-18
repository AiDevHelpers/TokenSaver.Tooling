import { spawn, type ChildProcess } from "child_process";
import * as vscode from "vscode";
import { findOpenPortInRange, isPortOpen, waitForPortOpen } from "./net.js";
import { formatLogLine, listeningPort, splitChunk } from "./stdoutRouting.js";
import { isServiceInstalled, runSc } from "./windowsServices.js";

/** The Windows service name the MSI registers for TokenSaver.MCP. */
export const TOKENSAVER_SERVICE_NAME = "TokenSaverMCP";

export type ServerState = "running" | "stopped" | "starting" | "restarting" | "error";

/** What the lifecycle needs from the extension. Each member is called lazily, so the channels can be created later. */
export interface McpServerDeps {
    log(message: string): void;
    notify(type: "info" | "error", message: string): void;
    updateStatus(state: ServerState): void;
    /** The TokenSaver output channel; undefined before activation. */
    output(): { append(text: string): void; appendLine(text: string): void } | undefined;
    /** Event Log mirror of the TokenSaverMCP service, started and stopped with it. */
    serviceEvents: { start(): void; stop(): void };
    /** True when /mcp answers on the port. */
    verify(port: number): Promise<boolean>;
    /** The TokenSaver.MCP executable (or project) to launch in child-process mode. */
    exePath(): string;
}

/**
 * Owns the TokenSaver.MCP server as seen from VS Code: adopts the Windows service when it is installed, otherwise
 * spawns and owns a child process, and shuts that child down after the configured idle time.
 */
export class McpServerProcess {
    private child: ChildProcess | null = null;
    private startPromise: Promise<void> | null = null;
    private stopping: Promise<void> | null = null;
    private idleTimer: ReturnType<typeof setTimeout> | null = null;
    private stopIntentional = false; // true while stop() is in progress; see the exit handler
    private stdoutBuffer = "";
    private currentPort: number | null = null;
    private windowsService = false;

    constructor(private readonly deps: McpServerDeps) {}

    /** Port the server answered on, or null when it is not running. */
    get port(): number | null {
        return this.currentPort;
    }

    /** True while a child process this extension spawned is alive. */
    get hasChildProcess(): boolean {
        return this.child !== null;
    }

    /** True when the TokenSaverMCP Windows service is the one serving requests. */
    get usingWindowsService(): boolean {
        return this.windowsService;
    }

    set usingWindowsService(value: boolean) {
        this.windowsService = value;
    }

    /** Kills the child process, if there is one. Used on deactivate. */
    killChild(): void {
        this.child?.kill();
    }

    async start(): Promise<void> {
        const cfg = vscode.workspace.getConfiguration("tokenSaver");
        const port = cfg.get<number>("server.port", 11436);

        // ── Service mode ────────────────────────────────────────────────────────
        // If TokenSaverMCP is registered as a Windows service (installed via
        // src/Installers/TokenSaver.Setup, running from Program Files\TokenSaver), adopt it instead
        // of spawning our own bundled copy. No idle-shutdown timer is scheduled in this mode: a
        // service is meant to stay running independent of this VS Code window's lifecycle.
        if (await isServiceInstalled(TOKENSAVER_SERVICE_NAME)) {
            this.windowsService = true;
            this.deps.log("TokenSaverMCP Windows service detected.");
            if (!(await isPortOpen("127.0.0.1", port))) {
                this.deps.log("Starting TokenSaverMCP service...");
                await runSc(["start", TOKENSAVER_SERVICE_NAME]);
            }
            try {
                await waitForPortOpen("127.0.0.1", port, cfg.get<number>("server.startTimeout", 30));
            } catch {
                this.deps.log("TokenSaverMCP service did not become reachable in time.");
            }
            if (await this.deps.verify(port)) {
                this.currentPort = port;
                this.deps.updateStatus("running");
                this.deps.log("TokenSaverMCP service is reachable.");
            } else {
                this.deps.updateStatus("error");
                this.deps.log(`TokenSaverMCP service is running but /mcp did not respond on port ${port}.`);
            }
            this.deps.serviceEvents.start();
            return;
        }
        this.windowsService = false;
        this.deps.serviceEvents.stop();

        // ── Process mode: the extension spawns and owns the child process ──────
        if (this.child) {
            this.deps.updateStatus("running");
            this.scheduleIdleShutdown();
            return;
        }
        if (this.startPromise) {
            await this.startPromise;
            this.scheduleIdleShutdown();
            return;
        }
        if (this.stopping) await this.stopping;
        if (this.child) {
            this.deps.updateStatus("running");
            this.scheduleIdleShutdown();
            return;
        }

        const existingPort = await findOpenPortInRange("127.0.0.1", port, 10);
        if (existingPort !== null) {
            if (await this.deps.verify(existingPort)) {
                this.currentPort = existingPort;
                this.deps.updateStatus("running");
                this.deps.log(`Port ${existingPort} already open — adopting existing server.`);
                this.scheduleIdleShutdown();
                return;
            }
            this.deps.log(
                `Port ${existingPort} is open but /mcp did not respond to verification — ` +
                `not adopting existing listener. Starting TokenSaver on port ${port}.`,
            );
        }

        this.deps.updateStatus("starting");
        this.stdoutBuffer = "";

        const timeoutSec = cfg.get<number>("server.startTimeout", 30);
        const projectPathOverride = cfg.get<string>("dotnet.projectPath", "");
        const dotnet = cfg.get<string>("dotnet.command", "dotnet");

        let spawnArgs: { cmd: string; args: string[] };
        if (projectPathOverride) {
            // Dev mode: dotnet run --project <path>
            spawnArgs = { cmd: dotnet, args: ["run", "--project", projectPathOverride] };
        } else {
            // Installed mode: run the MSI-installed exe directly (Program Files\TokenSaver)
            spawnArgs = { cmd: this.deps.exePath(), args: [] };
        }

        this.deps.log(`Launching: ${spawnArgs.cmd} ${spawnArgs.args.join(" ")}`);

        let child: ChildProcess;
        try {
            const workspaceRoot = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ?? "";
            const autoCodeHelper = cfg.get<boolean>("autoCodeHelper.enabled", false);

            child = spawn(spawnArgs.cmd, spawnArgs.args, {
                env: {
                    ...process.env,
                    VSCODE_LOGS: "1",
                    // Tells the server it was launched by the VS Code extension and should use HTTP-only
                    // transport. Without this, the server sees stdin=NUL, adds the stdio transport, reads EOF and exits.
                    TOKENSAVER_HTTP_ONLY: "1",
                    // Maps to the config key TokenSaver:WorkspaceRoot (double underscore = section separator).
                    // The bare name is a fallback if section binding fails.
                    ...(workspaceRoot
                        ? {
                              TOKENSAVER_TokenSaver__WorkspaceRoot: workspaceRoot,
                              TOKENSAVER_WorkspaceRoot: workspaceRoot,
                          }
                        : {}),
                    // CodeHelper auto-workflow toggle
                    TOKENSAVER_TokenSaver__CodeHelper__AutoEnabled: String(autoCodeHelper),
                },
                stdio: ["ignore", "pipe", "pipe"],
                windowsHide: true,
            });
        } catch (err) {
            this.deps.updateStatus("error");
            this.deps.log(`Failed to spawn server: ${(err as Error).message}`);
            this.deps.notify("error", "TokenSaver failed to start.");
            this.child = null;
            return;
        }

        if (!child.pid) {
            this.deps.updateStatus("error");
            this.deps.log("Server process failed to obtain a PID.");
            this.deps.notify("error", "TokenSaver failed to start.");
            this.child = null;
            return;
        }

        this.child = child;
        child.stdout?.on("data", (chunk: Buffer) => this.handleStdout(chunk));
        child.stderr?.on("data", (chunk: Buffer) => this.deps.output()?.append(chunk.toString()));
        child.on("exit", (code, signal) => {
            this.currentPort = null;
            this.child = null;
            this.clearIdleTimer();
            // Clean stop: exit code 0, no signal (Windows reports null), or a stop started by stop(). On macOS and Linux
            // proc.kill() gives code=null and signal=SIGTERM rather than code=0, hence the stopIntentional check.
            if (code === 0 || signal === null || this.stopIntentional) {
                this.deps.updateStatus("stopped");
                this.deps.notify("info", "TokenSaver stopped.");
            } else {
                this.deps.updateStatus("error");
                this.deps.log(`Server exited with code=${code} signal=${signal}`);
                this.deps.notify("error", `TokenSaver exited with code ${code}`);
            }
            this.stopIntentional = false;
        });
        child.on("error", (err) => {
            this.child = null;
            this.clearIdleTimer();
            this.deps.updateStatus("error");
            this.deps.log(`Server error: ${err.message}`);
            this.deps.notify("error", `TokenSaver error: ${err.message}`);
        });

        this.startPromise = (async () => {
            try {
                await waitForPortOpen("127.0.0.1", port, timeoutSec);
                if (!(await this.deps.verify(port))) {
                    throw new Error(`Server process started but /mcp did not respond on port ${port}.`);
                }
                this.deps.updateStatus("running");
                this.deps.log("TokenSaver started and reachable.");
                this.deps.notify("info", "TokenSaver started.");
                this.scheduleIdleShutdown();
            } catch (err) {
                this.deps.updateStatus("error");
                this.deps.log(`Startup timeout: ${(err as Error).message}`);
                this.child?.kill();
                this.child = null;
                throw err;
            } finally {
                this.startPromise = null;
            }
        })();

        await this.startPromise;
    }

    async stop(): Promise<void> {
        if (this.windowsService) {
            this.deps.log("Stopping TokenSaverMCP service...");
            await runSc(["stop", TOKENSAVER_SERVICE_NAME]);
            this.deps.serviceEvents.stop();
            this.deps.updateStatus("stopped");
            return;
        }
        this.clearIdleTimer();
        if (!this.child) {
            if (this.stopping) await this.stopping;
            this.deps.updateStatus("stopped");
            return;
        }
        if (this.stopping) {
            await this.stopping;
            return;
        }

        const proc = this.child;
        this.child = null;
        this.stopIntentional = true; // suppress the error notification for SIGTERM/SIGKILL on macOS/Linux
        let resolved = false;

        this.stopping = new Promise<void>((resolve) => {
            const cleanup = () => {
                if (resolved) return;
                resolved = true;
                this.stopping = null;
                this.deps.updateStatus("stopped");
                this.deps.log("TokenSaver stopped.");
                this.deps.notify("info", "TokenSaver stopped.");
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

        await this.stopping;
    }

    private clearIdleTimer(): void {
        if (this.idleTimer) {
            clearTimeout(this.idleTimer);
            this.idleTimer = null;
        }
    }

    private scheduleIdleShutdown(): void {
        this.clearIdleTimer();
        const timeoutSec = vscode.workspace.getConfiguration("tokenSaver").get<number>("server.idleTimeout", 0);
        if (!this.child || timeoutSec <= 0) return;
        this.idleTimer = setTimeout(async () => {
            this.deps.log(`Idle timeout (${timeoutSec}s) reached — stopping server.`);
            await this.stop();
        }, timeoutSec * 1000);
    }

    /** Stdout of the child: JSON log events and plain text to the channel, and the "Now listening on" port. */
    private handleStdout(chunk: Buffer): void {
        const { lines, rest } = splitChunk(this.stdoutBuffer, chunk.toString());
        this.stdoutBuffer = rest;
        for (const line of lines) {
            if (!line) continue;
            const port = listeningPort(line);
            if (port !== null) {
                this.currentPort = port;
                this.deps.log(`Server reported port: ${this.currentPort}`);
            }
            for (const out of formatLogLine(line)) {
                this.deps.output()?.appendLine(out);
            }
        }
    }
}
