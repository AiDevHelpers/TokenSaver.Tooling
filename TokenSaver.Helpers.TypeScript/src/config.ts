/**
 * Configuration management for TokenSaver
 */

import * as fs from "fs";
import * as path from "path";

export interface RepoHelperConfig {
  addTokenSaverToGitignore?: boolean | null;
  optimizeInstructions?: boolean | null;
  autoSplitOversizedInstructions?: boolean | null;
  combineClaudeMdForClaudeCode?: boolean | null;
}

export interface TokenSaverBehaviorConfig {
  repoHelper?: RepoHelperConfig | null;
  [key: string]: unknown;
}

/**
 * Gets the path to the global behavior config file
 */
export function getGlobalConfigPath(): string {
  const programData = process.env["ProgramData"] || "C:\\ProgramData";
  return path.join(programData, "TokenSaver", "config.json");
}

/**
 * Gets the path to the local (workspace) behavior config file
 */
export function getLocalConfigPath(workspaceRoot: string): string {
  return path.join(workspaceRoot, ".tokensaver", "config.json");
}

/**
 * Reads a behavior config file
 */
export function readConfigFile(
  filePath: string
): TokenSaverBehaviorConfig | null {
  try {
    if (!fs.existsSync(filePath)) {
      return null;
    }

    const text = fs.readFileSync(filePath, "utf8");
    if (!text.trim()) {
      return null;
    }

    return JSON.parse(text) as TokenSaverBehaviorConfig;
  } catch {
    return null;
  }
}

/**
 * Writes a behavior config file
 */
export function writeConfigFile(
  filePath: string,
  config: TokenSaverBehaviorConfig
): void {
  const dir = path.dirname(filePath);
  if (dir) {
    fs.mkdirSync(dir, { recursive: true });
  }

  const json = JSON.stringify(config, null, 2);
  fs.writeFileSync(filePath, json + "\n", "utf8");
}

/**
 * Gets merged config (local wins over global)
 */
export function getMergedRepoHelperConfig(
  workspaceRoot: string
): RepoHelperConfig {
  const local = readConfigFile(getLocalConfigPath(workspaceRoot));
  const global = readConfigFile(getGlobalConfigPath());

  return {
    addTokenSaverToGitignore:
      local?.repoHelper?.addTokenSaverToGitignore ??
      global?.repoHelper?.addTokenSaverToGitignore ??
      null,
    optimizeInstructions:
      local?.repoHelper?.optimizeInstructions ??
      global?.repoHelper?.optimizeInstructions ??
      null,
    autoSplitOversizedInstructions:
      local?.repoHelper?.autoSplitOversizedInstructions ??
      global?.repoHelper?.autoSplitOversizedInstructions ??
      null,
    combineClaudeMdForClaudeCode:
      local?.repoHelper?.combineClaudeMdForClaudeCode ??
      global?.repoHelper?.combineClaudeMdForClaudeCode ??
      null,
  };
}

/**
 * Updates a config value in the specified scope
 */
export function updateConfigValue(
  workspaceRoot: string,
  scope: "local" | "global",
  mutate: (cfg: TokenSaverBehaviorConfig) => TokenSaverBehaviorConfig
): void {
  const filePath =
    scope === "local"
      ? getLocalConfigPath(workspaceRoot)
      : getGlobalConfigPath();

  const current = readConfigFile(filePath) ?? {};
  const updated = mutate(current);
  writeConfigFile(filePath, updated);
}
