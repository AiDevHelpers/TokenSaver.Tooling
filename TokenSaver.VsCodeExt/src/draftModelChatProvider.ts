import * as vscode from "vscode";
import {
    HttpDraftModelClient,
    type DraftModelChatMessage,
    type IDraftModelClient,
} from "@tokensaver/draftmodel-provider";

// Registers DraftModel (the local Ollama-compatible router served by DraftModel.Loader) as a
// selectable model in Copilot Chat's model picker, via VS Code's `languageModelChatProviders`
// contribution point. Once selected, it backs every Copilot surface that just sends chat
// requests against the active model — Chat, inline Explain/Fix/Refactor/Review, and
// generate-docs/generate-tests — so no per-feature wiring is needed here.
//
// All DraftModel HTTP/parsing logic lives in @tokensaver/draftmodel-provider (framework-agnostic,
// unit tested); this file is only the thin vscode.LanguageModelChatProvider adapter.

const DRAFTMODEL_VENDOR = "draftmodel";

function draftModelBaseUrl(): string {
    const cfg = vscode.workspace.getConfiguration("tokenSaver");
    const healthUrl = cfg.get<string>(
        "draftModel.healthUrl",
        "http://localhost:11435/health",
    );
    return healthUrl.replace(/\/health\/?$/, "");
}

function createClient(): IDraftModelClient {
    return new HttpDraftModelClient({ baseUrl: draftModelBaseUrl() });
}

function partToText(part: unknown): string {
    return part instanceof vscode.LanguageModelTextPart ? part.value : "";
}

function toDraftModelMessages(
    messages: readonly vscode.LanguageModelChatRequestMessage[],
): DraftModelChatMessage[] {
    return messages.map((message) => ({
        role:
            message.role === vscode.LanguageModelChatMessageRole.Assistant
                ? "assistant"
                : "user",
        content: message.content.map(partToText).join(""),
    }));
}

class DraftModelChatProvider implements vscode.LanguageModelChatProvider {
    constructor(private readonly logError: (message: string) => void) {}

    async provideLanguageModelChatInformation(
        _options: vscode.PrepareLanguageModelChatModelOptions,
        _token: vscode.CancellationToken,
    ): Promise<vscode.LanguageModelChatInformation[]> {
        const client = createClient();
        try {
            const health = await client.checkHealth();
            if (health.status === "unreachable") {
                return [];
            }
            const models = await client.listModels();
            return models.map((model) => ({
                id: model.id,
                name: `DraftModel: ${model.name}`,
                family: "draftmodel",
                version: "1.0.0",
                maxInputTokens: 32_000,
                maxOutputTokens: 4_096,
                tooltip:
                    "Local DraftModel router (Ollama-compatible), served by DraftModel.Loader.",
                capabilities: { toolCalling: false, imageInput: false },
            }));
        } catch (err) {
            this.logError(
                `DraftModel chat provider: failed to list models: ${(err as Error).message}`,
            );
            return [];
        }
    }

    async provideLanguageModelChatResponse(
        _model: vscode.LanguageModelChatInformation,
        messages: readonly vscode.LanguageModelChatRequestMessage[],
        _options: vscode.ProvideLanguageModelChatResponseOptions,
        progress: vscode.Progress<vscode.LanguageModelResponsePart>,
        token: vscode.CancellationToken,
    ): Promise<void> {
        const client = createClient();
        const controller = new AbortController();
        const cancelListener = token.onCancellationRequested(() =>
            controller.abort(),
        );
        try {
            for await (const chunk of client.streamChat(
                toDraftModelMessages(messages),
                controller.signal,
            )) {
                if (chunk.contentDelta) {
                    progress.report(
                        new vscode.LanguageModelTextPart(chunk.contentDelta),
                    );
                }
            }
        } finally {
            cancelListener.dispose();
        }
    }

    async provideTokenCount(
        _model: vscode.LanguageModelChatInformation,
        text: string | vscode.LanguageModelChatRequestMessage,
        _token: vscode.CancellationToken,
    ): Promise<number> {
        const content =
            typeof text === "string" ? text : text.content.map(partToText).join("");
        return createClient().estimateTokenCount(content);
    }
}

export function registerDraftModelChatProvider(
    context: vscode.ExtensionContext,
    logError: (message: string) => void,
) {
    try {
        if (typeof vscode.lm?.registerLanguageModelChatProvider === "function") {
            context.subscriptions.push(
                vscode.lm.registerLanguageModelChatProvider(
                    DRAFTMODEL_VENDOR,
                    new DraftModelChatProvider(logError),
                ),
            );
        }
    } catch (err) {
        logError(
            `Failed to register DraftModel chat provider: ${(err as Error).message}`,
        );
    }
}
