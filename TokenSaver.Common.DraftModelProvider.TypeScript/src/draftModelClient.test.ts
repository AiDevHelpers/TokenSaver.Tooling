import assert from "node:assert/strict";
import { test } from "node:test";
import { HttpDraftModelClient } from "./draftModelClient.js";
import type { DraftModelChatMessage } from "./types.js";

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function sseResponse(frames: readonly string[]): Response {
  const stream = new ReadableStream<Uint8Array>({
    start(controller) {
      const encoder = new TextEncoder();
      for (const frame of frames) {
        controller.enqueue(encoder.encode(frame));
      }
      controller.close();
    },
  });
  return new Response(stream, { status: 200, headers: { "Content-Type": "text/event-stream" } });
}

test("checkHealth returns the router's reported status", async () => {
  const client = new HttpDraftModelClient({
    baseUrl: "http://localhost:11435",
    fetchImpl: (async () => jsonResponse({ status: "warming_up" })) as typeof fetch,
  });

  const health = await client.checkHealth();
  assert.equal(health.status, "warming_up");
});

test("checkHealth returns unreachable when the request throws", async () => {
  const client = new HttpDraftModelClient({
    baseUrl: "http://localhost:11435",
    fetchImpl: (async () => {
      throw new Error("ECONNREFUSED");
    }) as typeof fetch,
  });

  const health = await client.checkHealth();
  assert.equal(health.status, "unreachable");
});

test("listModels maps the OpenAI-style /v1/models body", async () => {
  const client = new HttpDraftModelClient({
    baseUrl: "http://localhost:11435",
    fetchImpl: (async () =>
      jsonResponse({ object: "list", data: [{ id: "HelperModel", object: "model" }] })) as typeof fetch,
  });

  const models = await client.listModels();
  assert.deepEqual(models, [{ id: "HelperModel", name: "HelperModel" }]);
});

test("listModels throws on a non-OK response", async () => {
  const client = new HttpDraftModelClient({
    baseUrl: "http://localhost:11435",
    fetchImpl: (async () => jsonResponse({}, 503)) as typeof fetch,
  });

  await assert.rejects(() => client.listModels());
});

test("streamChat yields text deltas and stops at [DONE]", async () => {
  const client = new HttpDraftModelClient({
    baseUrl: "http://localhost:11435",
    fetchImpl: (async () =>
      sseResponse([
        `data: ${JSON.stringify({ choices: [{ delta: { content: "Hel" } }] })}\n\n`,
        `data: ${JSON.stringify({ choices: [{ delta: { content: "lo" } }] })}\n\n`,
        `data: ${JSON.stringify({ choices: [{ delta: {}, finish_reason: "stop" }] })}\n\n`,
        `data: [DONE]\n\n`,
      ])) as typeof fetch,
  });

  const messages: DraftModelChatMessage[] = [{ role: "user", content: "hi" }];
  const deltas: string[] = [];
  let sawDone = false;
  for await (const chunk of client.streamChat(messages)) {
    if (chunk.contentDelta) deltas.push(chunk.contentDelta);
    if (chunk.done) {
      sawDone = true;
      break;
    }
  }

  assert.equal(deltas.join(""), "Hello");
  assert.ok(sawDone);
});

test("streamChat throws when the router responds with an error status", async () => {
  const client = new HttpDraftModelClient({
    baseUrl: "http://localhost:11435",
    fetchImpl: (async () => jsonResponse({}, 500)) as typeof fetch,
  });

  await assert.rejects(async () => {
    for await (const _ of client.streamChat([{ role: "user", content: "hi" }])) {
      // no-op
    }
  });
});

test("estimateTokenCount is a stable rough estimate", () => {
  const client = new HttpDraftModelClient({ baseUrl: "http://localhost:11435" });
  assert.equal(client.estimateTokenCount(""), 1);
  assert.equal(client.estimateTokenCount("a".repeat(8)), 2);
});
