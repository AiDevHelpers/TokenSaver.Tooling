# TokenSaver Visual Studio Extension

AI-native code indexing and context building for Visual Studio 2026.

## Building the VSIX

### Prerequisites

- Visual Studio 2026 (or Visual Studio Build Tools with SDK component)
- .NET 10.0 SDK
- PowerShell 7+

### Build Steps

From the repository root:

```powershell
# Build and package the VSIX
.\scripts\build-vs-extension-vsix.ps1

# Or with specific configuration
.\scripts\build-vs-extension-vsix.ps1 -Configuration Release

# Skip automatic installation
.\scripts\build-vs-extension-vsix.ps1 -NoInstall
```

The built VSIX will be output to `build/dist/TokenSaver.Vs.Extension.vsix`.

### Installing the VSIX

#### Via Command Line
```powershell
# Install into Visual Studio
vsixinstaller "build\dist\TokenSaver.Vs.Extension.vsix"

# Or from PowerShell
& "C:\Program Files\Microsoft Visual Studio\<version>\<edition>\Common7\IDE\VsixInstaller.exe" "build\dist\TokenSaver.Vs.Extension.vsix"
```

#### Via Visual Studio
1. Open Visual Studio
2. Go to **Extensions → Manage Extensions**
3. Click **Install from File...**
4. Select `TokenSaver.Vs.Extension.vsix`

### Development

The extension relies on:
- **TokenSaver.Client.Helpers** — configuration, health checks, service management
- **TokenSaver.Config** — workspace configuration
- Visual Studio SDK (included via NuGet)

Key classes:
- `TokenSaverExtension` — main extension lifecycle and commands
- Uses Windows Service integration for running TokenSaver.MCP as a background service

## Troubleshooting

### Build fails with "Visual Studio SDK not found"
- Ensure Visual Studio Build Tools or IDE is installed
- Install the Visual Studio SDK component if missing

### Extension doesn't load
- Check **Extensions → Manage Extensions** for error messages
- View the Activity Log: **View → Output → Show output from: Extension**

### MCP server connection issues
- Ensure TokenSaver.MCP Windows Service is installed and running
- Check Health Report via the extension's Tools menu
