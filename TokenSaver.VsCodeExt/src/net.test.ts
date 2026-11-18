import * as net from "node:net";
import { afterEach, beforeEach, describe, expect, test } from "vitest";
import { findOpenPortInRange, isPortOpen, waitForPortOpen } from "./net";

// Ports are taken from the ephemeral range so tests do not collide with a running TokenSaver.
async function listenEphemeral(): Promise<{ server: net.Server; port: number }> {
    const server = net.createServer();
    await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", () => resolve()));
    const address = server.address();
    if (address === null || typeof address === "string") {
        throw new Error("no port");
    }

    return { server, port: address.port };
}

async function freePort(): Promise<number> {
    const { server, port } = await listenEphemeral();
    await new Promise<void>((resolve) => server.close(() => resolve()));
    return port;
}

describe("net probes", () => {
    let server: net.Server | undefined;

    beforeEach(() => {
        server = undefined;
    });

    afterEach(async () => {
        if (server) {
            await new Promise<void>((resolve) => server?.close(() => resolve()));
        }
    });

    test("isPortOpen_listeningPort_true", async () => {
        const listening = await listenEphemeral();
        server = listening.server;

        expect(await isPortOpen("127.0.0.1", listening.port)).toBe(true);
    });

    test("isPortOpen_closedPort_false", async () => {
        const port = await freePort();

        expect(await isPortOpen("127.0.0.1", port)).toBe(false);
    });

    test("waitForPortOpen_timesOut_whenNothingListens", async () => {
        const port = await freePort();

        await expect(waitForPortOpen("127.0.0.1", port, 1)).rejects.toThrow(/Did not become reachable/);
    });

    test("findOpenPortInRange_returnsTheFirstListeningPort", async () => {
        const listening = await listenEphemeral();
        server = listening.server;

        const found = await findOpenPortInRange("127.0.0.1", listening.port, 3);

        expect(found).toBe(listening.port);
    });

    test("findOpenPortInRange_nothingOpen_isNull", async () => {
        const port = await freePort();

        expect(await findOpenPortInRange("127.0.0.1", port, 2)).toBeNull();
    });
});
