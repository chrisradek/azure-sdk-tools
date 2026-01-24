namespace Azure.Sdk.Tools.Cli.CopilotAgents;

public interface ICopilotAgentRunner
{
    Task<TResult> RunAsync<TResult>(CopilotAgent<TResult> agent, CancellationToken ct = default)
        where TResult : notnull;
}
