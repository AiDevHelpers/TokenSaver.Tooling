using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TokenSaver.Vs.Extension.Services;

/// <summary>
/// Public surface used by the SimpleModelServer commands to talk to SimpleModelServer's router. Kept
/// independent of any Visual Studio SDK type so it can be unit tested in isolation.
/// </summary>
public interface ISimpleModelServerClient
{
    Task<SimpleModelServerHealth> CheckHealthAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SimpleModelServerInfo>> ListModelsAsync(CancellationToken cancellationToken = default);

    IAsyncEnumerable<SimpleModelServerChatChunk> StreamChatAsync(
        IReadOnlyList<SimpleModelServerChatMessage> messages,
        CancellationToken cancellationToken = default);

    int EstimateTokenCount(string text);
}
