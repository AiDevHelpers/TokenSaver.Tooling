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
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by the Visual Studio extension host via reflection.")]
internal sealed class ExtensionEntrypoint : global::Microsoft.VisualStudio.Extensibility.Extension
{
    /// <inheritdoc/>
    public override ExtensionConfiguration ExtensionConfiguration => new()
    {
        RequiresInProcessHosting = false,
        Metadata = new(
            id: "TokenSaver.Vs.Extension",
            version: new Version(1, 0, 0),
            publisherName: "TokenSaver",
            displayName: "TokenSaver",
            description: "AI-native code indexing and context building for Visual Studio 2026. Manages the TokenSaver.MCP server (start/stop the Windows service and check its health) from the Extensions menu.")
        {
            MoreInfo = "https://github.com/rsperry79/TokenSaver",
            ReleaseNotes = "https://github.com/rsperry79/TokenSaver/releases",
        },
    };

    /// <inheritdoc/>
    protected override void InitializeServices(IServiceCollection serviceCollection)
    {
        base.InitializeServices(serviceCollection);

        // Shared by the tool window and the commands that open it on a given tab.
        serviceCollection.AddSingleton<Pages.TokenSaverWindowViewModel>();
    }
}
