using System.ComponentModel;
using System.Text.Json;
using Azure.Sdk.Tools.Cli.Helpers;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Azure.Sdk.Tools.Cli.CopilotAgents;

public class CopilotAgentRunner : ICopilotAgentRunner
{
    private readonly ICopilotClientWrapper _client;
    private readonly TokenUsageHelper _tokenUsageHelper;
    private readonly ConversationLogger _conversationLogger;
    private readonly ILogger<CopilotAgentRunner> _logger;

    public CopilotAgentRunner(
        ICopilotClientWrapper client,
        TokenUsageHelper tokenUsageHelper,
        ConversationLogger conversationLogger,
        ILogger<CopilotAgentRunner> logger)
    {
        _client = client;
        _tokenUsageHelper = tokenUsageHelper;
        _conversationLogger = conversationLogger;
        _logger = logger;
    }

    public async Task<TResult> RunAsync<TResult>(
        CopilotAgent<TResult> agent,
        CancellationToken ct = default) where TResult : notnull
    {
        // Collect tools + Exit tool
        var tools = agent.Tools.ToList();

        TResult? capturedResult = default;
        tools.Add(AIFunctionFactory.Create(
            ([Description("The result of the agent run. Output the result requested exactly, without additional padding, explanation, or code fences unless requested.")]
             TResult result) =>
            {
                capturedResult = result;
                return "Exiting with result";
            },
            "Exit",
            "Call this tool when you are finished with the work or are otherwise unable to continue."));

        // Create session with config and tools
        var sessionConfig = new SessionConfig
        {
            Model = agent.Model,
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Append,
                Content = agent.Instructions
            }
        };
        
        await using var session = await _client.CreateSessionAsync(sessionConfig, tools, ct);

        // Subscribe to events for token tracking BEFORE sending messages
        // Events are dispatched during SendAsync, so handlers must be registered first
        using var eventSubscription = session.On(evt =>
        {
            _logger.LogDebug("Received session event: {EventType}", evt.GetType().Name);
            switch (evt)
            {
                case AssistantUsageEvent usage:
                    _tokenUsageHelper.Add(
                        usage.Data.Model ?? agent.Model,
                        (int)(usage.Data.InputTokens ?? 0),
                        (int)(usage.Data.OutputTokens ?? 0));
                    break;
                case AssistantMessageEvent msg:
                    _logger.LogDebug("Assistant message: {Content}", msg.Data.Content?.Substring(0, Math.Min(100, msg.Data.Content?.Length ?? 0)));
                    break;
                case ToolExecutionStartEvent toolStart:
                    _logger.LogDebug("Tool execution started: {ToolName}", toolStart.Data.ToolName);
                    break;
                case ToolExecutionCompleteEvent toolComplete:
                    _logger.LogDebug("Tool execution completed: {ToolCallId} success={Success}", toolComplete.Data.ToolCallId, toolComplete.Data.Success);
                    break;
                case SessionErrorEvent error:
                    _logger.LogError("Session error: {ErrorType} - {Message}", error.Data.ErrorType, error.Data.Message);
                    break;
            }
        });

        // Validation retry loop
        var prompt = "Begin the task. Call tools as needed, then call Exit with the result.";
        var iterations = 0;

        while (iterations < agent.MaxIterations)
        {
            iterations++;
            capturedResult = default;

            _logger.LogDebug("Sending message iteration {Iteration}", iterations);
            
            // SendAsync blocks until the session is idle (all tool calls completed)
            // The Exit tool will be invoked during this call, setting capturedResult
            await session.SendAsync(new MessageOptions { Prompt = prompt }, ct);
            
            _logger.LogDebug("Message completed, capturedResult is {HasResult}", capturedResult != null ? "set" : "null");

            // Check if Exit was called
            if (capturedResult == null)
            {
                throw new InvalidOperationException("Agent completed without calling Exit tool");
            }

            // Validate result if validator provided
            if (agent.ValidateResult != null)
            {
                var validation = await agent.ValidateResult(capturedResult);
                if (!validation.Success)
                {
                    var reason = validation.Reason is string str
                        ? str
                        : JsonSerializer.Serialize(validation.Reason);
                    _logger.LogWarning("Agent result failed validation: {Reason}. Retrying.", reason);
                    prompt = $"The result you provided did not pass validation: {reason}. Please try again.";
                    continue;
                }
            }

            // Success
            return capturedResult;
        }

        throw new InvalidOperationException(
            $"Agent did not return a valid result within {agent.MaxIterations} iterations");
    }
}
