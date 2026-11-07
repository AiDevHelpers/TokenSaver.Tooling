/**
 * Shared TypeScript contracts/interfaces for TokenSaver services
 * Mirrors TokenSaver.Common.Contracts (.NET library)
 * Defines the surface area that MCP tools and services must implement
 */

import type {
  CodeChunk,
  DistillResult,
  RepoIndexStats,
  SavingsAggregate,
  SemanticSearchHit,
} from "@tokensaver/common-models";

/**
 * Semantic search service - finds relevant code by meaning
 */
export interface ISemanticSearchService {
  /**
   * Search for chunks semantically similar to query
   */
  search(
    query: string,
    options?: {
      topK?: number;
      minRelevance?: number;
    }
  ): Promise<SemanticSearchHit[]>;

  /**
   * Index chunks for semantic search
   */
  indexChunks(chunks: CodeChunk[]): Promise<void>;

  /**
   * Clear all indexed chunks
   */
  clearIndex(): Promise<void>;

  /**
   * Get current index size
   */
  getIndexSize(): Promise<number>;
}

/**
 * Distillation service - summarizes text to reduce tokens
 */
export interface IDistillationService {
  /**
   * Distill/summarize text
   */
  distill(
    text: string,
    options: {
      targetTokens: number;
      context?: string;
    }
  ): Promise<DistillResult>;

  /**
   * Batch distill multiple texts
   */
  distillBatch(
    texts: string[],
    targetTokensPerItem: number
  ): Promise<DistillResult[]>;

  /**
   * Get distillation statistics
   */
  getSavings(since?: Date): Promise<SavingsAggregate>;
}

/**
 * Repository indexer - indexes source code
 */
export interface IRepoIndexer {
  /**
   * Index a repository
   */
  index(
    workspacePath: string,
    options?: {
      onProgress?: (message: string) => void;
    }
  ): Promise<RepoIndexStats>;

  /**
   * Get index statistics
   */
  getStats(): Promise<RepoIndexStats>;

  /**
   * Clear the index
   */
  clear(): Promise<void>;

  /**
   * Check if index is current
   */
  isUpToDate(): Promise<boolean>;

  /**
   * Rebuild the index
   */
  rebuild(options?: {
    onProgress?: (message: string) => void;
  }): Promise<RepoIndexStats>;
}

/**
 * Code chunk extractor - breaks files into manageable pieces
 */
export interface IChunkExtractor {
  /**
   * Extract chunks from a single file
   */
  extract(
    filePath: string,
    fileContent: string
  ): Promise<CodeChunk[]>;

  /**
   * Extract chunks from multiple files
   */
  extractBatch(
    files: Array<{ path: string; content: string }>
  ): Promise<CodeChunk[]>;

  /**
   * Get supported file extensions
   */
  getSupportedExtensions(): string[];
}

/**
 * Code summarizer - generates one-line summaries of code
 */
export interface ICodeSummarizer {
  /**
   * Summarize a code chunk
   */
  summarize(chunk: CodeChunk): Promise<string>;

  /**
   * Batch summarize chunks
   */
  summarizeBatch(chunks: CodeChunk[]): Promise<Map<string, string>>;
}

/**
 * Reranker service - reorders search results by relevance
 */
export interface IRerankService {
  /**
   * Rerank search results
   */
  rerank(
    query: string,
    results: SemanticSearchHit[],
    topK?: number
  ): Promise<SemanticSearchHit[]>;
}

/**
 * Conditional retrieval service - smart context building
 */
export interface IConditionalRetrievalService {
  /**
   * Retrieve context conditionally based on query
   */
  retrieve(
    query: string,
    options?: {
      maxTokens?: number;
      strategy?: "aggressive" | "moderate" | "conservative";
    }
  ): Promise<{ chunks: CodeChunk[]; totalTokens: number }>;
}

/**
 * Savings tracker - tracks token economy metrics
 */
export interface ISavingsTracker {
  /**
   * Record a distillation operation
   */
  recordDistillation(
    originalTokens: number,
    distilledTokens: number
  ): Promise<void>;

  /**
   * Record a retrieval operation
   */
  recordRetrieval(
    queryTokens: number,
    retrievedTokens: number
  ): Promise<void>;

  /**
   * Get aggregate savings
   */
  getSavings(since?: Date): Promise<SavingsAggregate>;

  /**
   * Clear history
   */
  clear(): Promise<void>;
}

/**
 * Tool registry - dynamic discovery of available MCP tools
 */
export interface IToolRegistry {
  /**
   * Get all registered tools
   */
  getTools(): ToolDefinition[];

  /**
   * Get a specific tool by name
   */
  getTool(name: string): ToolDefinition | null;

  /**
   * Check if a tool is available
   */
  hasTool(name: string): boolean;
}

/**
 * Client-extension settings shared by the TokenSaver VS Code and Visual Studio extensions.
 * Mirrors TokenSaver.Config.Contracts.IExtensionSettings (.NET) — the VS Code extension's
 * `tokenSaver.*` package.json settings map onto this shape 1:1 (see
 * TokenSaver.VsCode.Extension/package.json's `contributes.configuration`); the Visual Studio
 * extension mirrors the same shape locally in Configuration/BehaviorConfig.cs.
 */
export interface IExtensionSettings {
  /** tokenSaver.autostart */
  autostart?: boolean;
  /** tokenSaver.verboseLogging */
  verboseLogging?: boolean;
  /** tokenSaver.autoCodeHelper.enabled */
  autoCodeHelperEnabled?: boolean;
  /** tokenSaver.autoInstallPrompt */
  autoInstallPrompt?: boolean;
  /** tokenSaver.mcp.exePath */
  mcpExePath?: string;
  server?: IExtensionServerSettings;
  dotnet?: IExtensionDotnetSettings;
  notifications?: IExtensionNotificationsSettings;
  simpleModelServer?: IExtensionSimpleModelServerSettings;
}

/** tokenSaver.server.* */
export interface IExtensionServerSettings {
  port?: number;
  startTimeout?: number;
  idleTimeout?: number;
}

/** tokenSaver.dotnet.* */
export interface IExtensionDotnetSettings {
  command?: string;
  projectPath?: string;
}

/** tokenSaver.notifications.* */
export interface IExtensionNotificationsSettings {
  enabled?: boolean;
  level?: "all" | "errors";
}

/** tokenSaver.simpleModelServer.* */
export interface IExtensionSimpleModelServerSettings {
  exePath?: string;
  healthUrl?: string;
  startTimeout?: number;
}

/**
 * Tool definition - describes an MCP tool
 */
export interface ToolDefinition {
  name: string;
  description: string;
  category:
    | "context"
    | "analysis"
    | "optimization"
    | "configuration"
    | "diagnostics";
  inputSchema: Record<string, unknown>;
  outputSchema: Record<string, unknown>;
  version: string;
}
