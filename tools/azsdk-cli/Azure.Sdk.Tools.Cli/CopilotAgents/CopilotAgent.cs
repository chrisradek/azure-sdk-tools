using Microsoft.Extensions.AI;

namespace Azure.Sdk.Tools.Cli.CopilotAgents;

public class CopilotAgent<TResult> where TResult : notnull
{
    public required string Instructions { get; init; }
    public IEnumerable<AIFunction> Tools { get; init; } = [];
    public string Model { get; init; } = "gpt-5";
    public int MaxIterations { get; init; } = 100;
    public Func<TResult, Task<CopilotAgentValidationResult>>? ValidateResult { get; init; }
}
