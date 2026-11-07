using TokenSaver.Vs.Extension.Services;
using Xunit;

namespace TokenSaver.Vs.Extension.Tests.Services;

public sealed class SimpleModelServerPromptBuilderTests
{
    [Fact]
    public void BuildMessages_Chat_SendsCodeAsRawUserMessage()
    {
        var messages = SimpleModelServerPromptBuilder.BuildMessages(SimpleModelServerAction.Chat, "how does this work?");

        Assert.Equal(2, messages.Count);
        Assert.Equal(SimpleModelServerChatRole.System, messages[0].Role);
        Assert.Equal(SimpleModelServerChatRole.User, messages[1].Role);
        Assert.Equal("how does this work?", messages[1].Content);
    }

    [Theory]
    [InlineData(SimpleModelServerAction.Explain)]
    [InlineData(SimpleModelServerAction.Fix)]
    [InlineData(SimpleModelServerAction.Refactor)]
    [InlineData(SimpleModelServerAction.GenerateDocs)]
    [InlineData(SimpleModelServerAction.CodeReview)]
    [InlineData(SimpleModelServerAction.GenerateTests)]
    public void BuildMessages_CodeActions_WrapCodeInFencedBlockWithLanguage(SimpleModelServerAction action)
    {
        var messages = SimpleModelServerPromptBuilder.BuildMessages(action, "int x = 1;", languageId: "csharp");

        Assert.Equal("```csharp\nint x = 1;\n```", messages[1].Content);
        Assert.Contains("csharp", messages[0].Content);
    }

    [Fact]
    public void BuildMessages_DefaultsLanguageToCode_WhenNotProvided()
    {
        var messages = SimpleModelServerPromptBuilder.BuildMessages(SimpleModelServerAction.Explain, "x = 1");
        Assert.Equal("```code\nx = 1\n```", messages[1].Content);
    }

    [Fact]
    public void BuildMessages_DistinctActionsProduceDistinctSystemPrompts()
    {
        var explain = SimpleModelServerPromptBuilder.BuildMessages(SimpleModelServerAction.Explain, "x");
        var review = SimpleModelServerPromptBuilder.BuildMessages(SimpleModelServerAction.CodeReview, "x");

        Assert.NotEqual(explain[0].Content, review[0].Content);
    }
}
