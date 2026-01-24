using GitHub.Copilot.SDK;

namespace Azure.Sdk.Tools.Cli.CopilotAgents;

/// <summary>
/// Production wrapper that delegates to the actual CopilotSession.
/// </summary>
public class CopilotSessionWrapper : ICopilotSessionWrapper
{
    private readonly CopilotSession _session;

    public CopilotSessionWrapper(CopilotSession session)
    {
        _session = session;
    }

    public IDisposable On(SessionEventHandler handler)
    {
        return _session.On(handler);
    }

    public Task<string> SendAsync(MessageOptions options, CancellationToken cancellationToken = default)
    {
        return _session.SendAsync(options, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return _session.DisposeAsync();
    }
}
