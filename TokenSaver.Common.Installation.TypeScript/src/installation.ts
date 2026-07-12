/**
 * Installation utilities for TokenSaver
 * Mirrors TokenSaver.Common.Installation (.NET library)
 * Workspace detection, service paths, version management
 */

import { platform } from "os";
import { join, dirname } from "path";
import { existsSync, readFileSync } from "fs";

/**
 * Detect workspace root by searching for .git or .slnx
 */
export function findWorkspaceRoot(startPath?: string): string | null {
  let current = startPath || process.cwd();

  while (current && current.length > 1) {
    if (isWorkspaceRoot(current)) {
      return current;
    }
    const parent = dirname(current);
    if (parent === current) break;
    current = parent;
  }

  return null;
}

/**
 * Check if a directory is a workspace root
 */
export function isWorkspaceRoot(dirPath: string): boolean {
  // Check for .git directory
  if (existsSync(join(dirPath, ".git"))) {
    return true;
  }

  // Check for .slnx file
  try {
    const entries = readDirSync(dirPath);
    return entries.some((f) => f.endsWith(".slnx"));
  } catch {
    return false;
  }
}

/**
 * Get the .tokensaver directory for a workspace
 */
export function getTokenSaverDirectory(workspaceRoot: string): string {
  return join(workspaceRoot, ".tokensaver");
}

/**
 * Get the config file path in the workspace
 */
export function getWorkspaceConfigPath(workspaceRoot: string): string {
  return join(getTokenSaverDirectory(workspaceRoot), "config.json");
}

/**
 * Get the index database path in the workspace
 */
export function getIndexDatabasePath(workspaceRoot: string): string {
  return join(getTokenSaverDirectory(workspaceRoot), "index.db");
}

/**
 * Get program files installation directory
 */
export function getProgramFilesPath(): string {
  if (platform() !== "win32") {
    throw new Error("Service installation is only supported on Windows");
  }
  return join(
    process.env.ProgramFiles || "C:\\Program Files",
    "TokenSaver"
  );
}

/**
 * Get the global configuration directory
 */
export function getGlobalConfigDirectory(): string {
  if (platform() !== "win32") {
    throw new Error("Service configuration is only supported on Windows");
  }
  return join(
    process.env.ProgramData || "C:\\ProgramData",
    "TokenSaver"
  );
}

/**
 * Get the global configuration file path
 */
export function getGlobalConfigPath(): string {
  return join(getGlobalConfigDirectory(), "config.json");
}

/**
 * Get the service executable path
 */
export function getServiceExecutablePath(): string {
  return join(getProgramFilesPath(), "TokenSaver.MCP.exe");
}

/**
 * Get the service name
 */
export function getServiceName(): string {
  return "TokenSaverMCP";
}

/**
 * Check if the service is installed
 */
export function isServiceInstalled(): boolean {
  if (platform() !== "win32") {
    return false;
  }
  return existsSync(getServiceExecutablePath());
}

/**
 * Get the user data directory
 */
export function getUserDataDirectory(): string {
  const localAppData =
    process.env.LOCALAPPDATA ||
    join(process.env.USERPROFILE || "", "AppData", "Local");
  return join(localAppData, "TokenSaver");
}

/**
 * Get the logs directory
 */
export function getLogsDirectory(): string {
  return join(getUserDataDirectory(), "Logs");
}

/**
 * Get the cached models directory
 */
export function getModelsDirectory(): string {
  return join(getUserDataDirectory(), "Models");
}

/**
 * Installation status result
 */
export interface InstallationStatus {
  isInstalled: boolean;
  serviceExists: boolean;
  configExists: boolean;
  installedVersion?: string;
  installPath?: string;
  configPath?: string;
}

/**
 * Check the installation status
 */
export function checkInstallationStatus(): InstallationStatus {
  if (platform() !== "win32") {
    return {
      isInstalled: false,
      serviceExists: false,
      configExists: false,
    };
  }

  const exePath = getServiceExecutablePath();
  const configPath = getGlobalConfigPath();

  const serviceExists = existsSync(exePath);
  const configExists = existsSync(configPath);

  return {
    isInstalled: serviceExists,
    serviceExists,
    configExists,
    installPath: getProgramFilesPath(),
    configPath,
  };
}

/**
 * Check if .tokensaver directory is in .gitignore
 */
export function isTokenSaverInGitignore(workspaceRoot: string): boolean {
  const gitignorePath = join(workspaceRoot, ".gitignore");
  if (!existsSync(gitignorePath)) {
    return false;
  }

  try {
    const content = readFileSync(gitignorePath, "utf-8");
    return content.includes(".tokensaver/") || content.includes(".tokensaver");
  } catch {
    return false;
  }
}

/**
 * Detailed status report
 */
export interface StatusReport {
  isInstalled: boolean;
  serviceExists: boolean;
  configExists: boolean;
  installedVersion?: string;
  installPath?: string;
  globalConfigPath?: string;
  workspaceRoot?: string;
  workspaceConfigPath?: string;
  tokenSaverDir?: string;
  isTokenSaverInGitignore: boolean;
}

/**
 * Get a detailed status report
 */
export function getDetailedStatus(workspaceRoot?: string): StatusReport {
  const installStatus = checkInstallationStatus();
  const wsRoot = workspaceRoot || findWorkspaceRoot();

  return {
    isInstalled: installStatus.isInstalled,
    serviceExists: installStatus.serviceExists,
    configExists: installStatus.configExists,
    installedVersion: installStatus.installedVersion,
    installPath: installStatus.installPath,
    globalConfigPath: installStatus.configPath,
    workspaceRoot: wsRoot || undefined,
    workspaceConfigPath:
      wsRoot ? getWorkspaceConfigPath(wsRoot) : undefined,
    tokenSaverDir: wsRoot ? getTokenSaverDirectory(wsRoot) : undefined,
    isTokenSaverInGitignore:
      wsRoot ? isTokenSaverInGitignore(wsRoot) : false,
  };
}

/**
 * Helper to read directory entries (polyfill for older Node versions)
 */
function readDirSync(dirPath: string): string[] {
  try {
    const fs = require("fs");
    return fs.readdirSync(dirPath);
  } catch {
    return [];
  }
}
