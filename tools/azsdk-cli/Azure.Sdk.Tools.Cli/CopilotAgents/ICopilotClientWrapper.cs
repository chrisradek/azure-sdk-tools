using GitHub.Copilot.SDK;
using Microsoft.Extensions.AI;

namespace Azure.Sdk.Tools.Cli.CopilotAgents;

/// <summary>
/// Interface wrapper for CopilotClient to enable unit testing.
/// </summary>
public interface ICopilotClientWrapper
{
    Task<ICopilotSessionWrapper> CreateSessionAsync(
        SessionConfig? config,
        ICollection<AIFunction> tools,
        CancellationToken cancellationToken = default);
}
