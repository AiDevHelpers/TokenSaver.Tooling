namespace TokenSaver.Vs.Extension.Services;

/// <summary>
/// Framework-agnostic DraftModel contract, mirroring the @tokensaver/draftmodel-provider TypeScript
/// package used by the VS Code extensions. See DraftModel repo's docs/router-contract.md.
/// </summary>
public enum DraftModelChatRole
{
    System,
    User,
    Assistant,
}

public sealed record DraftModelChatMessage(DraftModelChatRole Role, string Content);

/// <summary>One virtual model exposed by the router (id/name mirror `routerModelName`, e.g. "HelperModel").</summary>
public sealed record DraftModelInfo(string Id, string Name);

/// <summary>One streamed delta from a chat completion.</summary>
public sealed record DraftModelChatChunk(string ContentDelta, bool Done);

public enum DraftModelHealthStatus
{
    Ok,
    WarmingUp,
    Degraded,
    Unhealthy,
    Unreachable,
}

public sealed record DraftModelHealth(DraftModelHealthStatus Status);
