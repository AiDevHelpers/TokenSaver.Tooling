/**
 * Mirrors a Windows service's Application event log into an output channel. Used for the TokenSaver and SimpleModelServer
 * services, which log to the Event Log when run by the SCM. Polls every few seconds and prints each new record once.
 */

export interface EventLogEntry {
    TimeCreated?: string;
    RecordId?: number;
    LevelDisplayName?: string;
    Message?: string;
}

export interface LineSink {
    appendLine(line: string): void;
}

/** PowerShell emits one object for a single record and an array for several, so both are accepted. */
export function parseEventLogJson(stdout: string): EventLogEntry[] {
    const parsed = JSON.parse(stdout);
    if (Array.isArray(parsed)) return parsed;
    return parsed ? [parsed] : [];
}

/**
 * One record as output lines. Only the first line carries the timestamp and level; continuation lines (stack traces,
 * multi-line health details) are indented, so a multi-line message does not look like many events fired at once.
 */
export function formatEventLines(entry: EventLogEntry): string[] {
    const timestamp = entry.TimeCreated ?? "";
    const level = entry.LevelDisplayName ?? "INFO";
    const message = entry.Message ?? "";
    const lines: string[] = [];
    let first = true;
    for (const line of message.split(/\r?\n/)) {
        if (!line) continue;
        lines.push(first ? `${timestamp} ${level} ${line}` : `    ${line}`);
        first = false;
    }

    return lines;
}

export interface ServiceEventLogOptions {
    /** Event Log provider name, e.g. "TokenSaver". */
    source: string;
    /** Polling only happens while the service is managed by the SCM. */
    isActive: () => boolean;
    /** Resolved on each write, so the channel can be created after this object. */
    channel: () => LineSink | undefined;
    /** Receives a failed poll's message. */
    onError: (message: string) => void;
    intervalMs?: number;
}

export class ServiceEventLogPoller {
    private timer: ReturnType<typeof setInterval> | null = null;
    private lastRecordId = 0;

    constructor(private readonly options: ServiceEventLogOptions) {}

    /** Starts polling if the service is active. Replaces any poller already running. */
    start(): void {
        this.stop();
        if (!this.options.isActive()) return;

        this.timer = setInterval(() => {
            this.poll().catch((err: Error) => this.options.onError(err.message));
        }, this.options.intervalMs ?? 5000);
        this.poll().catch((err: Error) => this.options.onError(err.message));
    }

    stop(): void {
        if (this.timer) {
            clearInterval(this.timer);
            this.timer = null;
        }
    }

    /** One poll: reads the newest records, writes those not seen before, in RecordId order. */
    async poll(): Promise<void> {
        if (!this.options.isActive()) return;
        const { execFile } = await import("child_process");

        // TimeCreated is formatted to a string before ConvertTo-Json: otherwise PowerShell serializes [DateTime] as the
        // legacy "/Date(ms)/" wire format and the output shows that instead of a readable timestamp.
        const psCommand =
            `Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='${this.options.source}'} -MaxEvents 50 ` +
            `| Select-Object @{n='TimeCreated';e={$_.TimeCreated.ToString('yyyy-MM-dd HH:mm:ss')}},RecordId,LevelDisplayName,Message ` +
            `| ConvertTo-Json -Compress`;

        return new Promise<void>((resolve, reject) => {
            execFile(
                "powershell.exe",
                ["-NoProfile", "-NonInteractive", "-Command", psCommand],
                { windowsHide: true, maxBuffer: 1024 * 1024 },
                (err, stdout, stderr) => {
                    if (err) {
                        const errMessage = stderr?.toString() || err.message;
                        if (/NoMatchingEventsFound|No matching events found/i.test(errMessage)) {
                            resolve();
                            return;
                        }

                        reject(new Error(errMessage));
                        return;
                    }

                    if (!stdout) {
                        resolve();
                        return;
                    }

                    let entries: EventLogEntry[];
                    try {
                        entries = parseEventLogJson(stdout);
                    } catch (parseErr) {
                        reject(parseErr);
                        return;
                    }

                    entries
                        .filter((entry) => typeof entry.RecordId === "number")
                        .sort((a, b) => a.RecordId! - b.RecordId!)
                        .forEach((entry) => {
                            if (entry.RecordId! <= this.lastRecordId) return;
                            this.lastRecordId = entry.RecordId!;
                            const channel = this.options.channel();
                            for (const line of formatEventLines(entry)) {
                                channel?.appendLine(line);
                            }
                        });

                    resolve();
                },
            );
        });
    }
}
