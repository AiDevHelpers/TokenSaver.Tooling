/** The states the status-bar item shows. */
export type ServerStatusState = "running" | "stopped" | "starting" | "restarting" | "error";

/** The part of a status-bar item this module writes. */
export interface StatusBarLike {
    text: string;
    tooltip: string | { value: string } | undefined;
    show(): void;
}

/** Sets the status-bar text and tooltip for a state, then shows the item. */
export function applyStatusBar(bar: StatusBarLike, state: ServerStatusState, autoCodeHelperOn: boolean): void {
    switch (state) {
        case "running":
            bar.text = autoCodeHelperOn ? "$(server) TokenSaver: Running $(rocket)" : "$(server) TokenSaver: Running";
            bar.tooltip = autoCodeHelperOn
                ? "TokenSaver running with CodeHelper auto-workflow active. Click for status."
                : "TokenSaver MCP server is running. Click for status.";
            break;
        case "stopped":
            bar.text = "$(server) TokenSaver: Stopped";
            bar.tooltip = "TokenSaver MCP server is stopped. Click to start.";
            break;
        case "starting":
            bar.text = "$(sync~spin) TokenSaver: Starting…";
            bar.tooltip = "TokenSaver MCP server is starting.";
            break;
        case "restarting":
            bar.text = "$(sync~spin) TokenSaver: Restarting…";
            bar.tooltip = "TokenSaver MCP server is restarting.";
            break;
        case "error":
            bar.text = "$(issue-opened) TokenSaver: Error";
            bar.tooltip = "TokenSaver MCP server encountered an error. Click for logs.";
            break;
    }
    bar.show();
}

/** What the status report prints about the two servers. */
export interface StatusReportInput {
    configuredPort: number;
    actualPort: number | null;
    mcpWindowsService: boolean;
    mcpChildRunning: boolean;
    simpleModelServerWindowsService: boolean;
    simpleModelServerChildRunning: boolean;
    simpleModelServerHealthUrl: string;
}

export function statusReportLines(input: StatusReportInput): string[] {
    return [
        "=== TokenSaver Status ===",
        `MCP server port (configured): ${input.configuredPort}`,
        `MCP server actual port: ${input.actualPort ?? "(not running)"}`,
        input.mcpWindowsService
            ? "MCP server: running as Windows service (TokenSaverMCP)"
            : `MCP server process running: ${input.mcpChildRunning}`,
        input.simpleModelServerWindowsService
            ? "SimpleModelServer: running as Windows service (SimpleModelServer)"
            : `SimpleModelServer process running: ${input.simpleModelServerChildRunning}`,
        `SimpleModelServer health URL: ${input.simpleModelServerHealthUrl}`,
        "==========================",
    ];
}

/** Whether a notification should be shown, given the user's notification settings. */
export function shouldNotify(type: "info" | "error", enabled: boolean, level: string): boolean {
    if (!enabled) return false;
    if (level === "errors" && type === "info") return false;
    return true;
}

/** Formats a timestamp the way the service's own log lines do: yyyy-MM-dd HH:mm:ss. */
export function timestampPrefix(now: Date = new Date()): string {
    const pad = (n: number, len = 2) => String(n).padStart(len, "0");
    return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())} ${pad(now.getHours())}:${pad(now.getMinutes())}:${pad(now.getSeconds())}`;
}
