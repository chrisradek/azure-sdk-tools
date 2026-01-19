// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Text.Json.Serialization;
using Azure.Sdk.Tools.Cli.Models;

namespace Azure.Sdk.Tools.Cli.Models.Workflow;

/// <summary>
/// Response from the workflow tool containing next steps for Copilot.
/// </summary>
public class WorkflowResponse : CommandResponse
{
    [JsonPropertyName("phase")]
    public WorkflowPhase Phase { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty; // For user display

    [JsonPropertyName("instruction")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Instruction { get; set; } // What Copilot should do next

    // Expected result format for the next workflow step
    [JsonPropertyName("expected_result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ExpectedResult? ExpectedResult { get; set; }

    // Optional: suggest using a subagent (for TSP fixes)
    [JsonPropertyName("use_subagent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SubagentSuggestion? UseSubagent { get; set; }

    // Optional: run a specific tool
    [JsonPropertyName("run_tool")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ToolSuggestion? RunTool { get; set; }

    // Workflow ID for next call (simple string, not encoded state)
    [JsonPropertyName("workflow_id")]
    public string WorkflowId { get; set; } = string.Empty;

    // Terminal state info
    [JsonPropertyName("is_complete")]
    public bool IsComplete { get; set; }

    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; set; } // "success" | "failure"

    [JsonPropertyName("summary")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Summary { get; set; }

    /// <summary>
    /// When true, the agent MUST call this workflow tool again after completing the current step.
    /// The workflow cannot proceed without this callback.
    /// </summary>
    [JsonPropertyName("continuation_required")]
    public bool ContinuationRequired { get; set; }

    /// <summary>
    /// Explicit instruction for how to continue the workflow. This should be followed exactly.
    /// </summary>
    [JsonPropertyName("continuation_instruction")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContinuationInstruction { get; set; }

    /// <summary>
    /// Progress tracking to show how far along the workflow is.
    /// </summary>
    [JsonPropertyName("progress")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkflowProgress? Progress { get; set; }

    protected override string Format()
    {
        var progress = Progress != null ? $" (Step {Progress.CurrentStep}/{Progress.TotalSteps})" : "";
        return $"[{Phase}]{progress} {Message}";
    }
}

/// <summary>
/// Tracks workflow progress to make it clear the workflow is not complete.
/// </summary>
public class WorkflowProgress
{
    [JsonPropertyName("current_step")]
    public int CurrentStep { get; set; }

    [JsonPropertyName("total_steps")]
    public int TotalSteps { get; set; }

    [JsonPropertyName("completed_steps")]
    public List<string> CompletedSteps { get; set; } = new();

    [JsonPropertyName("remaining_steps")]
    public List<string> RemainingSteps { get; set; } = new();
}

/// <summary>
/// Suggestion to use a subagent for a specific task.
/// </summary>
public class SubagentSuggestion
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // "typespec"

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;
}

/// <summary>
/// Suggestion to run a specific tool.
/// </summary>
public class ToolSuggestion
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty; // "azsdk_package_generate", "azsdk_package_build", "azsdk_sdk_fix"

    [JsonPropertyName("args")]
    public Dictionary<string, string> Args { get; set; } = new();
}

/// <summary>
/// Describes the expected result format for the workflow to continue.
/// This helps Copilot understand exactly what JSON structure to provide.
/// </summary>
public class ExpectedResult
{
    /// <summary>
    /// The expected "type" field value for the result.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Description of what this result represents.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The fields expected in the result, with descriptions.
    /// Key is field name, value is description of expected value.
    /// </summary>
    [JsonPropertyName("fields")]
    public Dictionary<string, string> Fields { get; set; } = new();

    /// <summary>
    /// Example JSON that Copilot should produce.
    /// </summary>
    [JsonPropertyName("example")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Example { get; set; }
}
