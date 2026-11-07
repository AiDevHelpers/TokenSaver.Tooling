using System;
using System.Collections.Generic;

namespace TokenSaver.Vs.Extension.Services;

/// <summary>
/// The Copilot-equivalent actions SimpleModelServer is wired up for in Visual Studio. Visual Studio has
/// no public SDK hook to register a custom Copilot model provider (unlike VS Code's
/// languageModelChatProviders), so each of these is its own command against SimpleModelServer directly.
/// </summary>
public enum SimpleModelServerAction
{
    Chat,
    Explain,
    Fix,
    Refactor,
    GenerateDocs,
    CodeReview,
    GenerateTests,
}

/// <summary>Builds the system/user message pair sent to SimpleModelServer for a given action. Pure string
/// logic, no Visual Studio SDK dependency, so it's fully unit testable.</summary>
public static class SimpleModelServerPromptBuilder
{
    public static IReadOnlyList<SimpleModelServerChatMessage> BuildMessages(
        SimpleModelServerAction action,
        string code,
        string? languageId = null)
    {
        ArgumentNullException.ThrowIfNull(code);

        var language = string.IsNullOrWhiteSpace(languageId) ? "code" : languageId;
        var systemPrompt = SystemPromptFor(action, language);
        var userPrompt = action == SimpleModelServerAction.Chat
            ? code
            : $"```{language}\n{code}\n```";

        return
        [
            new SimpleModelServerChatMessage(SimpleModelServerChatRole.System, systemPrompt),
            new SimpleModelServerChatMessage(SimpleModelServerChatRole.User, userPrompt),
        ];
    }

    private static string SystemPromptFor(SimpleModelServerAction action, string language) => action switch
    {
        SimpleModelServerAction.Chat =>
            "You are a helpful pair-programming assistant. Answer the developer's question concisely.",
        SimpleModelServerAction.Explain =>
            $"Explain what the following {language} code does. Be concise and focus on intent, not a line-by-line narration.",
        SimpleModelServerAction.Fix =>
            $"Find and fix bugs in the following {language} code. Return the corrected code and a short explanation of what was wrong.",
        SimpleModelServerAction.Refactor =>
            $"Refactor the following {language} code for clarity and simplicity without changing its behavior. Return the refactored code and a short explanation of the changes.",
        SimpleModelServerAction.GenerateDocs =>
            $"Write documentation comments for the following {language} code, following that language's doc-comment conventions.",
        SimpleModelServerAction.CodeReview =>
            $"Perform a code review of the following {language} code. List concrete issues (correctness, security, readability) ranked by severity.",
        SimpleModelServerAction.GenerateTests =>
            $"Write unit tests for the following {language} code, covering the main behavior and edge cases.",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };
}
