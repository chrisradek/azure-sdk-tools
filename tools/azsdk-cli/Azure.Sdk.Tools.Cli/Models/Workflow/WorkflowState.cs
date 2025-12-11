// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.Sdk.Tools.Cli.Models.Workflow;

/// <summary>
/// State data structure for tracking SDK customization workflow progress.
/// </summary>
public class WorkflowState
{
    public string WorkflowId { get; set; } = string.Empty;
    public WorkflowPhase Phase { get; set; }

    // Entry context
    public string EntryType { get; set; } = string.Empty; // "build_error" | "user_request"
    public string OriginalRequest { get; set; } = string.Empty;

    // Current errors (updated after each build)
    public string CurrentErrors { get; set; } = string.Empty;

    // Track what's been tried
    public string? LastFixType { get; set; } // "tsp" | "sdk" | null

    // Iteration tracking
    public int Iteration { get; set; }
    public int MaxIterations { get; set; }

    // Paths
    public string PackagePath { get; set; } = string.Empty;
    public string TypeSpecPath { get; set; } = string.Empty;

    // History (for debugging/summary)
    public List<WorkflowHistoryEntry> History { get; set; } = new();
}

/// <summary>
/// Entry in workflow history for tracking actions taken.
/// </summary>
public class WorkflowHistoryEntry
{
    public int Iteration { get; set; }
    public WorkflowPhase Phase { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty; // "success" | "failure" | "skipped"
    public string? Details { get; set; }
}

/// <summary>
/// Workflow phases for SDK customization workflow.
/// </summary>
public enum WorkflowPhase
{
    Start,
    Classify,
    AttemptTspFix,
    Regenerate,
    AttemptSdkFix,
    Build,
    Success,
    Failure
}
