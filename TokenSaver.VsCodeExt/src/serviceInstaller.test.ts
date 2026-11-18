import { afterEach, beforeEach, describe, expect, test } from "vitest";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import * as path from "node:path";
import {
    installWindowsService,
    uninstallWindowsService,
    type ServiceInstallerDeps,
    type WindowsServiceSpec,
} from "./serviceInstaller";

const spec: WindowsServiceSpec = {
    serviceName: "TestSvc",
    label: "Test",
    logName: "TestSvc",
    alreadyInstalled: "already",
    installing: "installing",
    installed: "installed",
    notInstalled: "not installed",
    uninstalling: "uninstalling",
    removed: "removed",
};

function fakeDeps(overrides: Partial<ServiceInstallerDeps> = {}) {
    const calls: string[] = [];
    const deps: ServiceInstallerDeps = {
        log: (m) => calls.push(`log:${m}`),
        info: (m) => calls.push(`info:${m}`),
        error: (m) => calls.push(`error:${m}`),
        isInstalled: async () => false,
        runMsiexec: async (args) => {
            calls.push(`msiexec:${args.join(" ")}`);
            return 0;
        },
        runSc: async (args) => {
            calls.push(`sc:${args.join(" ")}`);
            return 0;
        },
        onInstalled: () => calls.push("onInstalled"),
        onUninstalled: () => calls.push("onUninstalled"),
        afterInstall: async () => {
            calls.push("afterInstall");
        },
        ...overrides,
    };
    return { deps, calls };
}

describe("installWindowsService", () => {
    let dir: string;
    let msi: string;

    beforeEach(() => {
        dir = mkdtempSync(path.join(tmpdir(), "svcinst-"));
        msi = path.join(dir, "setup.msi");
        writeFileSync(msi, "x");
    });

    afterEach(() => {
        rmSync(dir, { recursive: true, force: true });
    });

    test("alreadyInstalled_reportsAndDoesNothingElse", async () => {
        const { deps, calls } = fakeDeps({ isInstalled: async () => true });

        await installWindowsService(spec, msi, deps);

        expect(calls).toEqual(["info:already"]);
    });

    test("missingMsi_reportsTheMissingInstaller", async () => {
        const { deps, calls } = fakeDeps();

        await installWindowsService(spec, path.join(dir, "missing.msi"), deps);

        expect(calls.some((c) => c.startsWith("error:Test installer not found"))).toBe(true);
        expect(calls.some((c) => c.startsWith("msiexec:"))).toBe(false);
    });

    test("msiexecFailure_reportsTheExitCodeAndDoesNotMarkInstalled", async () => {
        const { deps, calls } = fakeDeps({ runMsiexec: async () => 1603 });

        await installWindowsService(spec, msi, deps);

        expect(calls).toContain("error:Test installer failed (msiexec exit code 1603).");
        expect(calls).not.toContain("onInstalled");
        expect(calls).not.toContain("afterInstall");
    });

    test("success_marksInstalledAndRunsTheAfterInstallStep", async () => {
        const { deps, calls } = fakeDeps();

        await installWindowsService(spec, msi, deps);

        expect(calls).toEqual([
            `log:Running installer: ${msi}`,
            "info:installing",
            "msiexec:/i " + msi + " /qn /norestart",
            "onInstalled",
            "log:TestSvc service installed and started.",
            "info:installed",
            "afterInstall",
        ]);
    });
});

describe("uninstallWindowsService", () => {
    test("notInstalled_reportsAndDoesNothingElse", async () => {
        const { deps, calls } = fakeDeps();

        await uninstallWindowsService(spec, undefined, deps);

        expect(calls).toEqual(["info:not installed"]);
    });

    test("noMsi_fallsBackToScStopAndDelete", async () => {
        const { deps, calls } = fakeDeps({ isInstalled: async () => true });

        await uninstallWindowsService(spec, undefined, deps);

        expect(calls).toContain("sc:stop TestSvc");
        expect(calls).toContain("sc:delete TestSvc");
        expect(calls).toContain("onUninstalled");
    });

    test("scDeleteFailure_reportsAndDoesNotMarkRemoved", async () => {
        const { deps, calls } = fakeDeps({
            isInstalled: async () => true,
            runSc: async (args) => (args[0] === "delete" ? 5 : 0),
        });

        await uninstallWindowsService(spec, undefined, deps);

        expect(calls.some((c) => c.startsWith("error:sc.exe delete failed (code 5)"))).toBe(true);
        expect(calls).not.toContain("onUninstalled");
    });

    test("success_marksRemovedAndResetsThePromptWhenAsked", async () => {
        const dir = mkdtempSync(path.join(tmpdir(), "svcuninst-"));
        const msi = path.join(dir, "setup.msi");
        writeFileSync(msi, "x");
        try {
            const { deps, calls } = fakeDeps({ isInstalled: async () => true });
            let reset = false;

            await uninstallWindowsService(spec, msi, deps, async () => {
                reset = true;
            });

            expect(calls).toContain("onUninstalled");
            expect(calls).toContain("info:removed");
            expect(reset).toBe(true);
        } finally {
            rmSync(dir, { recursive: true, force: true });
        }
    });

    test("noResetCallback_stillMarksRemoved", async () => {
        const { deps, calls } = fakeDeps({ isInstalled: async () => true });

        await uninstallWindowsService(spec, undefined, deps);

        expect(calls).toContain("onUninstalled");
    });
});
