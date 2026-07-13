using TokenSaver.Vs.Extension.Services;
using Xunit;

namespace TokenSaver.Vs.Extension.Tests.Services;

public sealed class DraftModelPromptBuilderTests
{
    [Fact]
    public void BuildMessages_Chat_SendsCodeAsRawUserMessage()
    {
        var messages = DraftModelPromptBuilder.BuildMessages(DraftModelAction.Chat, "how does this work?");

        Assert.Equal(2, messages.Count);
        Assert.Equal(DraftModelChatRole.System, messages[0].Role);
        Assert.Equal(DraftModelChatRole.User, messages[1].Role);
        Assert.Equal("how does this work?", messages[1].Content);
    }

    [Theory]
    [InlineData(DraftModelAction.Explain)]
    [InlineData(DraftModelAction.Fix)]
    [InlineData(DraftModelAction.Refactor)]
    [InlineData(DraftModelAction.GenerateDocs)]
    [InlineData(DraftModelAction.CodeReview)]
    [InlineData(DraftModelAction.GenerateTests)]
    public void BuildMessages_CodeActions_WrapCodeInFencedBlockWithLanguage(DraftModelAction action)
    {
        var messages = DraftModelPromptBuilder.BuildMessages(action, "int x = 1;", languageId: "csharp");

        Assert.Equal("```csharp\nint x = 1;\n```", messages[1].Content);
        Assert.Contains("csharp", messages[0].Content);
    }

    [Fact]
    public void BuildMessages_DefaultsLanguageToCode_WhenNotProvided()
    {
        var messages = DraftModelPromptBuilder.BuildMessages(DraftModelAction.Explain, "x = 1");
        Assert.Equal("```code\nx = 1\n```", messages[1].Content);
    }

    [Fact]
    public void BuildMessages_DistinctActionsProduceDistinctSystemPrompts()
    {
        var explain = DraftModelPromptBuilder.BuildMessages(DraftModelAction.Explain, "x");
        var review = DraftModelPromptBuilder.BuildMessages(DraftModelAction.CodeReview, "x");

        Assert.NotEqual(explain[0].Content, review[0].Content);
    }
}
