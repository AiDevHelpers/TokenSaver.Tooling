namespace TokenSaver.Vs.Extension.Services;

/// <summary>
/// Framework-agnostic SimpleModelServer contract, mirroring the @tokensaver/simpleModelServer-provider TypeScript
/// package used by the VS Code extensions. See SimpleModelServer repo's docs/router-contract.md.
/// </summary>
public enum SimpleModelServerChatRole
{
    System,
    User,
    Assistant,
}

public sealed record SimpleModelServerChatMessage(SimpleModelServerChatRole Role, string Content);

/// <summary>One virtual model exposed by the router (id/name mirror `routerModelName`, e.g. "HelperModel").</summary>
public sealed record SimpleModelServerInfo(string Id, string Name);

/// <summary>One streamed delta from a chat completion.</summary>
public sealed record SimpleModelServerChatChunk(string ContentDelta, bool Done);

public enum SimpleModelServerHealthStatus
{
    Ok,
    WarmingUp,
    Degraded,
    Unhealthy,
    Unreachable,
}

public sealed record SimpleModelServerHealth(SimpleModelServerHealthStatus Status);
