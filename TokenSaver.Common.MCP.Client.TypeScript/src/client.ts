/**
 * HTTP client for TokenSaver MCP server
 * Used by VS Code, Cursor, and other extensions to invoke MCP tools
 */

import type {
  BuildContextResult,
  ContextHygieneReport,
  DetectedProvider,
} from "@tokensaver/common-models";

export interface McpClientOptions {
  baseUrl: string;
  timeout?: number; // milliseconds
  retries?: number;
}

export interface McpToolCall {
  tool: string;
  input: Record<string, unknown>;
}

export interface McpToolResponse<T = unknown> {
  success: boolean;
  data?: T;
  error?: string;
  executionTimeMs?: number;
}

/**
 * HTTP client for calling TokenSaver MCP tools
 */
export class McpClient {
  private baseUrl: string;
  private timeout: number;
  private retries: number;

  constructor(options: McpClientOptions) {
    this.baseUrl = options.baseUrl.replace(/\/$/, ""); // Remove trailing slash
    this.timeout = options.timeout ?? 30000;
    this.retries = options.retries ?? 3;
  }

  /**
   * Call a tool on the MCP server
   */
  async callTool<T = unknown>(
    tool: string,
    input: Record<string, unknown>
  ): Promise<McpToolResponse<T>> {
    const url = `${this.baseUrl}/api/tools/${encodeURIComponent(tool)}`;

    for (let attempt = 1; attempt <= this.retries; attempt++) {
      try {
        const response = await fetch(url, {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
          },
          body: JSON.stringify(input),
          signal: AbortSignal.timeout(this.timeout),
        });

        if (!response.ok) {
          if (attempt < this.retries && response.status >= 500) {
            await this.delay(100 * attempt);
            continue;
          }

          return {
            success: false,
            error: `HTTP ${response.status}: ${response.statusText}`,
          };
        }

        const data = (await response.json()) as McpToolResponse<T>;
        return data;
      } catch (error) {
        if (attempt < this.retries) {
          await this.delay(100 * attempt);
          continue;
        }

        return {
          success: false,
          error: error instanceof Error ? error.message : String(error),
        };
      }
    }

    return {
      success: false,
      error: `Failed after ${this.retries} retries`,
    };
  }

  /**
   * Build concise context for an AI call
   */
  async buildContext(input: {
    workspaceRoot?: string;
    filePaths?: string[];
    query?: string;
    maxTokens?: number;
  }): Promise<McpToolResponse<BuildContextResult>> {
    return this.callTool<BuildContextResult>("BuildConciseContext", input);
  }

  /**
   * Check context hygiene
   */
  async checkContextHygiene(input: {
    contextText: string;
  }): Promise<McpToolResponse<ContextHygieneReport>> {
    return this.callTool<ContextHygieneReport>("ContextHygiene", input);
  }

  /**
   * Get diagnostic information
   */
  async getDiagnostics(): Promise<
    McpToolResponse<Record<string, unknown>>
  > {
    return this.callTool("ContextDiag", {});
  }

  /**
   * Verify server is healthy
   */
  async getHealth(): Promise<
    McpToolResponse<{ status: string; version: string }>
  > {
    try {
      const response = await fetch(`${this.baseUrl}/health`, {
        signal: AbortSignal.timeout(this.timeout),
      });

      if (response.ok) {
        return {
          success: true,
          data: (await response.json()) as { status: string; version: string },
        };
      }

      return {
        success: false,
        error: `HTTP ${response.status}`,
      };
    } catch (error) {
      return {
        success: false,
        error: error instanceof Error ? error.message : String(error),
      };
    }
  }

  /**
   * Check if server is reachable
   */
  async isReachable(): Promise<boolean> {
    try {
      const response = await fetch(`${this.baseUrl}/mcp`, {
        signal: AbortSignal.timeout(5000),
      });
      return response.ok || response.status === 405; // 405 = Method not allowed
    } catch {
      return false;
    }
  }

  private delay(ms: number): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }
}

/**
 * Create a client instance
 */
export function createMcpClient(options: McpClientOptions): McpClient {
  return new McpClient(options);
}
