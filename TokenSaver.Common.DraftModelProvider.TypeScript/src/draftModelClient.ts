import type {
  DraftModelChatChunk,
  DraftModelChatMessage,
  DraftModelClientOptions,
  DraftModelHealth,
  DraftModelHealthStatus,
  DraftModelInfo,
  IDraftModelClient,
} from "./types.js";

const KNOWN_HEALTH_STATUSES: readonly DraftModelHealthStatus[] = [
  "ok",
  "warming_up",
  "degraded",
  "unhealthy",
];

/**
 * Talks to DraftModel.Loader's OpenAI-compatible endpoints
 * (`/v1/models`, `/v1/chat/completions`, `/health`). See DraftModel repo's
 * docs/router-contract.md for the full HTTP contract.
 */
export class HttpDraftModelClient implements IDraftModelClient {
  private readonly baseUrl: string;
  private readonly fetchImpl: typeof fetch;

  constructor(options: DraftModelClientOptions) {
    this.baseUrl = options.baseUrl.replace(/\/+$/, "");
    this.fetchImpl = options.fetchImpl ?? fetch;
  }

  async checkHealth(signal?: AbortSignal): Promise<DraftModelHealth> {
    try {
      const response = await this.fetchImpl(`${this.baseUrl}/health`, { signal });
      const body = (await response.json().catch(() => null)) as { status?: string } | null;
      const status = body?.status;
      if (status && (KNOWN_HEALTH_STATUSES as readonly string[]).includes(status)) {
        return { status: status as DraftModelHealthStatus };
      }
      return { status: response.ok ? "ok" : "unhealthy" };
    } catch {
      return { status: "unreachable" };
    }
  }

  async listModels(signal?: AbortSignal): Promise<DraftModelInfo[]> {
    const response = await this.fetchImpl(`${this.baseUrl}/v1/models`, { signal });
    if (!response.ok) {
      throw new Error(`DraftModel /v1/models returned ${response.status}`);
    }
    const body = (await response.json()) as { data?: Array<{ id?: string }> };
    return (body.data ?? [])
      .filter((entry): entry is { id: string } => typeof entry.id === "string" && entry.id.length > 0)
      .map((entry) => ({ id: entry.id, name: entry.id }));
  }

  async *streamChat(
    messages: readonly DraftModelChatMessage[],
    signal?: AbortSignal
  ): AsyncIterable<DraftModelChatChunk> {
    const response = await this.fetchImpl(`${this.baseUrl}/v1/chat/completions`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ messages, stream: true }),
      signal,
    });

    if (!response.ok || !response.body) {
      throw new Error(`DraftModel /v1/chat/completions returned ${response.status}`);
    }

    yield* parseSseStream(response.body);
  }

  estimateTokenCount(text: string): number {
    // No real tokenizer is exposed by the router; ~4 chars/token is the
    // standard rough estimate used when nothing better is available.
    return Math.max(1, Math.ceil(text.length / 4));
  }
}

/** Parses `text/event-stream` frames of the form `data: {...}\n\n`, terminated by `data: [DONE]`. */
export async function* parseSseStream(
  body: ReadableStream<Uint8Array>
): AsyncIterable<DraftModelChatChunk> {
  const reader = body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";

  try {
    while (true) {
      const { value, done } = await reader.read();
      if (done) break;
      buffer += decoder.decode(value, { stream: true });

      let separatorIndex: number;
      while ((separatorIndex = buffer.indexOf("\n\n")) !== -1) {
        const frame = buffer.slice(0, separatorIndex);
        buffer = buffer.slice(separatorIndex + 2);
        const chunk = parseSseFrame(frame);
        if (chunk) {
          yield chunk;
          if (chunk.done) return;
        }
      }
    }
  } finally {
    reader.releaseLock();
  }
}

function parseSseFrame(frame: string): DraftModelChatChunk | null {
  const dataLine = frame
    .split("\n")
    .map((line) => line.trim())
    .find((line) => line.startsWith("data:"));
  if (!dataLine) return null;

  const payload = dataLine.slice("data:".length).trim();
  if (payload === "[DONE]") {
    return { contentDelta: "", done: true };
  }

  try {
    const parsed = JSON.parse(payload) as {
      choices?: Array<{ delta?: { content?: string }; finish_reason?: string | null }>;
    };
    const choice = parsed.choices?.[0];
    return {
      contentDelta: choice?.delta?.content ?? "",
      done: Boolean(choice?.finish_reason),
    };
  } catch {
    return null;
  }
}
