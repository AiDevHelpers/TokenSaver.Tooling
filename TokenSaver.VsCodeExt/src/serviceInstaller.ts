import * as fs from "fs";

/** The strings and service name that differ between the two WiX installers. */
export interface WindowsServiceSpec {
    serviceName: string;
    /** Short name in error messages, e.g. "TokenSaver" or "SimpleModelServer". */
    label: string;
    /** Name in log lines, e.g. "TokenSaverMCP". */
    logName: string;
    alreadyInstalled: string;
    installing: string;
    installed: string;
    notInstalled: string;
    uninstalling: string;
    removed: string;
}

export interface ServiceInstallerDeps {
    log(message: string): void;
    info(message: string): void;
    error(message: string): void;
    isInstalled(serviceName: string): Promise<boolean>;
    runMsiexec(args: string[]): Promise<number>;
    runSc(args: string[]): Promise<number>;
    /** Called once the service is installed and started. */
    onInstalled(): void;
    /** Called once the service is removed. */
    onUninstalled(): void;
    /** Called after a successful install, e.g. to open the install prompt when the other service is also done. */
    afterInstall(): Promise<void>;
}

/** Installs the service from its bundled MSI. Does nothing when the service already exists. */
export async function installWindowsService(
    spec: WindowsServiceSpec,
    msi: string,
    deps: ServiceInstallerDeps,
): Promise<void> {
    if (await deps.isInstalled(spec.serviceName)) {
        deps.info(spec.alreadyInstalled);
        return;
    }

    if (!fs.existsSync(msi)) {
        deps.error(
            `${spec.label} installer not found at: ${msi}\nRebuild the VSIX with build\\scripts\\build-vsix.ps1 (requires the wix CLI: dotnet tool install -g wix).`,
        );
        return;
    }

    deps.log(`Running installer: ${msi}`);
    deps.info(spec.installing);

    // /qn runs the install silently; msiexec's elevation is handled by runMsiexec.
    const code = await deps.runMsiexec(["/i", msi, "/qn", "/norestart"]);
    if (code !== 0) {
        deps.log(`Installer exited with code ${code}.`);
        deps.error(`${spec.label} installer failed (msiexec exit code ${code}).`);
        return;
    }

    deps.onInstalled();
    deps.log(`${spec.logName} service installed and started.`);
    deps.info(spec.installed);
    await deps.afterInstall();
}

/**
 * Removes the service by running its MSI's uninstall. Without a bundled MSI (or without an extension context to find
 * it) it falls back to sc.exe stop and delete. `resetInstallPrompt` is set only when a context is available, so the
 * first-run install prompt is offered again next activation.
 */
export async function uninstallWindowsService(
    spec: WindowsServiceSpec,
    msi: string | undefined,
    deps: ServiceInstallerDeps,
    resetInstallPrompt?: () => Promise<void>,
): Promise<void> {
    if (!(await deps.isInstalled(spec.serviceName))) {
        deps.info(spec.notInstalled);
        return;
    }

    if (!msi || !fs.existsSync(msi)) {
        // Bundled MSI unavailable: fall back to sc.exe so uninstall still works.
        deps.log("Bundled MSI not found — falling back to sc.exe stop/delete.");
        await deps.runSc(["stop", spec.serviceName]);
        const scCode = await deps.runSc(["delete", spec.serviceName]);
        if (scCode !== 0) {
            deps.error(`sc.exe delete failed (code ${scCode}). Try running VS Code as Administrator.`);
            return;
        }
    } else {
        deps.log(`Running uninstaller: ${msi}`);
        deps.info(spec.uninstalling);
        const code = await deps.runMsiexec(["/x", msi, "/qn", "/norestart"]);
        if (code !== 0) {
            deps.log(`Uninstaller exited with code ${code}.`);
            deps.error(`${spec.label} uninstaller failed (msiexec exit code ${code}).`);
            return;
        }
    }

    deps.onUninstalled();
    deps.log(`${spec.logName} service uninstalled.`);
    deps.info(spec.removed);

    if (resetInstallPrompt) {
        await resetInstallPrompt();
    }
}
