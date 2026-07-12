/**
 * Shared TypeScript types and models for TokenSaver
 * Mirrors TokenSaver.Common.Models (.NET library)
 */

/**
 * AI provider type enumeration
 */
export enum ProviderType {
  Anthropic = "Anthropic",
  OpenAI = "OpenAI",
  DeepSeek = "DeepSeek",
  Qwen = "Qwen",
  OpenSource = "OpenSource",
  Unknown = "Unknown",
}

/**
 * Result of building context for an AI call
 */
export interface BuildContextResult {
  success: boolean;
  contextText: string;
  tokenCount: number;
  builderVersion: string;
  builtAt: Date;
  sourceFileCount: number;
  excludedFileCount: number;
  warnings?: string[];
}

/**
 * Context hygiene report - diagnostics about context quality
 */
export interface ContextHygieneReport {
  status: "healthy" | "degraded" | "poor";
  issueCount: number;
  issues: ContextIssue[];
  recommendations: string[];
  analysisTime: number; // milliseconds
}

/**
 * Single context hygiene issue
 */
export interface ContextIssue {
  severity: "info" | "warning" | "error";
  type: string;
  description: string;
  affectedSections?: string[];
  suggestedFix?: string;
}

/**
 * Conversation index for tracking context through a session
 */
export interface ConversationIndex {
  sessionId: string;
  turnNumber: number;
  cumulativeTokens: number;
  contextSections: ContextSection[];
  lastUpdated: Date;
}

/**
 * A section of context in a conversation
 */
export interface ContextSection {
  id: string;
  type: "system" | "code" | "docs" | "conversation" | "analysis";
  title: string;
  tokenCount: number;
  addedAtTurn: number;
  isStale: boolean;
  staleSinceTokens?: number;
}

/**
 * AI provider detection result
 */
export interface DetectedProvider {
  provider: ProviderType;
  confidence: number; // 0-1
  detectionMethod: string;
  clientName?: string;
  version?: string;
}

/**
 * Configuration update result from MCP call
 */
export interface ConfigUpdateResult {
  success: boolean;
  appliedChanges: string[];
  warnings?: string[];
  errors?: string[];
  requiresReload: boolean;
}

/**
 * Code chunk for token economy tracking
 */
export interface CodeChunk {
  id: string;
  path: string;
  language: string;
  content: string;
  startLine: number;
  endLine: number;
  tokenCount: number;
  summary?: string;
}

/**
 * Result of distillation (summarization) operation
 */
export interface DistillResult {
  original: {
    text: string;
    tokenCount: number;
  };
  distilled: {
    text: string;
    tokenCount: number;
  };
  reductionRatio: number; // percentage saved
  quality: "low" | "medium" | "high";
  executionTimeMs: number;
}

/**
 * Savings aggregate for token economy tracking
 */
export interface SavingsAggregate {
  totalTokensSaved: number;
  totalDistillOperations: number;
  averageSavingsPercent: number;
  byOperation: Record<string, number>;
  period: {
    startDate: Date;
    endDate: Date;
  };
}

/**
 * Outcome of a retrieval operation
 */
export interface RetrievalOutcome {
  queryTokens: number;
  retrievedChunks: number;
  totalRetrievedTokens: number;
  rerankingApplied: boolean;
  topResultRelevance: number; // 0-1
  executionTimeMs: number;
}

/**
 * Semantic search hit
 */
export interface SemanticSearchHit {
  fileId: string;
  filePath: string;
  startLine: number;
  endLine: number;
  relevanceScore: number; // 0-1
  snippet: string;
  snippetTokens: number;
}

/**
 * Workspace preferences
 */
export interface WorkspacePreferences {
  languagePreferences: Record<string, number>; // language -> priority
  excludePatterns: string[];
  includePatterns: string[];
  indexingStrategy: "aggressive" | "moderate" | "conservative";
  tokenBudget: number;
  refreshIntervalMinutes: number;
}

/**
 * Ollama model entry
 */
export interface OllamaModelEntry {
  name: string;
  size: number;
  digest: string;
  modifiedTime: Date;
}

/**
 * Ollama API status result
 */
export interface OllamaStatusResult {
  status: "ok" | "degraded" | "error";
  message: string;
  modelsAvailable: OllamaModelEntry[];
  gpuStats?: GpuStats;
}

/**
 * GPU statistics from Ollama
 */
export interface GpuStats {
  name: string;
  available: number;
  total: number;
  usagePercent: number;
}

/**
 * Test case for accuracy testing
 */
export interface TestCase {
  id: string;
  name: string;
  input: string;
  expectedOutput: string;
  category: string;
  priority: "low" | "medium" | "high";
}

/**
 * Test result from accuracy evaluation
 */
export interface TestResult {
  testCaseId: string;
  actualOutput: string;
  passed: boolean;
  score: number; // 0-1
  feedback: string;
  executionTimeMs: number;
}

/**
 * Full test report
 */
export interface TestReport {
  reportId: string;
  timestamp: Date;
  totalTests: number;
  passedTests: number;
  failedTests: number;
  skippedTests: number;
  successRate: number; // percentage
  results: TestResult[];
}
