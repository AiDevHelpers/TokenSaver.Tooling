using System;

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace TokenSaver.Vs.Extension.Commands;

/// <summary>
/// Top-level "TokenSaver" submenu under Visual Studio's main "Extensions" menu, holding
/// extension-wide commands (Settings, chat) that aren't specific to a code selection — unlike the
/// per-selection "SimpleModelServer" submenu on the editor context menu (see <see cref="SimpleModelServerMenu"/>).
/// Parented to IDG_VS_EXTENSIONS (0x6000) — the standard group for third-party entries directly
/// under the Extensions menu (guidSHLMainMenu, see vsshlids.h).
/// </summary>
internal static class TokenSaverMenu
{
    private static readonly Guid ShellMainMenuGuid = new("{d309f791-903f-11d0-9efc-00a0c911004f}");
    private const uint ExtensionsMenuGroupId = 0x6000;

    [VisualStudioContribution]
    public static MenuConfiguration Menu => new("TokenSaver")
    {
        Children =
        [
            MenuChild.Command<OpenSettingsCommand>(),
            MenuChild.Command<OpenChatCommand>(),
            MenuChild.Command<StartServerCommand>(),
            MenuChild.Command<StopServerCommand>(),
            MenuChild.Command<RestartServerCommand>(),
            MenuChild.Command<ShowServerStatusCommand>(),
            MenuChild.Command<ShowLogsCommand>(),
            MenuChild.Command<FullSetupCommand>(),
            MenuChild.Command<InstallPromptCommand>(),
            MenuChild.Command<InstallMcpServiceCommand>(),
            MenuChild.Command<UninstallMcpServiceCommand>(),
            MenuChild.Command<InstallSimpleModelServerCommand>(),
            MenuChild.Command<UninstallSimpleModelServerCommand>(),
        ],
        Placements = [CommandPlacement.VsctParent(ShellMainMenuGuid, ExtensionsMenuGroupId, priority: 0x0100)],
    };
}
