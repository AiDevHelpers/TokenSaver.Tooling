import { describe, expect, test } from "vitest";
import { formatLogLine, listeningPort, splitChunk } from "./stdoutRouting";

const noTime = (ts: string) => `@${ts}`;

describe("formatLogLine", () => {
    test("plainText_passesThroughUnchanged", () => {
        expect(formatLogLine("Application started.")).toEqual(["Application started."]);
    });

    test("structuredEvent_isFormattedWithLevelAndSource", () => {
        const line = JSON.stringify({ ts: "2026-11-18T09:00:00Z", lvl: "WRN", src: "Router", msg: "slow" });

        expect(formatLogLine(line, noTime)).toEqual(["@2026-11-18T09:00:00Z WRN Router: slow"]);
    });

    test("structuredEvent_withException_addsTheExceptionLine", () => {
        const line = JSON.stringify({ lvl: "ERR", msg: "failed", ex: "System.Exception: x" });

        expect(formatLogLine(line, noTime)).toEqual([" ERR failed", "System.Exception: x"]);
    });

    test("structuredEvent_defaultsToInfoWhenLevelIsMissing", () => {
        expect(formatLogLine(JSON.stringify({ msg: "hello" }))).toEqual([" INF hello"]);
    });

    test("malformedJsonStartingWithBrace_isKeptAsText", () => {
        expect(formatLogLine("{not json")).toEqual(["{not json"]);
    });
});

describe("splitChunk", () => {
    test("completeLines_areReturnedAndNothingIsLeftOver", () => {
        expect(splitChunk("", "a\nb\n")).toEqual({ lines: ["a", "b"], rest: "" });
    });

    test("partialLine_isHeldUntilItsEnd", () => {
        const first = splitChunk("", "hel");
        expect(first).toEqual({ lines: [], rest: "hel" });

        const second = splitChunk(first.rest, "lo\nwor");
        expect(second).toEqual({ lines: ["hello"], rest: "wor" });
    });

    test("crlfLineBreaks_areHandled", () => {
        expect(splitChunk("", "x\r\ny\r\n").lines).toEqual(["x", "y"]);
    });
});

describe("listeningPort", () => {
    test("hostListeningLine_givesThePort", () => {
        expect(listeningPort("Now listening on: http://127.0.0.1:11436")).toBe(11436);
    });

    test("otherLine_isNull", () => {
        expect(listeningPort("Application started.")).toBeNull();
    });
});
