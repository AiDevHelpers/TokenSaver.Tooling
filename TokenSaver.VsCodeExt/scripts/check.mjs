// Runs the extension's gate in order: compile, lint, test. Stops at the first failure.
import { spawnSync } from "node:child_process";

for (const step of ["build", "lint", "test"]) {
  // npm_execpath is set when this runs under npm; calling its JS entry point avoids shell quoting issues on Windows.
  const npmCli = process.env.npm_execpath;
  const result = npmCli
    ? spawnSync(process.execPath, [npmCli, "run", step], { stdio: "inherit" })
    : spawnSync("npm", ["run", step], { stdio: "inherit", shell: process.platform === "win32" });
  if (result.status !== 0) {
    process.exit(result.status ?? 1);
  }
}
