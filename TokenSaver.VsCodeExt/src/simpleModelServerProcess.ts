import { spawn, type ChildProcess } from "child_process";
import * as vscode from "vscode";
import { fetchHealthOk } from "./health.js";
import { isPortOpen, waitForPortOpen } from "./net.js";
import { isServiceInstalled, runSc } from "./windowsServices.js";

/** The Windows service name the SimpleModelServer MSI registers. */
export const SIMPLEMODEL_SERVER_SERVICE_NAME = "SimpleModelServer";

/** What the SimpleModelServer lifecycle needs from the extension. Members are called lazily. */
export interface SimpleModelServerDeps {
    log(message: string): void;
    /** The SimpleModelServer output channel; undefined before activation. */
    output(): { append(text: string): void; appendLine(text: string): void } | undefined;
    /** Event Log mirror of the SimpleModelServer service, started and stopped with it. */
    serviceEvents: { start(): void; stop(): void };
    /** Path of SimpleModelServer.Server.exe for process mode. */
    exePath(): string;
}

/**
 * Owns the SimpleModelServer (model router) process as seen from VS Code. Adopts the Windows service when it is
 * installed; otherwise adopts a listener already on the health URL, or spawns and owns a child process.
 */
export class SimpleModelServerProcess {
    private child: ChildProcess | null = null;
    private startPromise: Promise<void> | null = null;
    private stopping: Promise<void> | null = null;
    private healthCheckInProgress = false;
    private windowsService = false;

    constructor(private readonly deps: SimpleModelServerDeps) {}

    /** True while a child process this extension spawned is alive. */
    get hasChildProcess(): boolean {
        return this.child !== null;
    }

    /** True when the SimpleModelServer Windows service is the one serving requests. */
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
        // ── Service mode ────────────────────────────────────────────────────
        const cfg0 = vscode.workspace.getConfiguration("tokenSaver");
        const healthUrl0 = cfg0.get<string>("simpleModelServer.healthUrl", "http://localhost:11435/health");
        const port0 = parseInt(new URL(healthUrl0).port, 10);

        if (await isServiceInstalled(SIMPLEMODEL_SERVER_SERVICE_NAME)) {
            this.windowsService = true;
            this.deps.log("SimpleModelServer Windows service detected.");
            if (!(await isPortOpen("127.0.0.1", port0))) {
                this.deps.log("Starting SimpleModelServer service...");
                await runSc(["start", SIMPLEMODEL_SERVER_SERVICE_NAME]);
            }
            const timeoutSec0 = cfg0.get<number>("simpleModelServer.startTimeout", 120);
            try {
                await waitForPortOpen("127.0.0.1", port0, timeoutSec0);
            } catch {
                this.deps.log("SimpleModelServer service did not become reachable in time.");
            }
            await this.checkAndHeal(healthUrl0);
            this.deps.serviceEvents.start();
            return;
        }
        this.windowsService = false;

        // ── Process mode (legacy) ───────────────────────────────────────────
        if (this.child) return;
        if (this.startPromise) {
            await this.startPromise;
            return;
        }
        if (this.stopping) await this.stopping;
        if (this.child) return;

        const cfg = vscode.workspace.getConfiguration("tokenSaver");
        const healthUrl = cfg.get<string>("simpleModelServer.healthUrl", "http://localhost:11435/health");
        const timeoutSec = cfg.get<number>("simpleModelServer.startTimeout", 120);
        const exe = this.deps.exePath();

        // If something is already listening at the health URL, adopt it.
        const port = new URL(healthUrl).port;
        if (await isPortOpen("127.0.0.1", parseInt(port, 10))) {
            this.deps.log(`SimpleModelServer already running on port ${port} — adopted.`);
            await this.checkAndHeal(healthUrl);
            return;
        }

        this.deps.log(`Launching SimpleModelServer: ${exe}`);

        let proc: ChildProcess;
        try {
            proc = spawn(exe, ["--service"], {
                stdio: ["ignore", "pipe", "pipe"],
                windowsHide: true,
            });
        } catch (err) {
            this.deps.log(`Failed to spawn SimpleModelServer: ${(err as Error).message}`);
            return;
        }

        if (!proc.pid) {
            this.deps.log("SimpleModelServer failed to obtain a PID.");
            return;
        }

        this.child = proc;

        proc.stdout?.on("data", (chunk: Buffer) => {
            for (const line of chunk.toString().split(/\r?\n/).filter(Boolean)) {
                this.deps.output()?.appendLine(line);
            }
        });
        proc.stderr?.on("data", (chunk: Buffer) => this.deps.output()?.append(chunk.toString()));
        proc.on("exit", (code, signal) => {
            this.child = null;
            if (code !== 0 && signal == null) {
                this.deps.log(`SimpleModelServer exited with code=${code} signal=${signal}`);
            }
        });
        proc.on("error", (err) => {
            this.child = null;
            this.deps.log(`SimpleModelServer error: ${err.message}`);
        });

        this.startPromise = (async () => {
            try {
                await waitForPortOpen("127.0.0.1", parseInt(port, 10), timeoutSec);
                this.deps.log("SimpleModelServer is reachable.");
                await this.checkAndHeal(healthUrl);
            } catch (err) {
                this.deps.log(`SimpleModelServer startup timeout: ${(err as Error).message}`);
                proc.kill();
                this.child = null;
            } finally {
                this.startPromise = null;
            }
        })();

        await this.startPromise;
    }

    async stop(): Promise<void> {
        if (this.windowsService) {
            this.deps.log("Stopping SimpleModelServer service...");
            await runSc(["stop", SIMPLEMODEL_SERVER_SERVICE_NAME]);
            this.deps.serviceEvents.stop();
            return;
        }
        if (!this.child) {
            if (this.stopping) await this.stopping;
            return;
        }
        if (this.stopping) {
            await this.stopping;
            return;
        }

        const proc = this.child;
        this.child = null;
        let resolved = false;

        this.stopping = new Promise<void>((resolve) => {
            const cleanup = () => {
                if (!resolved) {
                    resolved = true;
                    this.stopping = null;
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

        await this.stopping;
    }

    /**
     * Hits /health; if the response is not healthy, stops and starts SimpleModelServer again. Called on initial start
     * and after any slow MCP response. Re-entrant calls are ignored while one check is running.
     */
    async checkAndHeal(healthUrl: string): Promise<void> {
        if (this.healthCheckInProgress) return;
        this.healthCheckInProgress = true;

        try {
            const healthy = await fetchHealthOk(healthUrl);
            if (healthy) {
                this.deps.log("SimpleModelServer health: ok");
                return;
            }

            this.deps.log("SimpleModelServer health check failed — restarting...");
            await this.stop();
            await this.start();
        } catch (err) {
            this.deps.log(`Health check error: ${(err as Error).message}`);
        } finally {
            this.healthCheckInProgress = false;
        }
    }
}
