import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import * as path from "node:path";
import { afterEach, beforeEach, describe, expect, test } from "vitest";
import {
    authHeaders,
    buildUserPickItems,
    defaultTokenPaths,
    expandEnv,
    isServerAlive,
    isValidAccount,
    readAuthToken,
} from "./auth";

const TOKEN = "a".repeat(43);

describe("token location and reading", () => {
    let dir: string;

    beforeEach(() => {
        dir = mkdtempSync(path.join(tmpdir(), "tsauth-"));
    });

    afterEach(() => {
        rmSync(dir, { recursive: true, force: true });
    });

    test("defaultTokenPaths_programDataFirstThenLocalAppData", () => {
        const paths = defaultTokenPaths({ ProgramData: "C:\\PD", LOCALAPPDATA: "C:\\LA" });

        expect(paths).toEqual([path.join("C:\\PD", "TokenSaver", "auth-token"), path.join("C:\\LA", "TokenSaver", "auth-token")]);
    });

    test("readAuthToken_configuredPathWins_andIsTrimmed", async () => {
        const file = path.join(dir, "token");
        writeFileSync(file, `${TOKEN}\r\n`);

        expect(await readAuthToken(file, {})).toBe(TOKEN);
    });

    test("readAuthToken_fallsBackToTheNextDefaultLocation", async () => {
        const local = path.join(dir, "TokenSaver");
        writeFileSync(path.join(dir, "decoy"), "x");
        rmSync(local, { recursive: true, force: true });
        const second = path.join(dir, "second");
        writeFileSync(second, TOKEN);

        expect(await readAuthToken("", { ProgramData: path.join(dir, "missing"), LOCALAPPDATA: dir })).toBeUndefined();
        expect(await readAuthToken(second, {})).toBe(TOKEN);
    });

    test("readAuthToken_missingOrTooShortFile_isUndefined", async () => {
        const short = path.join(dir, "short");
        writeFileSync(short, "tiny");

        expect(await readAuthToken(path.join(dir, "nope"), {})).toBeUndefined();
        expect(await readAuthToken(short, {})).toBeUndefined();
    });

    test("expandEnv_replacesKnownVariablesOnly", () => {
        expect(expandEnv("%ProgramData%\\x", { ProgramData: "C:\\PD" })).toBe("C:\\PD\\x");
        expect(expandEnv("%UNKNOWN%\\x", {})).toBe("%UNKNOWN%\\x");
    });
});

describe("headers and liveness", () => {
    test("authHeaders_bearerWhenTokenPresent_emptyOtherwise", () => {
        expect(authHeaders(TOKEN)).toEqual({ Authorization: `Bearer ${TOKEN}` });
        expect(authHeaders(undefined)).toEqual({});
    });

    test("isServerAlive_unauthorizedStillMeansTheServerIsUp", () => {
        expect(isServerAlive(200)).toBe(true);
        expect(isServerAlive(405)).toBe(true);
        expect(isServerAlive(401)).toBe(true);
        expect(isServerAlive(500)).toBe(false);
        expect(isServerAlive(403)).toBe(false);
    });
});

describe("account validation and picker rows", () => {
    test("isValidAccount_acceptsNamesAndRejectsInjection", () => {
        for (const ok of ["alice", "PC\\alice", "first last", "svc$"]) {
            expect(isValidAccount(ok), ok).toBe(true);
        }

        for (const bad of ["", "..\\x", "a/b", "a;rm -rf", 'a"b', "a&b", "..", "."]) {
            expect(isValidAccount(bad), bad).toBe(false);
        }
    });

    test("buildUserPickItems_membersFirst_thenCandidatesNotAlreadyMembers", () => {
        const items = buildUserPickItems({
            group: "TokenSaver Users",
            tokenPath: "x",
            members: ["PC\\alice"],
            candidates: ["PC\\Alice", "PC\\bob"],
        });

        expect(items.map((i) => [i.account, i.action])).toEqual([
            ["PC\\alice", "remove"],
            ["PC\\bob", "add"],
        ]);
    });
});
