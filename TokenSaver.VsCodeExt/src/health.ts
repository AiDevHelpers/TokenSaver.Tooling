import * as vscode from "vscode";

// HTTP health probes for SimpleModelServer (the /health contract accepts 200 ok and 503 degraded).

export async function fetchHealthOk(healthUrl: string): Promise<boolean> {
    const cfg = vscode.workspace.getConfiguration("tokenSaver");
    const timeoutMs = cfg.get<number>("simpleModelServer.startTimeout", 120) * 1000;
    try {
        const res = await fetch(healthUrl, {
            signal: AbortSignal.timeout(timeoutMs),
        });
        // 200 = healthy, 503 = degraded (models loading) — both mean the process is running.
        // Only a connection error (fetch throws) means it's truly dead.
        if (res.status !== 200 && res.status !== 503) return false;
        const body = (await res.json()) as { status?: string };
        return body.status === "ok" || body.status === "degraded";
    } catch {
        return false;
    }
}

export async function waitForHealthOk(healthUrl: string, timeoutSec: number): Promise<boolean> {
    const deadline = Date.now() + timeoutSec * 1000;
    while (Date.now() < deadline) {
        if (await fetchHealthOk(healthUrl)) return true;
        await new Promise((r) => setTimeout(r, 2000));
    }
    return false;
}
