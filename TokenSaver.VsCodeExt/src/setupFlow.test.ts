import { describe, expect, test } from "vitest";
import { maybePromptForServiceInstall, runFullSetup, SERVICE_INSTALL_PROMPT_ASKED_KEY, type SetupDeps } from "./setupFlow";

function fakeContext(extensionPath = "C:\\nowhere") {
    const store = new Map<string, unknown>();
    return {
        extensionPath,
        store,
        globalState: {
            get: <T>(key: string, fallback: T) => (store.has(key) ? (store.get(key) as T) : fallback),
            update: async (key: string, value: unknown) => {
                store.set(key, value);
            },
        },
    };
}

function fakeDeps(installed: string[] = [], overrides: Partial<SetupDeps> = {}) {
    const calls: string[] = [];
    const installedSet = new Set(installed);
    const deps: SetupDeps = {
        log: (m) => calls.push(`log:${m}`),
        notify: (t, m) => calls.push(`notify:${t}:${m}`),
        info: (m) => calls.push(`info:${m}`),
        error: (m) => calls.push(`error:${m}`),
        ask: async () => "Not now",
        settings: () => ({
            healthUrl: "http://localhost:11435/health",
            simpleModelServerTimeoutSec: 6,
            port: 11436,
            mcpTimeoutSec: 1,
            autoInstallPrompt: true,
        }),
        isServiceInstalled: async (name) => installedSet.has(name),
        runSc: async (args) => {
            calls.push(`sc:${args.join(" ")}`);
            return 0;
        },
        waitForHealthOk: async () => true,
        verifyMcp: async () => true,
        installSimpleModelServer: async () => {
            calls.push("installSimpleModelServer");
            installedSet.add("SimpleModelServer");
        },
        installTokenSaver: async () => {
            calls.push("installTokenSaver");
            installedSet.add("TokenSaverMCP");
        },
        checkMcpFullHealth: async () => true,
        openInstallPrompt: async () => {
            calls.push("openInstallPrompt");
        },
        installedVsixVersion: () => undefined,
        installVsix: async () => {},
        ...overrides,
    };
    return { deps, calls };
}

describe("runFullSetup", () => {
    test("bothServicesInstalledAndHealthy_skipsInstallsAndOpensThePrompt", async () => {
        const { deps, calls } = fakeDeps(["SimpleModelServer", "TokenSaverMCP"]);

        await runFullSetup(fakeContext() as never, deps);

        expect(calls).not.toContain("installSimpleModelServer");
        expect(calls).not.toContain("installTokenSaver");
        expect(calls[calls.length - 1]).toBe("info:TokenSaver setup complete: SimpleModelServer and TokenSaver.MCP are both installed and running.");
        expect(calls).toContain("openInstallPrompt");
    });

    test("simpleModelServerDoesNotInstall_stopsBeforeTokenSaver", async () => {
        const { deps, calls } = fakeDeps([], { installSimpleModelServer: async () => {} });

        await runFullSetup(fakeContext() as never, deps);

        expect(calls.some((c) => c.startsWith("error:TokenSaver setup failed: SimpleModelServer did not install"))).toBe(true);
        expect(calls).not.toContain("installTokenSaver");
        expect(calls).not.toContain("openInstallPrompt");
    });

    test("simpleModelServerNeverHealthy_reportsAndStops", async () => {
        const { deps, calls } = fakeDeps(["SimpleModelServer"], { waitForHealthOk: async () => false });

        await runFullSetup(fakeContext() as never, deps);

        expect(calls.some((c) => c.startsWith("error:TokenSaver setup failed: SimpleModelServer did not become healthy"))).toBe(true);
        expect(calls).not.toContain("installTokenSaver");
        expect(calls).toContain("sc:stop SimpleModelServer");
    });

    test("mcpNeverReachable_reportsAndStopsBeforeThePrompt", async () => {
        const { deps, calls } = fakeDeps(["SimpleModelServer", "TokenSaverMCP"], { verifyMcp: async () => false });

        await runFullSetup(fakeContext() as never, deps);

        expect(calls.some((c) => c.startsWith("error:TokenSaver setup failed: TokenSaver.MCP did not respond"))).toBe(true);
        expect(calls).not.toContain("openInstallPrompt");
    });
});

describe("maybePromptForServiceInstall", () => {
    test("autoInstallPromptOff_neverAsks", async () => {
        let asked = false;
        const { deps } = fakeDeps([], {
            settings: () => ({ healthUrl: "", simpleModelServerTimeoutSec: 0, port: 0, mcpTimeoutSec: 0, autoInstallPrompt: false }),
            ask: async () => {
                asked = true;
                return "Install";
            },
        });

        await maybePromptForServiceInstall(fakeContext() as never, deps);

        expect(asked).toBe(false);
    });

    test("alreadyAnswered_neverAsksAgain", async () => {
        const context = fakeContext();
        context.store.set(SERVICE_INSTALL_PROMPT_ASKED_KEY, true);
        let asked = false;
        const { deps } = fakeDeps([], {
            ask: async () => {
                asked = true;
                return "Install";
            },
        });

        await maybePromptForServiceInstall(context as never, deps);

        expect(asked).toBe(false);
    });

    test("noBundledInstallers_marksAskedWithoutAsking", async () => {
        // The extension path has no bin/*.Setup folders, so there is nothing to offer.
        const context = fakeContext("C:\\definitely-not-here");
        const { deps } = fakeDeps();

        await maybePromptForServiceInstall(context as never, deps);

        expect(context.store.get(SERVICE_INSTALL_PROMPT_ASKED_KEY)).toBeUndefined();
    });
});
