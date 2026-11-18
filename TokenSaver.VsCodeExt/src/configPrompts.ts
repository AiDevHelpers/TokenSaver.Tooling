import * as fs from "fs";
import * as path from "path";
import * as vscode from "vscode";
import {
    globalBehaviorConfigPath,
    gitignoreCoversTokenSaver,
    localBehaviorConfigPath,
    readMergedBehaviorConfig,
    writeBehaviorConfigValue,
} from "./behaviorConfigFile.js";

/**
 * On activation, if no config has answered whether `.tokensaver/` belongs in `.gitignore` and it is not already covered,
 * asks the user once and saves the answer (this workspace, or every workspace). Dismissing the prompt asks again next time.
 */
export async function maybePromptAddTokenSaverToGitignore(log: (message: string) => void): Promise<void> {
    const workspaceRoot = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
    if (!workspaceRoot) return;

    const merged = readMergedBehaviorConfig(workspaceRoot, log);
    if (merged.addTokenSaverToGitignore !== null && merged.addTokenSaverToGitignore !== undefined) return;
    if (gitignoreCoversTokenSaver(workspaceRoot)) return;

    const choice = await vscode.window.showInformationMessage(
        "TokenSaver writes its cache to `.tokensaver/`, which isn't in this workspace's .gitignore. Add it?",
        "Add (this workspace only)",
        "Add (remember for all workspaces)",
        "Don't add",
    );
    if (choice === undefined) return; // dismissed: ask again next activation

    const add = choice !== "Don't add";
    const scopeGlobal = choice === "Add (remember for all workspaces)";
    const targetPath = scopeGlobal ? globalBehaviorConfigPath() : localBehaviorConfigPath(workspaceRoot);

    try {
        writeBehaviorConfigValue(
            targetPath,
            (cfg) => ({ ...cfg, repoHelper: { ...cfg.repoHelper, addTokenSaverToGitignore: add } }),
            log,
        );
    } catch (err) {
        log(`Failed to persist gitignore preference to ${targetPath}: ${(err as Error).message}`);
    }

    if (add) {
        const gitignorePath = path.join(workspaceRoot, ".gitignore");
        try {
            const existing = fs.existsSync(gitignorePath) ? fs.readFileSync(gitignorePath, "utf8") : "";
            const needsNewline = existing.length > 0 && !existing.endsWith("\n");
            fs.writeFileSync(gitignorePath, existing + (needsNewline ? "\n" : "") + ".tokensaver/\n", "utf8");
            log("Added `.tokensaver/` to .gitignore.");
        } catch (err) {
            log(`Failed to update .gitignore: ${(err as Error).message}`);
        }
    }
}

/**
 * One-time move of the old "localHelper.*" settings to "tokenSaver.*". A value is copied only when the new key was never
 * set, so a later edit under "tokenSaver.*" always wins. The old values are left in place.
 */
export function migrateLegacySettings(): void {
    const oldCfg = vscode.workspace.getConfiguration("localHelper");
    const newCfg = vscode.workspace.getConfiguration("tokenSaver");
    const keys = [
        "autostart", "server.port", "server.startTimeout", "server.idleTimeout",
        "dotnet.command", "dotnet.projectPath", "notifications.enabled", "notifications.level",
        "verboseLogging", "simpleModelServer.exePath", "mcp.exePath", "simpleModelServer.healthUrl",
        "simpleModelServer.startTimeout", "autoCodeHelper.enabled", "autoInstallPrompt",
    ];
    for (const key of keys) {
        const oldInspect = oldCfg.inspect(key);
        const oldValue = oldInspect?.globalValue ?? oldInspect?.workspaceValue;
        if (oldValue === undefined) continue;

        const newInspect = newCfg.inspect(key);
        if (newInspect?.globalValue !== undefined || newInspect?.workspaceValue !== undefined) continue;

        const target =
            oldInspect?.workspaceValue !== undefined ? vscode.ConfigurationTarget.Workspace : vscode.ConfigurationTarget.Global;
        void newCfg.update(key, oldValue, target);
    }
}
