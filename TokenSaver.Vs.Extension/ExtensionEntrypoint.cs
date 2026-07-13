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
        // In-process (VSSDK-compatible) extensions must have a null Metadata: the manifest is the
        // static source.extension.vsixmanifest file instead of this source-generated one, because
        // in-proc hosting needs the classic Microsoft.VisualStudio.VsPackage asset entry (for the
        // ProvideOptionPageAttribute-based Tools > Options page) that the generated manifest has no
        // way to express. See source.extension.vsixmanifest for the id/display name/description/
        // installation-target metadata that used to live here.
        RequiresInProcessHosting = true,
    };

    /// <inheritdoc/>
    protected override void InitializeServices(IServiceCollection serviceCollection)
    {
        base.InitializeServices(serviceCollection);
    }
}
