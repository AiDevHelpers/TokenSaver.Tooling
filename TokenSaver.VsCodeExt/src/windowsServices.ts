
// Windows service and installer helpers: sc.exe for start/stop/query, msiexec (via PowerShell -Verb RunAs) for installs.

export async function isServiceInstalled(serviceName: string): Promise<boolean> {
    try {
        const { execFile } = await import("child_process");
        const out = await new Promise<string>((resolve, reject) => {
            execFile(
                "sc.exe",
                ["query", serviceName],
                (err, stdout) => (err && !(stdout ?? "").includes("STATE") ? reject(err) : resolve(stdout ?? "")),
            );
        });
        return out.includes("STATE");
    } catch {
        return false;
    }
}

export async function runSc(args: string[]): Promise<number> {
    const { execFile } = await import("child_process");
    return new Promise((resolve) => {
        execFile("sc.exe", args, (err) => resolve(err ? (typeof err.code === "number" ? err.code : 1) : 0));
    });
}

export async function runMsiexec(args: string[]): Promise<number> {
    const { execFile } = await import("child_process");
    // Quote each arg for safe interpolation inside the PowerShell -ArgumentList array literal.
    const quotedArgs = args.map((a) => `'${a.replace(/'/g, "''")}'`).join(",");
    const psCommand =
        `$proc = Start-Process -FilePath 'msiexec.exe' -ArgumentList @(${quotedArgs}) ` +
        `-Verb RunAs -Wait -PassThru; exit $proc.ExitCode`;
    return new Promise((resolve) => {
        execFile(
            "powershell.exe",
            ["-NoProfile", "-NonInteractive", "-Command", psCommand],
            (err) => resolve(err ? (typeof err.code === "number" ? err.code : 1) : 0),
        );
    });
}
