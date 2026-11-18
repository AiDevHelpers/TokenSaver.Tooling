import { describe, expect, test } from "vitest";
import { applyStatusBar, shouldNotify, statusReportLines, timestampPrefix, type StatusBarLike } from "./statusUi";

function fakeBar(): StatusBarLike & { shown: boolean } {
    return { text: "", tooltip: undefined, shown: false, show() { this.shown = true; } };
}

describe("applyStatusBar", () => {
    test("running_withAutoCodeHelper_showsTheRocket", () => {
        const bar = fakeBar();
        applyStatusBar(bar, "running", true);
        expect(bar.text).toBe("$(server) TokenSaver: Running $(rocket)");
        expect(bar.shown).toBe(true);
    });

    test("error_showsTheErrorIcon", () => {
        const bar = fakeBar();
        applyStatusBar(bar, "error", false);
        expect(bar.text).toBe("$(issue-opened) TokenSaver: Error");
    });
});

describe("statusReportLines", () => {
    test("reportsProcessesOrServices_andTheActualPort", () => {
        const lines = statusReportLines({
            configuredPort: 11436,
            actualPort: null,
            mcpWindowsService: true,
            mcpChildRunning: false,
            simpleModelServerWindowsService: false,
            simpleModelServerChildRunning: true,
            simpleModelServerHealthUrl: "http://localhost:11435/health",
        });
        expect(lines).toContain("MCP server actual port: (not running)");
        expect(lines).toContain("MCP server: running as Windows service (TokenSaverMCP)");
        expect(lines).toContain("SimpleModelServer process running: true");
    });
});

describe("shouldNotify", () => {
    test("disabled_neverNotifies", () => {
        expect(shouldNotify("error", false, "all")).toBe(false);
    });

    test("errorsOnlyLevel_suppressesInfo", () => {
        expect(shouldNotify("info", true, "errors")).toBe(false);
        expect(shouldNotify("error", true, "errors")).toBe(true);
    });
});

describe("timestampPrefix", () => {
    test("formatsLikeTheServiceLogs", () => {
        expect(timestampPrefix(new Date(2026, 10, 18, 9, 5, 7))).toBe("2026-11-18 09:05:07");
    });
});
