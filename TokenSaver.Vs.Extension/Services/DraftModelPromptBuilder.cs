using System;
using System.Collections.Generic;

namespace TokenSaver.Vs.Extension.Services;

/// <summary>
/// The Copilot-equivalent actions DraftModel is wired up for in Visual Studio. Visual Studio has
/// no public SDK hook to register a custom Copilot model provider (unlike VS Code's
/// languageModelChatProviders), so each of these is its own command against DraftModel directly.
/// </summary>
public enum DraftModelAction
{
    Chat,
    Explain,
    Fix,
    Refactor,
    GenerateDocs,
    CodeReview,
    GenerateTests,
}

/// <summary>Builds the system/user message pair sent to DraftModel for a given action. Pure string
/// logic, no Visual Studio SDK dependency, so it's fully unit testable.</summary>
public static class DraftModelPromptBuilder
{
    public static IReadOnlyList<DraftModelChatMessage> BuildMessages(
        DraftModelAction action,
        string code,
        string? languageId = null)
    {
        ArgumentNullException.ThrowIfNull(code);

        var language = string.IsNullOrWhiteSpace(languageId) ? "code" : languageId;
        var systemPrompt = SystemPromptFor(action, language);
        var userPrompt = action == DraftModelAction.Chat
            ? code
            : $"```{language}\n{code}\n```";

        return
        [
            new DraftModelChatMessage(DraftModelChatRole.System, systemPrompt),
            new DraftModelChatMessage(DraftModelChatRole.User, userPrompt),
        ];
    }

    private static string SystemPromptFor(DraftModelAction action, string language) => action switch
    {
        DraftModelAction.Chat =>
            "You are a helpful pair-programming assistant. Answer the developer's question concisely.",
        DraftModelAction.Explain =>
            $"Explain what the following {language} code does. Be concise and focus on intent, not a line-by-line narration.",
        DraftModelAction.Fix =>
            $"Find and fix bugs in the following {language} code. Return the corrected code and a short explanation of what was wrong.",
        DraftModelAction.Refactor =>
            $"Refactor the following {language} code for clarity and simplicity without changing its behavior. Return the refactored code and a short explanation of the changes.",
        DraftModelAction.GenerateDocs =>
            $"Write documentation comments for the following {language} code, following that language's doc-comment conventions.",
        DraftModelAction.CodeReview =>
            $"Perform a code review of the following {language} code. List concrete issues (correctness, security, readability) ranked by severity.",
        DraftModelAction.GenerateTests =>
            $"Write unit tests for the following {language} code, covering the main behavior and edge cases.",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };
}
