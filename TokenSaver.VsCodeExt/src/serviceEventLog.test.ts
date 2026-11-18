import { describe, expect, test } from "vitest";
import { formatEventLines, parseEventLogJson } from "./serviceEventLog";

describe("parseEventLogJson", () => {
    test("array_isReturnedAsIs", () => {
        const entries = parseEventLogJson('[{"RecordId":1},{"RecordId":2}]');

        expect(entries.map((e) => e.RecordId)).toEqual([1, 2]);
    });

    test("singleObject_isWrappedInAnArray", () => {
        expect(parseEventLogJson('{"RecordId":7}')).toEqual([{ RecordId: 7 }]);
    });

    test("nullJson_isEmpty", () => {
        expect(parseEventLogJson("null")).toEqual([]);
    });

    test("invalidJson_throws", () => {
        expect(() => parseEventLogJson("{")).toThrow();
    });
});

describe("formatEventLines", () => {
    test("singleLineMessage_isStampedWithTimestampAndLevel", () => {
        const lines = formatEventLines({ TimeCreated: "2026-11-18 09:00:00", LevelDisplayName: "Error", Message: "boom" });

        expect(lines).toEqual(["2026-11-18 09:00:00 Error boom"]);
    });

    test("continuationLines_areIndentedAndNotStamped", () => {
        const lines = formatEventLines({
            TimeCreated: "t",
            LevelDisplayName: "Warning",
            Message: "first\r\n  at a\n\n  at b",
        });

        expect(lines).toEqual(["t Warning first", "      at a", "      at b"]);
    });

    test("missingFields_useDefaults", () => {
        expect(formatEventLines({ Message: "x" })).toEqual([" INFO x"]);
    });

    test("emptyMessage_producesNoLines", () => {
        expect(formatEventLines({ TimeCreated: "t", Message: "" })).toEqual([]);
    });
});
