# @tokensaver/helpers

Shared TypeScript helpers for TokenSaver IDE extensions (VS Code, Cursor, Continue, etc.).

## Features

- **Configuration Management**: Read/write/merge behavior configs from local and global files
- **Health Checks**: Port availability, endpoint verification, polling utilities
- **Gitignore Management**: Check and manage `.tokensaver/` in project gitignore
- **Logging Abstractions**: Pluggable logger interface with in-memory and null loggers

## Installation

```bash
npm install @tokensaver/helpers
```

## Usage

### Logging

```typescript
import { setLogger, logInfo, DefaultLogger } from '@tokensaver/helpers';

const logger = new DefaultLogger();
setLogger(logger);

logInfo('TokenSaver initialized');
```

### Health Checks

```typescript
import { verifyMcpServer, waitForPortOpen } from '@tokensaver/helpers';

if (await verifyMcpServer(11436)) {
  console.log('MCP server is healthy');
}

if (await waitForPortOpen('127.0.0.1', 11436, 30000)) {
  console.log('Server is running');
}
```

### Configuration

```typescript
import { 
  getMergedRepoHelperConfig,
  updateConfigValue 
} from '@tokensaver/helpers';

const config = getMergedRepoHelperConfig(workspaceRoot);

updateConfigValue(workspaceRoot, 'local', (cfg) => ({
  ...cfg,
  repoHelper: {
    ...cfg.repoHelper,
    addTokenSaverToGitignore: true,
  }
}));
```

### Gitignore

```typescript
import { isTokenSaverInGitignore, addTokenSaverToGitignore } from '@tokensaver/helpers';

if (!isTokenSaverInGitignore(workspaceRoot)) {
  addTokenSaverToGitignore(workspaceRoot);
}
```

## Architecture

Modular design with separate concerns:
- `logging.ts` - Logger interface and implementations
- `health.ts` - Health check and polling utilities
- `config.ts` - Configuration file management
- `gitignore.ts` - .gitignore parsing and management

## Used By

- TokenSaver VS Code Extension
- TokenSaver Cursor Integration
- Other TokenSaver IDE extensions
