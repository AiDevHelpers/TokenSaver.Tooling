using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TokenSaver.Vs.Extension.Services;

/// <summary>
/// Public surface used by the DraftModel commands to talk to DraftModel.Loader's router. Kept
/// independent of any Visual Studio SDK type so it can be unit tested in isolation.
/// </summary>
public interface IDraftModelClient
{
    Task<DraftModelHealth> CheckHealthAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DraftModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default);

    IAsyncEnumerable<DraftModelChatChunk> StreamChatAsync(
        IReadOnlyList<DraftModelChatMessage> messages,
        CancellationToken cancellationToken = default);

    int EstimateTokenCount(string text);
}
