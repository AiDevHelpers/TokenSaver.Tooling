import { addAuthUser, buildUserPickItems, isValidAccount, listAuthUsers, removeAuthUser, type AuthUsers, type UserPickItem } from "./auth.js";

export type UserPick = UserPickItem;

export interface UserManagementDeps {
    port(): number;
    token(): Promise<string | undefined>;
    warn(message: string): void;
    info(message: string): void;
    error(message: string): void;
    /** Shows the member list and returns the choice, or undefined when dismissed. */
    pick(items: UserPick[], title: string): Promise<UserPick | undefined>;
    /** Asks for an account name. Returns undefined when dismissed. */
    input(): Promise<string | undefined>;
}

/** The "Manage Users" flow: pick a member to remove, a local account to add, or type another account. */
export async function manageUsers(deps: UserManagementDeps): Promise<void> {
    const port = deps.port();
    const token = await deps.token();
    if (!token) {
        deps.warn("TokenSaver: no readable auth token, so users cannot be managed from this account.");
        return;
    }

    try {
        const info: AuthUsers = await listAuthUsers(port, token);
        const items: UserPick[] = buildUserPickItems(info);
        const picked = await deps.pick(
            [...items, { label: "$(add) Enter another account…", description: "DOMAIN\\name", account: "", action: "add" }],
            `TokenSaver users (group: ${info.group})`,
        );
        if (!picked) {
            return;
        }

        let account = picked.account;
        if (account === "") {
            const typed = await deps.input();
            if (!typed) {
                return;
            }
            account = typed;
        }
        if (!isValidAccount(account)) {
            deps.error("TokenSaver: not a valid account name.");
            return;
        }

        if (picked.action === "remove") {
            await removeAuthUser(port, token, account);
            deps.info(`TokenSaver: removed ${account}.`);
        } else {
            await addAuthUser(port, token, account);
            deps.info(`TokenSaver: added ${account}.`);
        }
    } catch (err) {
        deps.error(`TokenSaver: ${(err as Error).message}`);
    }
}
