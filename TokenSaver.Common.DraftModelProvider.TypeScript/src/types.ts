/**
 * Framework-agnostic contract for talking to DraftModel.Loader's router
 * (see docs/router-contract.md in the DraftModel repo). No dependency on
 * `vscode` here so it can be unit tested outside the extension host.
 */

export type DraftModelChatRole = "system" | "user" | "assistant";

export interface DraftModelChatMessage {
  readonly role: DraftModelChatRole;
  readonly content: string;
}

/** One virtual model exposed by the router (id/name mirror `routerModelName`, e.g. "HelperModel"). */
export interface DraftModelInfo {
  readonly id: string;
  readonly name: string;
}

/** One streamed delta from a chat completion. */
export interface DraftModelChatChunk {
  readonly contentDelta: string;
  readonly done: boolean;
}

export type DraftModelHealthStatus =
  | "ok"
  | "warming_up"
  | "degraded"
  | "unhealthy"
  | "unreachable";

export interface DraftModelHealth {
  readonly status: DraftModelHealthStatus;
}

export interface DraftModelClientOptions {
  /** e.g. "http://localhost:11435" — no trailing slash. */
  readonly baseUrl: string;
  readonly fetchImpl?: typeof fetch;
}

/**
 * Public surface used by any Copilot-style provider adapter (VS Code's
 * LanguageModelChatProvider, or any future equivalent) to talk to DraftModel.
 */
export interface IDraftModelClient {
  checkHealth(signal?: AbortSignal): Promise<DraftModelHealth>;
  listModels(signal?: AbortSignal): Promise<DraftModelInfo[]>;
  streamChat(
    messages: readonly DraftModelChatMessage[],
    signal?: AbortSignal
  ): AsyncIterable<DraftModelChatChunk>;
  estimateTokenCount(text: string): number;
}
