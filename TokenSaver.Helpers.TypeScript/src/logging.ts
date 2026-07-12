/**
 * Logging abstractions for TokenSaver extensions
 */

export enum LogLevel {
  Debug = "DEBUG",
  Info = "INFO",
  Error = "ERROR",
  Warning = "WARNING",
}

export interface LogEntry {
  timestamp: Date;
  level: LogLevel;
  message: string;
}

export interface ILogger {
  logInfo(message: string): void;
  logError(message: string): void;
  logDebug(message: string): void;
  logWarning(message: string): void;
  logException(error: Error): void;
}

/**
 * In-memory logger for testing or fallback logging
 */
export class DefaultLogger implements ILogger {
  private entries: LogEntry[] = [];

  logInfo(message: string): void {
    this.add(LogLevel.Info, message);
  }

  logError(message: string): void {
    this.add(LogLevel.Error, message);
  }

  logDebug(message: string): void {
    this.add(LogLevel.Debug, message);
  }

  logWarning(message: string): void {
    this.add(LogLevel.Warning, message);
  }

  logException(error: Error): void {
    const message = `${error.name}: ${error.message}\n${error.stack}`;
    this.add(LogLevel.Error, message);
  }

  private add(level: LogLevel, message: string): void {
    this.entries.push({
      timestamp: new Date(),
      level,
      message,
    });
  }

  getEntries(): LogEntry[] {
    return [...this.entries];
  }

  clear(): void {
    this.entries = [];
  }
}

/**
 * Null logger that discards all messages
 */
export class NullLogger implements ILogger {
  logInfo(): void {}
  logError(): void {}
  logDebug(): void {}
  logWarning(): void {}
  logException(): void {}
}

let globalLogger: ILogger = new NullLogger();

/**
 * Set the global logger instance
 */
export function setLogger(logger: ILogger): void {
  globalLogger = logger ?? new NullLogger();
}

/**
 * Get the current global logger
 */
export function getLogger(): ILogger {
  return globalLogger;
}

export function logInfo(message: string): void {
  globalLogger.logInfo(message);
}

export function logError(message: string): void {
  globalLogger.logError(message);
}

export function logDebug(message: string): void {
  globalLogger.logDebug(message);
}

export function logWarning(message: string): void {
  globalLogger.logWarning(message);
}

export function logException(error: Error): void {
  globalLogger.logException(error);
}
