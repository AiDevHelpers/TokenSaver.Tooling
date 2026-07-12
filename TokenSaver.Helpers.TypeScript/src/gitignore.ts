/**
 * Gitignore management utilities
 */

import * as fs from "fs";
import * as path from "path";

/**
 * Checks if .tokensaver/ is already covered by .gitignore
 */
export function isTokenSaverInGitignore(workspaceRoot: string): boolean {
  const gitignorePath = path.join(workspaceRoot, ".gitignore");

  if (!fs.existsSync(gitignorePath)) {
    return false;
  }

  try {
    const content = fs.readFileSync(gitignorePath, "utf8");
    return isEntryCovered(content, ".tokensaver");
  } catch {
    return false;
  }
}

/**
 * Adds .tokensaver/ to the workspace .gitignore if not already present
 */
export function addTokenSaverToGitignore(workspaceRoot: string): void {
  const gitignorePath = path.join(workspaceRoot, ".gitignore");
  const existing = fs.existsSync(gitignorePath)
    ? fs.readFileSync(gitignorePath, "utf8")
    : "";

  if (!existing) {
    fs.writeFileSync(gitignorePath, ".tokensaver/\n", "utf8");
  } else if (!existing.endsWith("\n")) {
    fs.appendFileSync(gitignorePath, "\n.tokensaver/\n", "utf8");
  } else {
    fs.appendFileSync(gitignorePath, ".tokensaver/\n", "utf8");
  }
}

/**
 * Checks if a pattern in .gitignore covers the given entry
 */
function isEntryCovered(gitignoreContent: string, entry: string): boolean {
  const normalized = entry.toLowerCase();

  const lines = gitignoreContent.split(/\r?\n/);

  for (const rawLine of lines) {
    const line = rawLine.trim();

    // Skip comments and empty lines
    if (!line || line.startsWith("#") || line.startsWith("!")) {
      continue;
    }

    // Normalize the pattern by removing leading/trailing slashes
    const stripped = line.replace(/\//g, "");
    if (!stripped) {
      continue;
    }

    // Check if this pattern matches the entry
    const strippedLower = stripped.toLowerCase();
    if (
      normalized === strippedLower ||
      normalized.startsWith(strippedLower + "/")
    ) {
      return true;
    }
  }

  return false;
}
