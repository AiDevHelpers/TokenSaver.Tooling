/**
 * Turns the TokenSaver.MCP process's stdout into output-channel lines. The server writes one JSON object per log event
 * (lines starting with "{") and plain text for everything else (startup banner, unhandled console output).
 */

export interface StructuredLogLine {
    ts?: string;
    lvl?: string;
    ch?: string;
    src?: string;
    msg?: string;
    ex?: string;
}

/** Output lines for one stdout line: a structured event becomes one line plus its exception, plain text stays as is. */
export function formatLogLine(
    line: string,
    formatTime: (ts: string) => string = (ts) => new Date(ts).toLocaleTimeString(),
): string[] {
    if (!line.startsWith("{")) {
        return [line];
    }

    let entry: StructuredLogLine;
    try {
        entry = JSON.parse(line) as StructuredLogLine;
    } catch {
        return [line];
    }

    const ts = entry.ts ? formatTime(entry.ts) : "";
    const lvl = entry.lvl ?? "INF";
    const src = entry.src ? ` ${entry.src}:` : "";
    const msg = entry.msg ?? line;
    const out = [`${ts} ${lvl}${src} ${msg}`];
    if (entry.ex) {
        out.push(entry.ex);
    }

    return out;
}

/**
 * Adds a chunk to the unfinished line left over from the last chunk. Returns the complete lines and the new unfinished
 * remainder (empty when the chunk ended on a line break).
 */
export function splitChunk(pending: string, chunk: string): { lines: string[]; rest: string } {
    const lines = (pending + chunk).split(/\r?\n/);
    const rest = lines.pop() ?? "";
    return { lines, rest };
}

/** The port from the host's "Now listening on: ...:port" line, or null for any other line. */
export function listeningPort(line: string): number | null {
    const match = line.match(/Now listening on:.*?:(\d+)/);
    return match ? parseInt(match[1], 10) : null;
}
