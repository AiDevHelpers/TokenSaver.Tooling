#pragma warning disable CEE0027 // Command and menu text is not localised in this release; tracked separately.
using System;

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace TokenSaver.Vs.Extension.Commands;

/// <summary>
/// Top-level "TokenSaver" submenu under Visual Studio's main "Extensions" menu: chat, plus one entry per tab of
/// the TokenSaver tool window (Settings, Services, Logs). Service and setup actions are buttons on the Services
/// tab, not menu entries. The per-selection commands are on the editor context menu (see <see cref="SimpleModelServerMenu"/>).
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
            MenuChild.Command<OpenChatCommand>(),
            MenuChild.Command<OpenSettingsCommand>(),
            MenuChild.Command<OpenServicesCommand>(),
            MenuChild.Command<OpenLogsCommand>(),
        ],
        Placements = [CommandPlacement.VsctParent(ShellMainMenuGuid, ExtensionsMenuGroupId, priority: 0x0100)],
    };
}
