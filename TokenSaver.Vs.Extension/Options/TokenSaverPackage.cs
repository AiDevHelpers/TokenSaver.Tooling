using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.Shell;

namespace TokenSaver.Vs.Extension.Options;

/// <summary>
/// Classic (VSSDK) package providing the Tools &gt; Options &gt; TokenSaver &gt; General page. Only
/// exists because <see cref="ProvideOptionPageAttribute"/>/<see cref="DialogPage"/> have no
/// equivalent in the out-of-process VisualStudio.Extensibility SDK — see
/// <see cref="ExtensionEntrypoint"/> and TokenSaver.Vs.Extension.csproj's top comment for why this
/// extension now hosts in-process (VssdkCompatibleExtension/RequiresInProcessHosting) instead of
/// out-of-process.
/// </summary>
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(PackageGuidString)]
[ProvideOptionPage(typeof(TokenSaverOptionsPage), "TokenSaver", "General", 0, 0, supportsAutomation: true)]
public sealed class TokenSaverPackage : AsyncPackage
{
    public const string PackageGuidString = "34c83c12-40e0-4c22-bf7e-f6f2f2a16636";

    protected override Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress) =>
        Task.CompletedTask;
}
