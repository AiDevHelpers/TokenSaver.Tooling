using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using TokenSaver.Vs.Extension.Services;

namespace TokenSaver.Vs.Extension.Commands;

// Each command below is surfaced only as a child of the "DraftModel" submenu (see
// DraftModelMenu.cs) on the code editor's right-click context menu — no direct Placements here.

[VisualStudioContribution]
public sealed class AskDraftModelCommand(VisualStudioExtensibility extensibility) : DraftModelCommandBase(extensibility)
{
    protected override DraftModelAction Action => DraftModelAction.Chat;

    public override CommandConfiguration CommandConfiguration => new("Ask about Selection");
}

[VisualStudioContribution]
public sealed class ExplainCodeCommand(VisualStudioExtensibility extensibility) : DraftModelCommandBase(extensibility)
{
    protected override DraftModelAction Action => DraftModelAction.Explain;

    public override CommandConfiguration CommandConfiguration => new("Explain Code");
}

[VisualStudioContribution]
public sealed class FixIssuesCommand(VisualStudioExtensibility extensibility) : DraftModelCommandBase(extensibility)
{
    protected override DraftModelAction Action => DraftModelAction.Fix;

    public override CommandConfiguration CommandConfiguration => new("Fix Issues");
}

[VisualStudioContribution]
public sealed class RefactorCodeCommand(VisualStudioExtensibility extensibility) : DraftModelCommandBase(extensibility)
{
    protected override DraftModelAction Action => DraftModelAction.Refactor;

    public override CommandConfiguration CommandConfiguration => new("Refactor Code");
}

[VisualStudioContribution]
public sealed class GenerateDocsCommand(VisualStudioExtensibility extensibility) : DraftModelCommandBase(extensibility)
{
    protected override DraftModelAction Action => DraftModelAction.GenerateDocs;

    public override CommandConfiguration CommandConfiguration => new("Generate Docs");
}

[VisualStudioContribution]
public sealed class CodeReviewCommand(VisualStudioExtensibility extensibility) : DraftModelCommandBase(extensibility)
{
    protected override DraftModelAction Action => DraftModelAction.CodeReview;

    public override CommandConfiguration CommandConfiguration => new("Code Review");
}

[VisualStudioContribution]
public sealed class GenerateTestsCommand(VisualStudioExtensibility extensibility) : DraftModelCommandBase(extensibility)
{
    protected override DraftModelAction Action => DraftModelAction.GenerateTests;

    public override CommandConfiguration CommandConfiguration => new("Generate Unit Tests");
}
