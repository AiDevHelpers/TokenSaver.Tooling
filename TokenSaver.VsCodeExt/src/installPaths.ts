import * as path from "path";

/** The Windows service name the TokenSaver.MCP MSI registers. */
export const TOKENSAVER_SERVICE_NAME = "TokenSaverMCP";

/** The Windows service name the SimpleModelServer MSI registers. */
export const SIMPLEMODEL_SERVER_SERVICE_NAME = "SimpleModelServer";

/** Bundled TokenSaver.MCP installer, copied into the extension by scripts/build-vsix.ps1. */
export function tokenSaverMsiPath(extensionPath: string): string {
    return path.join(extensionPath, "bin", "TokenSaver.Setup", "TokenSaver-Setup.msi");
}

/** Bundled SimpleModelServer installer, copied into the extension by scripts/build-vsix.ps1. */
export function simpleModelServerMsiPath(extensionPath: string): string {
    return path.join(extensionPath, "bin", "SimpleModelServer.Setup", "SimpleModelServer-Setup.msi");
}
