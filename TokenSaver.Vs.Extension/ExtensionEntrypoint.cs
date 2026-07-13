using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Extensibility;

namespace TokenSaver.Vs.Extension;

/// <summary>
/// Entry point for the TokenSaver Visual Studio 2026 extension.
///
/// This is the out-of-process VisualStudio.Extensibility <see cref="Extension"/> instance Visual
/// Studio creates when the extension loads. The <see cref="ExtensionConfiguration"/> metadata below
/// is read by the build-time source generators to produce the VSIX manifest, so Visual Studio can
/// display and install the extension without loading this assembly.
/// </summary>
[VisualStudioContribution]
internal sealed class ExtensionEntrypoint : global::Microsoft.VisualStudio.Extensibility.Extension
{
    /// <inheritdoc/>
    public override ExtensionConfiguration ExtensionConfiguration => new()
    {
        Metadata = new(
            id: "TokenSaver.Vs.Extension",
            // this.ExtensionAssemblyVersion reflects AssemblyVersion. Nerdbank.GitVersioning
            // normally pins that at 1.0.0.0 for binding stability, which made every build look
            // identical to the VSIX installer (a "modify --vsix" of the same version is a no-op).
            // The project-local version.json overrides assemblyVersion.precision to "build", so
            // AssemblyVersion here tracks git height like FileVersion does elsewhere in the repo.
            version: this.ExtensionAssemblyVersion,
            publisherName: "TokenSaver",
            displayName: "TokenSaver",
            description: "AI-native code indexing and context building for Visual Studio 2026. Manages the TokenSaver.MCP server (start/stop the Windows service and check its health) from the Extensions menu.")
        {
            // Include VS 2026 (18.x). Without this the default would be [17.14, 18.0), excluding 2026.
            InstallationTargetVersion = "[17.14,)",

            // We build against net8.0; declare it so Visual Studio hosts the extension on .NET 8
            // rather than treating a mismatch as best-effort.
            DotnetTargetVersions = [DotnetTarget.Net8],

            MoreInfo = "https://github.com/rsperry79/TokenSaver",
        },
    };

    /// <inheritdoc/>
    protected override void InitializeServices(IServiceCollection serviceCollection)
    {
        base.InitializeServices(serviceCollection);
    }
}
