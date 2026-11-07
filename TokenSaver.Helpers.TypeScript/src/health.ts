/**
 * Health check utilities for TokenSaver servers
 */

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

/**
 * Checks if a port is open and accepting connections
 */
export async function isPortOpen(
  host: string,
  port: number,
  timeoutMs: number = 1000
): Promise<boolean> {
  return new Promise((resolve) => {
    const net = require("net");
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
    socket.setTimeout(timeoutMs, () => finish(false));

    try {
      socket.connect(port, host);
    } catch {
      finish(false);
    }
  });
}

/**
 * Waits for a port to become open, polling at 500ms intervals
 */
export async function waitForPortOpen(
  host: string,
  port: number,
  timeoutMs: number
): Promise<boolean> {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (await isPortOpen(host, port)) {
      return true;
    }
    await new Promise((r) => setTimeout(r, 500));
  }
  return false;
}

/**
 * Verifies the TokenSaver MCP server is responding at /mcp endpoint
 */
export async function verifyMcpServer(
  port: number,
  timeoutMs: number = 5000
): Promise<boolean> {
  try {
    const response = await fetch(`http://127.0.0.1:${port}/mcp`, {
      signal: AbortSignal.timeout(timeoutMs),
    });
    return response.ok || response.status === 405; // 405 = Method Not Allowed (expected for GET)
  } catch {
    return false;
  }
}

/**
 * Polls a health endpoint and checks if it returns 200 "ok" or 503 "degraded"
 */
export async function isSimpleModelServerHealthy(
  healthUrl: string,
  timeoutMs: number = 30000
): Promise<boolean> {
  try {
    const response = await fetch(healthUrl, {
      signal: AbortSignal.timeout(timeoutMs),
    });

    if (response.status !== 200 && response.status !== 503) {
      return false;
    }

    const body = (await response.json()) as { status?: string };
    return body.status === "ok" || body.status === "degraded";
  } catch {
    return false;
  }
}

/**
 * Gets the full health report from TokenSaver.MCP /health endpoint
 */
export async function getFullHealth(
  port: number,
  timeoutMs: number = 10000
): Promise<McpHealthReport | null> {
  try {
    const response = await fetch(`http://127.0.0.1:${port}/health`, {
      signal: AbortSignal.timeout(timeoutMs),
    });
    return (await response.json()) as McpHealthReport;
  } catch {
    return null;
  }
}

/**
 * Waits for a health endpoint to report ok/degraded, polling at 2s intervals
 */
export async function waitForHealthOk(
  healthUrl: string,
  timeoutMs: number
): Promise<boolean> {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (await isSimpleModelServerHealthy(healthUrl, 2000)) {
      return true;
    }
    await new Promise((r) => setTimeout(r, 2000));
  }
  return false;
}

/**
 * Finds the first open port in a range starting at basePort
 */
export async function findOpenPortInRange(
  host: string,
  basePort: number,
  count: number
): Promise<number | null> {
  for (let i = 0; i <= count; i++) {
    if (await isPortOpen(host, basePort + i)) {
      return basePort + i;
    }
  }
  return null;
}
