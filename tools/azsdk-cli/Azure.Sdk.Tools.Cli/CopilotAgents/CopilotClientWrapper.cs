using GitHub.Copilot.SDK;
using Microsoft.Extensions.AI;

namespace Azure.Sdk.Tools.Cli.CopilotAgents;

/// <summary>
/// Production wrapper that delegates to the actual CopilotClient.
/// </summary>
public class CopilotClientWrapper : ICopilotClientWrapper
{
    private readonly CopilotClient _client;

    public CopilotClientWrapper(CopilotClient client)
    {
        _client = client;
    }

    public async Task<ICopilotSessionWrapper> CreateSessionAsync(
        SessionConfig? config,
        ICollection<AIFunction> tools,
        CancellationToken cancellationToken = default)
    {
        // Merge tools into config
        var configWithTools = config ?? new SessionConfig();
        configWithTools = new SessionConfig
        {
            Model = configWithTools.Model,
            Tools = tools,
            SystemMessage = configWithTools.SystemMessage,
            SessionId = configWithTools.SessionId,
            AvailableTools = configWithTools.AvailableTools,
            ExcludedTools = configWithTools.ExcludedTools,
            Provider = configWithTools.Provider
        };
        
        var session = await _client.CreateSessionAsync(configWithTools, cancellationToken);
        return new CopilotSessionWrapper(session);
    }
}
