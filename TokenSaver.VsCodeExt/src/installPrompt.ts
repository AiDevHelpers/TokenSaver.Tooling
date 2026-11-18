import * as fs from "fs";
import * as path from "path";
import * as vscode from "vscode";

/**
 * Opens INSTALL_PROMPT.md: instructions an AI agent (or the user) can follow to wire the TokenSaver MCP server into any
 * supported client, over HTTP or stdio. Replaces the old auto-write of each client's config, which proved unreliable.
 */
export async function openInstallPrompt(context: vscode.ExtensionContext): Promise<void> {
    const promptPath = path.join(context.extensionPath, "INSTALL_PROMPT.md");
    if (!fs.existsSync(promptPath)) {
        vscode.window.showErrorMessage(`TokenSaver: INSTALL_PROMPT.md not found at ${promptPath}`);
        return;
    }
    const doc = await vscode.workspace.openTextDocument(promptPath);
    await vscode.window.showTextDocument(doc, { preview: false });
}

export interface McpProviderDeps {
    log(message: string): void;
    /** Starts SimpleModelServer, then the MCP server. */
    startServers(): Promise<void>;
    /** The port the MCP server answers on, or the configured one. */
    port(): number;
}

/** Registers TokenSaver as an MCP server definition with VS Code, when the host supports it. */
export function registerMcpProvider(context: vscode.ExtensionContext, deps: McpProviderDeps): void {
    try {
        if (typeof vscode.lm?.registerMcpServerDefinitionProvider === "function") {
            const provider: vscode.McpServerDefinitionProvider = {
                async provideMcpServerDefinitions() {
                    await deps.startServers();
                    return [
                        new vscode.McpHttpServerDefinition(
                            "TokenSaver",
                            vscode.Uri.parse(`http://localhost:${deps.port()}/mcp`),
                            {},
                            "1.0.0",
                        ),
                    ];
                },
                async resolveMcpServerDefinition(server) {
                    await deps.startServers();
                    return server;
                },
            };
            context.subscriptions.push(vscode.lm.registerMcpServerDefinitionProvider("TokenSaver", provider));
        }
    } catch (err) {
        deps.log(`Failed to register MCP provider: ${(err as Error).message}`);
    }
}
