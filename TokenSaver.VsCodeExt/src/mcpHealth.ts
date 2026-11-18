import { authHeaders, isServerAlive } from "./auth.js";

/** One entry of TokenSaver.MCP's /health response (see docs/Host-MCP/tokensaver-mcp-health-contract.md). */
export interface McpHealthCheckEntry {
    name?: string;
    status?: string;
    description?: string;
    error?: string | null;
}

export interface McpHealthReport {
    status?: string;
    checks?: McpHealthCheckEntry[];
}

export interface McpHealthDeps {
    log(message: string): void;
    notify(type: "info" | "error", message: string): void;
    /** The port the server answers on, or the configured one when it is not running. */
    port(): number;
    /** The endpoint token, or undefined when it cannot be read. */
    token(): Promise<string | undefined>;
    stop(): Promise<void>;
    start(): Promise<void>;
}

/**
 * True when /mcp answers. Uses 127.0.0.1, not localhost: the server listens on loopback only, and Node may resolve
 * localhost to ::1. A 401 still counts as up, since the token may just be unreadable.
 */
export async function verifyMcpServer(port: number, token: string | undefined, timeoutMs = 5000): Promise<boolean> {
    try {
        const res = await fetch(`http://127.0.0.1:${port}/mcp`, {
            headers: authHeaders(token),
            signal: AbortSignal.timeout(timeoutMs),
        });
        return isServerAlive(res.status);
    } catch {
        return false;
    }
}

/** Probes /mcp after startup. Unreachable (or an unexpected status) restarts the server. */
export async function checkMcpHealth(deps: McpHealthDeps): Promise<boolean> {
    const url = `http://127.0.0.1:${deps.port()}/mcp`;

    try {
        const res = await fetch(url, {
            headers: authHeaders(await deps.token()),
            signal: AbortSignal.timeout(5000),
        });
        // 401 means the server is up but did not accept (or we could not read) the token: not a reason to restart.
        if (isServerAlive(res.status)) {
            deps.log("MCP server health: ok");
            return true;
        }
        deps.log(`MCP server health check returned ${res.status} — restarting...`);
    } catch {
        deps.log("MCP server unreachable after start — restarting...");
    }

    await deps.stop();
    try {
        await deps.start();
        return true;
    } catch {
        return false;
    }
}

/**
 * Reads /health, which holds the fault-tracking and self-test results. It only logs and notifies: Degraded can be
 * normal (models still warming up), so the server is not restarted for it. Returns false only when the endpoint could not
 * be reached or parsed.
 */
export async function checkMcpFullHealth(deps: Pick<McpHealthDeps, "log" | "notify" | "port">): Promise<boolean> {
    const url = `http://127.0.0.1:${deps.port()}/health`;

    let report: McpHealthReport;
    try {
        const res = await fetch(url, { signal: AbortSignal.timeout(10000) });
        report = (await res.json()) as McpHealthReport;
    } catch (err) {
        deps.log(`TokenSaver.MCP full self-test (/health) unreachable: ${(err as Error).message}`);
        return false;
    }

    const status = report.status ?? "Unknown";
    deps.log(`TokenSaver.MCP full self-test (/health): ${status}`);

    const failingChecks = (report.checks ?? []).filter((c) => c.status && c.status !== "Healthy");
    if (failingChecks.length === 0) {
        return true;
    }

    for (const check of failingChecks) {
        const detail = check.error ?? check.description ?? "(no detail)";
        deps.log(`  ${check.name ?? "(unnamed check)"}: ${check.status} — ${detail}`);
    }

    if (status !== "Healthy") {
        deps.notify(
            "error",
            `TokenSaver.MCP self-test reported ${status}: ${failingChecks.map((c) => c.name ?? "check").join(", ")}. See TokenSaver output for details.`,
        );
    }

    return true;
}
