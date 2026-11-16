#pragma warning disable CEE0027 // Command and menu text is not localised in this release; tracked separately.
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using TokenSaver.Vs.Extension.Services;

namespace TokenSaver.Vs.Extension.Commands;

// Each command below is surfaced only as a child of the "SimpleModelServer" submenu (see
// SimpleModelServerMenu.cs) on the code editor's right-click context menu — no direct Placements here.

[VisualStudioContribution]
public sealed class AskSimpleModelServerCommand(VisualStudioExtensibility extensibility) : SimpleModelServerCommandBase(extensibility)
{
    protected override SimpleModelServerAction Action => SimpleModelServerAction.Chat;

    public override CommandConfiguration CommandConfiguration => new("Ask about Selection");
}

[VisualStudioContribution]
public sealed class ExplainCodeCommand(VisualStudioExtensibility extensibility) : SimpleModelServerCommandBase(extensibility)
{
    protected override SimpleModelServerAction Action => SimpleModelServerAction.Explain;

    public override CommandConfiguration CommandConfiguration => new("Explain Code");
}

[VisualStudioContribution]
public sealed class FixIssuesCommand(VisualStudioExtensibility extensibility) : SimpleModelServerCommandBase(extensibility)
{
    protected override SimpleModelServerAction Action => SimpleModelServerAction.Fix;

    public override CommandConfiguration CommandConfiguration => new("Fix Issues");
}

[VisualStudioContribution]
public sealed class RefactorCodeCommand(VisualStudioExtensibility extensibility) : SimpleModelServerCommandBase(extensibility)
{
    protected override SimpleModelServerAction Action => SimpleModelServerAction.Refactor;

    public override CommandConfiguration CommandConfiguration => new("Refactor Code");
}

[VisualStudioContribution]
public sealed class GenerateDocsCommand(VisualStudioExtensibility extensibility) : SimpleModelServerCommandBase(extensibility)
{
    protected override SimpleModelServerAction Action => SimpleModelServerAction.GenerateDocs;

    public override CommandConfiguration CommandConfiguration => new("Generate Docs");
}

[VisualStudioContribution]
public sealed class CodeReviewCommand(VisualStudioExtensibility extensibility) : SimpleModelServerCommandBase(extensibility)
{
    protected override SimpleModelServerAction Action => SimpleModelServerAction.CodeReview;

    public override CommandConfiguration CommandConfiguration => new("Code Review");
}

[VisualStudioContribution]
public sealed class GenerateTestsCommand(VisualStudioExtensibility extensibility) : SimpleModelServerCommandBase(extensibility)
{
    protected override SimpleModelServerAction Action => SimpleModelServerAction.GenerateTests;

    public override CommandConfiguration CommandConfiguration => new("Generate Unit Tests");
}
