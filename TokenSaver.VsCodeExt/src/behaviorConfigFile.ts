import * as fs from "fs";
import * as path from "path";

/**
 * The extension's view of .tokensaver/config.json (workspace) and %ProgramData%\TokenSaver\config.json (global).
 * Mirrors TokenSaver.Config's merge rule: the local file wins over the global file for each property.
 */

export interface RepoHelperBehaviorConfig {
    addTokenSaverToGitignore?: boolean | null;
    optimizeInstructions?: boolean | null;
}

export interface TokenSaverBehaviorConfig {
    repoHelper?: RepoHelperBehaviorConfig | null;
}

export function globalBehaviorConfigPath(programData: string | undefined = process.env["ProgramData"]): string {
    return path.join(programData ?? "C:\\ProgramData", "TokenSaver", "config.json");
}

export function localBehaviorConfigPath(workspaceRoot: string): string {
    return path.join(workspaceRoot, ".tokensaver", "config.json");
}

/** Reads a config file. A missing or empty file is null; an unreadable one is reported through onError and is null. */
export function readBehaviorConfigFile(
    filePath: string,
    onError: (message: string) => void,
): TokenSaverBehaviorConfig | null {
    try {
        if (!fs.existsSync(filePath)) return null;
        const text = fs.readFileSync(filePath, "utf8");
        if (!text.trim()) return null;
        return JSON.parse(text) as TokenSaverBehaviorConfig;
    } catch (err) {
        onError(`Failed to read behavior config ${filePath}: ${(err as Error).message}`);
        return null;
    }
}

export function readMergedBehaviorConfig(
    workspaceRoot: string,
    onError: (message: string) => void,
    programData?: string,
): RepoHelperBehaviorConfig {
    const local = readBehaviorConfigFile(localBehaviorConfigPath(workspaceRoot), onError);
    const global = readBehaviorConfigFile(globalBehaviorConfigPath(programData), onError);
    return {
        addTokenSaverToGitignore:
            local?.repoHelper?.addTokenSaverToGitignore ?? global?.repoHelper?.addTokenSaverToGitignore ?? null,
    };
}

/** Applies a change to a config file, creating the file and its folder when needed. */
export function writeBehaviorConfigValue(
    filePath: string,
    mutate: (cfg: TokenSaverBehaviorConfig) => TokenSaverBehaviorConfig,
    onError: (message: string) => void,
): void {
    const current = readBehaviorConfigFile(filePath, onError) ?? {};
    const updated = mutate(current);
    fs.mkdirSync(path.dirname(filePath), { recursive: true });
    fs.writeFileSync(filePath, JSON.stringify(updated, null, 2) + "\n", "utf8");
}

/** Mirrors GitignoreFilter.EntryIsCovered (TokenSaver.Library): is `.tokensaver` already in .gitignore? */
export function gitignoreCoversTokenSaver(workspaceRoot: string): boolean {
    const gitignorePath = path.join(workspaceRoot, ".gitignore");
    if (!fs.existsSync(gitignorePath)) return false;

    const normalised = ".tokensaver";
    let text: string;
    try {
        text = fs.readFileSync(gitignorePath, "utf8");
    } catch {
        return false;
    }

    for (const raw of text.split(/\r?\n/)) {
        const line = raw.trim();
        if (!line || line.startsWith("#") || line.startsWith("!")) continue;
        const stripped = line.replace(/\/+$/, "").replace(/^\/+/, "");
        if (!stripped) continue;
        if (
            normalised.toLowerCase() === stripped.toLowerCase() ||
            normalised.toLowerCase().startsWith(stripped.toLowerCase() + "/")
        ) {
            return true;
        }
    }
    return false;
}
