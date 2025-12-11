// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Collections.Concurrent;
using Azure.Sdk.Tools.Cli.Models.Workflow;

namespace Azure.Sdk.Tools.Cli.Services;

/// <summary>
/// Interface for managing in-memory workflow state.
/// </summary>
public interface IWorkflowStateService
{
    /// <summary>
    /// Creates a new workflow and returns its ID.
    /// </summary>
    string CreateWorkflow(WorkflowState state);

    /// <summary>
    /// Gets a workflow by ID. Returns null if not found or already completed.
    /// </summary>
    WorkflowState? GetWorkflow(string workflowId);

    /// <summary>
    /// Updates an existing workflow's state.
    /// </summary>
    void UpdateWorkflow(string workflowId, WorkflowState state);

    /// <summary>
    /// Marks a workflow as complete, preventing further reuse.
    /// </summary>
    /// <returns>True if successfully completed; false if already completed.</returns>
    bool TryCompleteWorkflow(string workflowId);
}

/// <summary>
/// In-memory implementation of workflow state management.
/// </summary>
public class WorkflowStateService : IWorkflowStateService
{
    private readonly ConcurrentDictionary<string, WorkflowState> _activeWorkflows = new();
    private readonly HashSet<string> _completedWorkflowIds = new();
    private readonly object _lock = new();

    public string CreateWorkflow(WorkflowState state)
    {
        var workflowId = $"workflow-{Guid.NewGuid():N}";
        state.WorkflowId = workflowId;
        _activeWorkflows[workflowId] = state;
        return workflowId;
    }

    public WorkflowState? GetWorkflow(string workflowId)
    {
        // Check if already completed
        lock (_lock)
        {
            if (_completedWorkflowIds.Contains(workflowId))
            {
                return null; // Workflow already finished
            }
        }

        _activeWorkflows.TryGetValue(workflowId, out var state);
        return state;
    }

    public void UpdateWorkflow(string workflowId, WorkflowState state)
    {
        _activeWorkflows[workflowId] = state;
    }

    public bool TryCompleteWorkflow(string workflowId)
    {
        lock (_lock)
        {
            if (_completedWorkflowIds.Contains(workflowId))
            {
                return false;
            }
            _completedWorkflowIds.Add(workflowId);
        }
        _activeWorkflows.TryRemove(workflowId, out _);
        return true;
    }
}
