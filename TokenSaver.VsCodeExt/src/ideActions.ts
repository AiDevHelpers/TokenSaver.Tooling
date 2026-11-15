import * as path from "path";
import * as vscode from "vscode";

// Common IDE actions for VS Code. The names match the Visual Studio bridge (TokenSaver.Vs.Extension
// Bridge/IdeActions.cs), so the MCP caller uses the same calls for either IDE. Actions VS Code's API cannot
// perform throw an "not supported" error instead of guessing, so the caller always knows what happened.

type Args = Record<string, unknown>;

export class UnsupportedActionError extends Error {
    constructor(action: string) {
        super(`Action '${action}' is not supported in VS Code (no VS Code API for it).`);
    }
}

function requireString(args: Args, key: string): string {
    const value = args[key];
    if (typeof value !== "string" || value.length === 0) {
        throw new Error(`Missing required argument '${key}'.`);
    }
    return value;
}

function optionalInt(args: Args, key: string): number | undefined {
    const value = args[key];
    return typeof value === "number" ? value : undefined;
}

function resolveFile(file: string): vscode.Uri {
    if (path.isAbsolute(file)) {
        return vscode.Uri.file(file);
    }
    const root = vscode.workspace.workspaceFolders?.[0]?.uri;
    if (!root) {
        throw new Error(`Relative path '${file}' needs an open workspace folder.`);
    }
    return vscode.Uri.joinPath(root, file);
}

function launchConfigurationNames(): string[] {
    const configs = vscode.workspace.getConfiguration("launch").get<Array<{ name?: string }>>("configurations") ?? [];
    return configs.map((c) => c.name ?? "").filter((n) => n.length > 0);
}

export async function runIdeAction(action: string, args: Args): Promise<unknown> {
    switch (action) {
        case "status":
            return {
                workspace_folders: vscode.workspace.workspaceFolders?.map((f) => f.uri.fsPath) ?? [],
                active_file: vscode.window.activeTextEditor?.document.uri.fsPath ?? null,
                debug_session: vscode.debug.activeDebugSession?.name ?? null,
            };

        case "startDebugging": {
            const folder = vscode.workspace.workspaceFolders?.[0];
            const name = typeof args.configuration === "string" ? args.configuration : launchConfigurationNames()[0];
            if (!name) {
                throw new Error("No launch configuration found in launch.json.");
            }
            const started = await vscode.debug.startDebugging(folder, name);
            return { started, configuration: name };
        }

        case "stopDebugging":
            await vscode.commands.executeCommand("workbench.action.debug.stop");
            return { stopped: true };

        case "runTests":
            await vscode.commands.executeCommand("testing.runAll");
            return { command: "testing.runAll" };

        case "formatDocument":
            if (!vscode.window.activeTextEditor) {
                throw new Error("No active editor to format.");
            }
            await vscode.commands.executeCommand("editor.action.formatDocument");
            return { formatted: true };

        case "build":
            await vscode.commands.executeCommand("workbench.action.tasks.build");
            return { command: "workbench.action.tasks.build" };

        case "getBuildDiagnostics": {
            const severity = typeof args.severity === "string" ? args.severity : "all";
            const wanted = severity === "error" ? [vscode.DiagnosticSeverity.Error]
                : severity === "warning" ? [vscode.DiagnosticSeverity.Warning]
                : [vscode.DiagnosticSeverity.Error, vscode.DiagnosticSeverity.Warning];
            const items: Array<{ file: string; line: number; severity: string; message: string }> = [];
            for (const [uri, diagnostics] of vscode.languages.getDiagnostics()) {
                for (const d of diagnostics) {
                    if (!wanted.includes(d.severity)) continue;
                    items.push({
                        file: uri.fsPath,
                        line: d.range.start.line + 1,
                        severity: vscode.DiagnosticSeverity[d.severity],
                        message: d.message,
                    });
                }
            }
            return { count: items.length, items };
        }

        case "codeCleanup":
            await vscode.commands.executeCommand("editor.action.sourceAction", { kind: "source.fixAll", apply: "first" });
            return { command: "editor.action.sourceAction", kind: "source.fixAll" };

        case "setBreakpoint": {
            const uri = resolveFile(requireString(args, "file"));
            const line = optionalInt(args, "line");
            if (line === undefined) throw new Error("Missing required argument 'line'.");
            vscode.debug.addBreakpoints([new vscode.SourceBreakpoint(new vscode.Location(uri, new vscode.Position(line - 1, 0)))]);
            return { file: uri.fsPath, line };
        }

        case "removeBreakpoint": {
            const uri = resolveFile(requireString(args, "file"));
            const line = optionalInt(args, "line");
            const matches = vscode.debug.breakpoints.filter(
                (b): b is vscode.SourceBreakpoint =>
                    b instanceof vscode.SourceBreakpoint &&
                    b.location.uri.fsPath === uri.fsPath &&
                    (line === undefined || b.location.range.start.line + 1 === line),
            );
            vscode.debug.removeBreakpoints(matches);
            return { removed: matches.length };
        }

        case "listBreakpoints":
            return {
                breakpoints: vscode.debug.breakpoints
                    .filter((b): b is vscode.SourceBreakpoint => b instanceof vscode.SourceBreakpoint)
                    .map((b) => ({
                        file: b.location.uri.fsPath,
                        line: b.location.range.start.line + 1,
                        enabled: b.enabled,
                        condition: b.condition ?? null,
                    })),
            };

        case "step": {
            const kind = requireString(args, "kind");
            const commands: Record<string, string> = {
                over: "workbench.action.debug.stepOver",
                into: "workbench.action.debug.stepInto",
                out: "workbench.action.debug.stepOut",
                continue: "workbench.action.debug.continue",
            };
            const command = commands[kind];
            if (!command) throw new Error(`Unknown step kind '${kind}'. Use over, into, out, or continue.`);
            await vscode.commands.executeCommand(command);
            return { step: kind };
        }

        case "debugState": {
            const session = vscode.debug.activeDebugSession;
            const frame = vscode.debug.activeStackItem;
            return {
                mode: session ? "debugging" : "none",
                session: session?.name ?? null,
                thread_id: frame && "threadId" in frame ? frame.threadId : null,
            };
        }

        case "evaluate": {
            const session = vscode.debug.activeDebugSession;
            if (!session) throw new Error("No active debug session.");
            const expression = requireString(args, "expression");
            const frame = vscode.debug.activeStackItem;
            const frameId = frame && "frameId" in frame ? frame.frameId : undefined;
            const reply = await session.customRequest("evaluate", { expression, frameId, context: "repl" });
            return { expression, result: reply.result, type: reply.type ?? null };
        }

        case "getLocals": {
            const session = vscode.debug.activeDebugSession;
            const frame = vscode.debug.activeStackItem;
            if (!session || !frame || !("frameId" in frame)) {
                throw new Error("Not stopped at a breakpoint; locals need a paused debug session.");
            }
            const scopes = (await session.customRequest("scopes", { frameId: frame.frameId })).scopes as Array<{ name: string; variablesReference: number }>;
            const locals: Array<{ scope: string; name: string; value: string; type: string | null }> = [];
            for (const scope of scopes) {
                const vars = (await session.customRequest("variables", { variablesReference: scope.variablesReference })).variables as Array<{ name: string; value: string; type?: string }>;
                for (const v of vars) {
                    locals.push({ scope: scope.name, name: v.name, value: v.value, type: v.type ?? null });
                }
            }
            return { count: locals.length, locals };
        }

        case "listConfigurations":
            return { configurations: launchConfigurationNames() };

        case "health":
            return { ide: "VsCode" };

        case "setStartupProject":
        case "setConfiguration":
        case "clean":
        case "publish":
        case "getOutput":
        case "searchOutput":
        case "clearOutput":
        case "addWatch":
        case "removeWatch":
        case "listWatches":
        case "prepareKill":
            throw new UnsupportedActionError(action);

        default:
            throw new Error(`Unknown action '${action}'.`);
    }
}
