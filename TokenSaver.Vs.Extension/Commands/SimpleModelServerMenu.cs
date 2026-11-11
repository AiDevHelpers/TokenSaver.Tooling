#pragma warning disable CEE0027 // Command and menu text is not localised in this release; tracked separately.
using System;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace TokenSaver.Vs.Extension.Commands;

/// <summary>
/// Groups all SimpleModelServer commands under a single "SimpleModelServer" submenu on the code editor's
/// right-click context menu, instead of 7 flat entries under Extensions. Parented to
/// IDG_VS_CODEWIN_ADVANCED — the standard group used to add custom items to the bottom of the
/// editor context menu (guidSHLMainMenu, see vsshlids.h).
/// </summary>
internal static class SimpleModelServerMenu
{
    private static readonly Guid ShellMainMenuGuid = new("{d309f791-903f-11d0-9efc-00a0c911004f}");
    private const uint CodeWindowAdvancedGroupId = 0x02D1;

    [VisualStudioContribution]
    public static MenuConfiguration Menu => new("SimpleModelServer")
    {
        Children =
        [
            MenuChild.Command<AskSimpleModelServerCommand>(),
            MenuChild.Command<ExplainCodeCommand>(),
            MenuChild.Command<FixIssuesCommand>(),
            MenuChild.Command<RefactorCodeCommand>(),
            MenuChild.Command<GenerateDocsCommand>(),
            MenuChild.Command<CodeReviewCommand>(),
            MenuChild.Command<GenerateTestsCommand>(),
        ],
        Placements = [CommandPlacement.VsctParent(ShellMainMenuGuid, CodeWindowAdvancedGroupId, priority: 0x0100)],
    };
}
