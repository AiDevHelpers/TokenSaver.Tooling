import { promises as fs } from "node:fs";
import * as path from "node:path";

/**
 * Client side of the endpoint token (TokenSaver.MCP `EndpointAuthMiddleware`): where the token file lives,
 * how to send it, and the /api/auth/users calls behind the "Manage Users" picker.
 */

export interface AuthUsers {
    group: string;
    tokenPath: string;
    members: string[];
    candidates: string[];
}

/** Token file locations, most likely first: the service's ProgramData file, then the dev-run profile fallback. */
export function defaultTokenPaths(env: NodeJS.ProcessEnv): string[] {
    const paths: string[] = [];
    const programData = env.ProgramData ?? env.PROGRAMDATA;
    if (programData) {
        paths.push(path.join(programData, "TokenSaver", "auth-token"));
    }

    const localAppData = env.LOCALAPPDATA;
    if (localAppData) {
        paths.push(path.join(localAppData, "TokenSaver", "auth-token"));
    }

    return paths;
}

/**
 * Reads the endpoint token. A configured path wins; otherwise the default locations are tried in order.
 * Returns undefined when no readable file holds a token (not installed, or this user is not in the TokenSaver group).
 */
export async function readAuthToken(
    configuredPath: string | undefined,
    env: NodeJS.ProcessEnv = process.env,
): Promise<string | undefined> {
    const candidates = configuredPath && configuredPath.trim() !== ""
        ? [expandEnv(configuredPath.trim(), env)]
        : defaultTokenPaths(env);

    for (const candidate of candidates) {
        try {
            const token = (await fs.readFile(candidate, "utf8")).trim();
            if (token.length >= 32) {
                return token;
            }
        } catch {
            // Missing or unreadable: try the next location.
        }
    }

    return undefined;
}

/** Replaces %NAME% references with environment values; unknown names are left as they are. */
export function expandEnv(value: string, env: NodeJS.ProcessEnv): string {
    return value.replace(/%([^%]+)%/g, (whole, name: string) => env[name] ?? whole);
}

/** Headers for a request to a protected endpoint. Empty when there is no token. */
export function authHeaders(token: string | undefined): Record<string, string> {
    return token ? { Authorization: `Bearer ${token}` } : {};
}

/** A 401 still proves the server is up (the token may just be unreadable); only a refusal to connect means down. */
export function isServerAlive(status: number): boolean {
    return status === 200 || status === 405 || status === 401;
}

/** Account names the API accepts: DOMAIN\name or name, no path or shell characters. */
export function isValidAccount(account: string): boolean {
    if (!/^([\w .\-$]{1,64}\\)?[\w .\-$]{1,64}$/.test(account)) {
        return false;
    }

    return account.split("\\").every((segment) => segment.replace(/[. ]/g, "").length > 0);
}

export interface UserPickItem {
    label: string;
    description?: string;
    account: string;
    action: "remove" | "add";
}

/** Rows for the picker: current members first (select to remove), then local accounts that could be added. */
export function buildUserPickItems(info: AuthUsers): UserPickItem[] {
    const members = new Set(info.members.map((m) => m.toLowerCase()));
    const items: UserPickItem[] = info.members.map((account) => ({
        label: `$(check) ${account}`,
        description: "member — select to remove",
        account,
        action: "remove",
    }));

    for (const account of info.candidates) {
        if (!members.has(account.toLowerCase())) {
            items.push({ label: account, description: "select to add", account, action: "add" });
        }
    }

    return items;
}

async function call(
    port: number,
    token: string,
    method: "GET" | "POST" | "DELETE",
    pathAndQuery: string,
    body?: unknown,
): Promise<Response> {
    return fetch(`http://127.0.0.1:${port}${pathAndQuery}`, {
        method,
        headers: { ...authHeaders(token), ...(body === undefined ? {} : { "Content-Type": "application/json" }) },
        body: body === undefined ? undefined : JSON.stringify(body),
        signal: AbortSignal.timeout(10000),
    });
}

async function failure(res: Response): Promise<Error> {
    let detail = "";
    try {
        const json = (await res.json()) as { error?: string };
        detail = json.error ?? "";
    } catch {
        // Not JSON.
    }

    return new Error(
        res.status === 401
            ? "The server refused the token. Make sure you are in the TokenSaver group, or reinstall."
            : `Request failed (${res.status})${detail ? `: ${detail}` : ""}`,
    );
}

export async function listAuthUsers(port: number, token: string): Promise<AuthUsers> {
    const res = await call(port, token, "GET", "/api/auth/users");
    if (!res.ok) {
        throw await failure(res);
    }

    return (await res.json()) as AuthUsers;
}

export async function addAuthUser(port: number, token: string, account: string): Promise<void> {
    const res = await call(port, token, "POST", "/api/auth/users", { account });
    if (!res.ok) {
        throw await failure(res);
    }
}

export async function removeAuthUser(port: number, token: string, account: string): Promise<void> {
    const res = await call(port, token, "DELETE", `/api/auth/users?account=${encodeURIComponent(account)}`);
    if (!res.ok) {
        throw await failure(res);
    }
}
