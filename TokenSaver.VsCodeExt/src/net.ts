import * as net from "node:net";

// TCP port probes used to decide whether a TokenSaver or SimpleModelServer process is already listening.

export async function isPortOpen(host: string, port: number): Promise<boolean> {
    return new Promise((resolve) => {
        const socket = new net.Socket();
        let done = false;
        const finish = (result: boolean) => {
            if (!done) {
                done = true;
                socket.destroy();
                resolve(result);
            }
        };
        socket.once("connect", () => finish(true));
        socket.once("error", () => finish(false));
        socket.setTimeout(1000, () => finish(false));
        socket.connect(port, host);
    });
}

export async function waitForPortOpen(host: string, port: number, timeoutSec: number) {
    const deadline = Date.now() + timeoutSec * 1000;
    while (Date.now() < deadline) {
        if (await isPortOpen(host, port)) return;
        await new Promise((r) => setTimeout(r, 500));
    }
    throw new Error(
        `Did not become reachable on ${host}:${port} within ${timeoutSec}s.`,
    );
}

export async function findOpenPortInRange(
    host: string,
    basePort: number,
    count: number,
): Promise<number | null> {
    for (let i = 0; i <= count; i++) {
        if (await isPortOpen(host, basePort + i)) return basePort + i;
    }
    return null;
}
