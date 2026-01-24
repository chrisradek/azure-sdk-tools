namespace Azure.Sdk.Tools.Cli.CopilotAgents;

public class CopilotAgentValidationResult
{
    public required bool Success { get; set; }
    public object? Reason { get; set; }
}
