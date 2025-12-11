// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.CommandLine;
using System.ComponentModel;
using System.Text.Json;
using Azure.Sdk.Tools.Cli.Commands;
using Azure.Sdk.Tools.Cli.Models;
using Azure.Sdk.Tools.Cli.Models.Workflow;
using Azure.Sdk.Tools.Cli.Services;
using ModelContextProtocol.Server;

namespace Azure.Sdk.Tools.Cli.Tools.Workflow;

/// <summary>
/// Coordinates SDK customization workflow for fixing build errors and applying customizations.
/// This tool acts as a pure state machine - it tracks state and returns instructions for Copilot.
/// </summary>
[McpServerToolType, Description("Coordinates SDK customization workflow for fixing build errors and applying customizations")]
public class SdkCustomizationWorkflowTool(
    ILogger<SdkCustomizationWorkflowTool> logger,
    IWorkflowStateService workflowStateService
) : MCPTool
{
    private const string ToolName = "azsdk_sdk_customization_workflow";

    public override CommandGroup[] CommandHierarchy { get; set; } = [SharedCommandGroups.Package];

    // CLI options for start mode
    private readonly Option<string> requestOption = new("--request", "-r")
    {
        Description = "Build errors or user request (required for new workflow)"
    };

    private readonly Option<string> requestTypeOption = new("--request-type")
    {
        Description = "Type of request: 'build_error' or 'user_request' (required for new workflow)"
    };

    private readonly Option<string> typeSpecPathOption = new("--typespec-path")
    {
        Description = "Absolute path to TypeSpec project (optional)"
    };

    private readonly Option<int> maxIterationsOption = new("--max-iterations")
    {
        Description = "Maximum fix iterations (default: 3)",
        DefaultValueFactory = _ => 3
    };

    // CLI options for continue mode
    private readonly Option<string> workflowIdOption = new("--workflow-id")
    {
        Description = "Workflow ID from previous call (required to continue workflow)"
    };

    private readonly Option<string> resultOption = new("--result")
    {
        Description = "JSON result from previous step (required to continue workflow)"
    };

    protected override Command GetCommand()
    {
        return new McpCommand("workflow", "SDK customization workflow coordinator", ToolName)
        {
            requestOption,
            requestTypeOption,
            SharedOptions.PackagePath,
            typeSpecPathOption,
            maxIterationsOption,
            workflowIdOption,
            resultOption
        };
    }

    public override async Task<CommandResponse> HandleCommand(ParseResult parseResult, CancellationToken ct)
    {
        var request = parseResult.GetValue(requestOption);
        var requestType = parseResult.GetValue(requestTypeOption);
        var packagePath = parseResult.GetValue(SharedOptions.PackagePath);
        var typeSpecPath = parseResult.GetValue(typeSpecPathOption);
        var maxIterations = parseResult.GetValue(maxIterationsOption);
        var workflowId = parseResult.GetValue(workflowIdOption);
        var result = parseResult.GetValue(resultOption);

        return await RunWorkflowAsync(request, requestType, packagePath, typeSpecPath, maxIterations, workflowId, result, ct);
    }

    [McpServerTool(Name = ToolName), Description("Coordinates SDK customization workflow. Start new workflow (provide request, requestType, paths) or continue existing (provide workflowId and result).")]
    public async Task<WorkflowResponse> RunWorkflowAsync(
        [Description("Build errors or user request (required for new workflow)")]
        string? request = null,
        [Description("Type of request: 'build_error' or 'user_request' (required for new workflow)")]
        string? requestType = null,
        [Description("Absolute path to SDK package")]
        string? packagePath = null,
        [Description("Absolute path to TypeSpec project")]
        string? typeSpecPath = null,
        [Description("Maximum fix iterations (default: 3)")]
        int maxIterations = 3,
        [Description("Workflow ID from previous call (required to continue workflow)")]
        string? workflowId = null,
        [Description("JSON result from previous step (required to continue workflow)")]
        string? result = null,
        CancellationToken ct = default)
    {
        try
        {
            // Determine mode: start vs continue
            if (string.IsNullOrEmpty(workflowId))
            {
                return await Task.FromResult(StartWorkflow(request, requestType, packagePath, typeSpecPath, maxIterations));
            }
            else
            {
                return await Task.FromResult(ContinueWorkflow(workflowId, result));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in SDK customization workflow");
            return CreateErrorResponse($"Error: {ex.Message}");
        }
    }

    private WorkflowResponse StartWorkflow(
        string? request,
        string? requestType,
        string? packagePath,
        string? typeSpecPath,
        int maxIterations)
    {
        // Validate required params
        if (string.IsNullOrEmpty(request))
        {
            return CreateErrorResponse("request is required to start a new workflow");
        }
        if (string.IsNullOrEmpty(requestType))
        {
            return CreateErrorResponse("requestType is required to start a new workflow");
        }
        if (string.IsNullOrEmpty(packagePath))
        {
            return CreateErrorResponse("packagePath is required to start a new workflow");
        }

        logger.LogInformation("Starting SDK customization workflow for {packagePath}", packagePath);

        // Create initial state
        var state = new WorkflowState
        {
            Phase = WorkflowPhase.Classify,
            EntryType = requestType,
            OriginalRequest = request,
            CurrentErrors = request, // For build_error, this is the errors
            PackagePath = packagePath,
            TypeSpecPath = typeSpecPath ?? "", // May be discovered later
            MaxIterations = maxIterations,
            Iteration = 1
        };

        // Store in service, get workflow ID
        var newWorkflowId = workflowStateService.CreateWorkflow(state);

        // Return classify phase with instructions
        return new WorkflowResponse
        {
            Phase = WorkflowPhase.Classify,
            Message = "Workflow started. Analyzing errors to determine fix approach...",
            Instruction = BuildClassificationInstruction(request),
            ExpectedResult = BuildClassificationExpectedResult(),
            WorkflowId = newWorkflowId,
            IsComplete = false
        };
    }

    private WorkflowResponse ContinueWorkflow(string workflowId, string? resultJson)
    {
        // Look up state from in-memory storage
        var state = workflowStateService.GetWorkflow(workflowId);
        if (state == null)
        {
            return CreateErrorResponse($"Workflow '{workflowId}' not found. It may have expired, completed, or the server was restarted. Start a new workflow.");
        }

        // Parse result
        if (string.IsNullOrEmpty(resultJson))
        {
            return CreateErrorResponse("result is required to continue a workflow");
        }

        StepResult? result;
        try
        {
            result = JsonSerializer.Deserialize<StepResult>(resultJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            return CreateErrorResponse($"Failed to parse result JSON: {ex.Message}");
        }

        if (result == null)
        {
            return CreateErrorResponse("Failed to parse result JSON");
        }

        logger.LogInformation("Continuing workflow {workflowId} from phase {phase} with result type {resultType}",
            workflowId, state.Phase, result.Type);

        // Apply state transition
        var response = ApplyTransition(state, result);

        // Update state in storage (unless complete)
        if (response.IsComplete)
        {
            workflowStateService.TryCompleteWorkflow(workflowId);
        }
        else
        {
            workflowStateService.UpdateWorkflow(workflowId, state);
        }

        return response;
    }

    // State machine logic
    private WorkflowResponse ApplyTransition(WorkflowState state, StepResult result)
    {
        return state.Phase switch
        {
            WorkflowPhase.Classify => HandleClassifyResult(state, result),
            WorkflowPhase.AttemptTspFix => HandleTspFixResult(state, result),
            WorkflowPhase.Regenerate => HandleRegenerateResult(state, result),
            WorkflowPhase.AttemptSdkFix => HandleSdkFixResult(state, result),
            WorkflowPhase.Build => HandleBuildResult(state, result),
            _ => CreateErrorResponse($"Invalid phase for transition: {state.Phase}")
        };
    }

    private WorkflowResponse HandleClassifyResult(WorkflowState state, StepResult result)
    {
        if (result.Type != "classification")
        {
            return CreateErrorResponse($"Expected classification result, got: {result.Type}");
        }

        var tspApplicable = result.TspApplicable ?? false;

        logger.LogInformation("Classification result: TspApplicable={tspApplicable}, TypeSpecPath={typeSpecPath}",
            tspApplicable, state.TypeSpecPath);

        state.History.Add(new WorkflowHistoryEntry
        {
            Iteration = state.Iteration,
            Phase = WorkflowPhase.Classify,
            Action = $"Classified: TSP applicable = {tspApplicable}",
            Result = "success"
        });

        if (tspApplicable)
        {
            state.Phase = WorkflowPhase.AttemptTspFix;
            state.LastFixType = "tsp";
            return new WorkflowResponse
            {
                Phase = WorkflowPhase.AttemptTspFix,
                Message = "Attempting to fix with TypeSpec decorators...",
                UseSubagent = new SubagentSuggestion
                {
                    Type = "typespec",
                    Prompt = BuildTspFixPrompt(state)
                },
                ExpectedResult = BuildTspFixExpectedResult(),
                WorkflowId = state.WorkflowId,
                IsComplete = false
            };
        }
        else
        {
            // TypeSpec not applicable - go to SDK fix
            state.Phase = WorkflowPhase.AttemptSdkFix;
            state.LastFixType = "sdk";
            return new WorkflowResponse
            {
                Phase = WorkflowPhase.AttemptSdkFix,
                Message = "TypeSpec cannot help. Attempting SDK code fix...",
                RunTool = new ToolSuggestion
                {
                    Name = "azsdk_sdk_fix",
                    Args = new Dictionary<string, string>
                    {
                        ["packagePath"] = state.PackagePath,
                        ["errors"] = state.CurrentErrors
                    }
                },
                ExpectedResult = BuildSdkFixExpectedResult(),
                WorkflowId = state.WorkflowId,
                IsComplete = false
            };
        }
    }

    private WorkflowResponse HandleTspFixResult(WorkflowState state, StepResult result)
    {
        state.History.Add(new WorkflowHistoryEntry
        {
            Iteration = state.Iteration,
            Phase = WorkflowPhase.AttemptTspFix,
            Action = $"TSP fix attempt: {result.Type}",
            Result = result.Type == "tsp_fix_applied" ? "success" : "failure",
            Details = result.Description ?? result.Reason
        });

        if (result.Type == "tsp_fix_applied")
        {
            // TSP fix applied, need to regenerate
            state.Phase = WorkflowPhase.Regenerate;
            return new WorkflowResponse
            {
                Phase = WorkflowPhase.Regenerate,
                Message = "TypeSpec fix applied. Regenerating SDK...",
                RunTool = new ToolSuggestion
                {
                    Name = "azsdk_package_generate",
                    Args = new Dictionary<string, string>
                    {
                        ["packagePath"] = state.PackagePath
                    }
                },
                ExpectedResult = BuildGenerateExpectedResult(),
                WorkflowId = state.WorkflowId,
                IsComplete = false
            };
        }
        else
        {
            // TSP couldn't help, fall back to SDK fix
            state.Phase = WorkflowPhase.AttemptSdkFix;
            state.LastFixType = "sdk";
            return new WorkflowResponse
            {
                Phase = WorkflowPhase.AttemptSdkFix,
                Message = "TypeSpec fix not applicable. Attempting SDK code fix...",
                RunTool = new ToolSuggestion
                {
                    Name = "azsdk_sdk_fix",
                    Args = new Dictionary<string, string>
                    {
                        ["packagePath"] = state.PackagePath,
                        ["errors"] = state.CurrentErrors
                    }
                },
                ExpectedResult = BuildSdkFixExpectedResult(),
                WorkflowId = state.WorkflowId,
                IsComplete = false
            };
        }
    }

    private WorkflowResponse HandleRegenerateResult(WorkflowState state, StepResult result)
    {
        var success = result.Success ?? false;

        state.History.Add(new WorkflowHistoryEntry
        {
            Iteration = state.Iteration,
            Phase = WorkflowPhase.Regenerate,
            Action = "SDK regeneration",
            Result = success ? "success" : "failure",
            Details = result.Errors ?? result.Output
        });

        if (success)
        {
            // Regeneration successful, now build
            state.Phase = WorkflowPhase.Build;
            return new WorkflowResponse
            {
                Phase = WorkflowPhase.Build,
                Message = "SDK regenerated. Building to verify fix...",
                RunTool = new ToolSuggestion
                {
                    Name = "azsdk_package_build",
                    Args = new Dictionary<string, string>
                    {
                        ["packagePath"] = state.PackagePath
                    }
                },
                ExpectedResult = BuildBuildExpectedResult(),
                WorkflowId = state.WorkflowId,
                IsComplete = false
            };
        }
        else
        {
            // Regeneration failed - terminal
            state.Phase = WorkflowPhase.Failure;
            return new WorkflowResponse
            {
                Phase = WorkflowPhase.Failure,
                Message = "SDK regeneration failed.",
                IsComplete = true,
                Status = "failure",
                Summary = GenerateSummary(state, "Regeneration failed"),
                WorkflowId = state.WorkflowId,
                ResponseError = result.Errors ?? "Regeneration failed"
            };
        }
    }

    private WorkflowResponse HandleSdkFixResult(WorkflowState state, StepResult result)
    {
        state.History.Add(new WorkflowHistoryEntry
        {
            Iteration = state.Iteration,
            Phase = WorkflowPhase.AttemptSdkFix,
            Action = $"SDK fix attempt: {result.Type}",
            Result = result.Type == "sdk_fix_applied" ? "success" : "failure",
            Details = result.Description ?? result.Reason
        });

        if (result.Type == "sdk_fix_applied")
        {
            // SDK fix applied, go directly to build (no regeneration needed)
            state.Phase = WorkflowPhase.Build;
            return new WorkflowResponse
            {
                Phase = WorkflowPhase.Build,
                Message = "SDK fix applied. Building to verify fix...",
                RunTool = new ToolSuggestion
                {
                    Name = "azsdk_package_build",
                    Args = new Dictionary<string, string>
                    {
                        ["packagePath"] = state.PackagePath
                    }
                },
                ExpectedResult = BuildBuildExpectedResult(),
                WorkflowId = state.WorkflowId,
                IsComplete = false
            };
        }
        else
        {
            // SDK fix failed - terminal
            state.Phase = WorkflowPhase.Failure;
            return new WorkflowResponse
            {
                Phase = WorkflowPhase.Failure,
                Message = "SDK fix could not be applied.",
                IsComplete = true,
                Status = "failure",
                Summary = GenerateSummary(state, "SDK fix failed"),
                WorkflowId = state.WorkflowId,
                ResponseError = result.Reason ?? "SDK fix failed"
            };
        }
    }

    private WorkflowResponse HandleBuildResult(WorkflowState state, StepResult result)
    {
        var success = result.Success ?? false;

        state.History.Add(new WorkflowHistoryEntry
        {
            Iteration = state.Iteration,
            Phase = WorkflowPhase.Build,
            Action = "Build verification",
            Result = success ? "success" : "failure",
            Details = result.Errors ?? result.Output
        });

        if (success)
        {
            // Success!
            state.Phase = WorkflowPhase.Success;
            return new WorkflowResponse
            {
                Phase = WorkflowPhase.Success,
                Message = "Build succeeded! Workflow complete.",
                IsComplete = true,
                Status = "success",
                Summary = GenerateSummary(state, "Build succeeded"),
                WorkflowId = state.WorkflowId
            };
        }

        // Build failed - update errors and check if we can retry
        state.CurrentErrors = result.Errors ?? state.CurrentErrors;
        state.Iteration++;

        if (state.Iteration > state.MaxIterations)
        {
            state.Phase = WorkflowPhase.Failure;
            return new WorkflowResponse
            {
                Phase = WorkflowPhase.Failure,
                Message = $"Max iterations ({state.MaxIterations}) reached. Build still failing.",
                IsComplete = true,
                Status = "failure",
                Summary = GenerateSummary(state, "Max iterations reached"),
                WorkflowId = state.WorkflowId,
                ResponseError = "Max iterations reached without successful build"
            };
        }

        // Reclassify with new errors
        state.Phase = WorkflowPhase.Classify;
        return new WorkflowResponse
        {
            Phase = WorkflowPhase.Classify,
            Message = $"Build failed. Starting iteration {state.Iteration} of {state.MaxIterations}. Reclassifying new errors...",
            Instruction = BuildClassificationInstruction(state.CurrentErrors),
            ExpectedResult = BuildClassificationExpectedResult(),
            WorkflowId = state.WorkflowId,
            IsComplete = false
        };
    }

    private static string BuildClassificationInstruction(string errors)
    {
        return $@"Use runSubagent to analyze these build errors and determine if any could potentially be fixed with TypeSpec client.tsp customizations. Have the subagent read `eng/common/knowledge/customizing-client-tsp.md` to understand the allowed customizations and their usage.

Build Errors:
{errors}

Respond with a JSON object: {{""type"": ""classification"", ""tspApplicable"": true/false}}

Set tspApplicable to true if TypeSpec decorators could help fix any of these errors (naming issues, visibility, model usage). Set to false for implementation issues, logic errors, or issues that require code changes.";
    }

    private static string BuildTspFixPrompt(WorkflowState state)
    {
        var typeSpecPathInfo = string.IsNullOrEmpty(state.TypeSpecPath)
            ? $"Package Path: {state.PackagePath}\n(Discover the TypeSpec project path from the package's tsp-location.yaml file)"
            : $"TypeSpec Path: {state.TypeSpecPath}";

        return $@"Use runSubagent to fix the build errors that can be resolved through TypeSpec client.tsp customizations. Have the subagent read `eng/common/knowledge/customizing-client-tsp.md` to understand the allowed customizations and their usage.

Build Errors:
{state.CurrentErrors}

{typeSpecPathInfo}

Read the TypeSpec customization documentation and have the subagent apply appropriate decorators (@clientName, @access, @usage, etc.) to fix the issues.

After making changes, respond with a JSON object:
- If fix applied: {{""type"": ""tsp_fix_applied"", ""description"": ""what was changed""}}
- If cannot fix with TypeSpec: {{""type"": ""tsp_fix_not_applicable"", ""reason"": ""why TypeSpec can't help""}}";
    }

    private static string GenerateSummary(WorkflowState state, string outcome)
    {
        var summary = new System.Text.StringBuilder();
        summary.AppendLine($"## SDK Customization Workflow Summary");
        summary.AppendLine($"- **Outcome**: {outcome}");
        summary.AppendLine($"- **Iterations**: {state.Iteration} of {state.MaxIterations}");
        summary.AppendLine($"- **Package**: {state.PackagePath}");
        summary.AppendLine();
        summary.AppendLine("### History");
        foreach (var entry in state.History)
        {
            summary.AppendLine($"- [Iteration {entry.Iteration}] {entry.Phase}: {entry.Action} ({entry.Result})");
            if (!string.IsNullOrEmpty(entry.Details))
            {
                summary.AppendLine($"  - Details: {entry.Details}");
            }
        }
        return summary.ToString();
    }

    #region Expected Result Builders

    private static ExpectedResult BuildClassificationExpectedResult()
    {
        return new ExpectedResult
        {
            Type = "classification",
            Description = "Classification of whether TypeSpec decorators can help fix the build errors",
            Fields = new Dictionary<string, string>
            {
                ["type"] = "Must be \"classification\"",
                ["tsp_applicable"] = "Boolean: true if TypeSpec decorators (@clientName, @access, @usage) could help fix any errors; false if errors require code changes"
            },
            Example = "{\"type\": \"classification\", \"tsp_applicable\": true}"
        };
    }

    private static ExpectedResult BuildTspFixExpectedResult()
    {
        return new ExpectedResult
        {
            Type = "tsp_fix_applied OR tsp_fix_not_applicable",
            Description = "Result of attempting to fix errors with TypeSpec decorators",
            Fields = new Dictionary<string, string>
            {
                ["type"] = "Either \"tsp_fix_applied\" (if decorators were added) or \"tsp_fix_not_applicable\" (if TypeSpec can't help)",
                ["description"] = "If fix applied: description of what decorators were added",
                ["reason"] = "If not applicable: explanation of why TypeSpec can't help"
            },
            Example = "{\"type\": \"tsp_fix_applied\", \"description\": \"Added @clientName decorator to rename Property1 to Property2\"}"
        };
    }

    private static ExpectedResult BuildGenerateExpectedResult()
    {
        return new ExpectedResult
        {
            Type = "generate_complete",
            Description = "Result of SDK code generation",
            Fields = new Dictionary<string, string>
            {
                ["type"] = "Must be \"generate_complete\"",
                ["success"] = "Boolean: true if generation succeeded, false if it failed",
                ["output"] = "Optional: generation output or summary",
                ["errors"] = "If failed: error messages from generation"
            },
            Example = "{\"type\": \"generate_complete\", \"success\": true, \"output\": \"SDK generated successfully\"}"
        };
    }

    private static ExpectedResult BuildSdkFixExpectedResult()
    {
        return new ExpectedResult
        {
            Type = "sdk_fix_applied OR sdk_fix_failed",
            Description = "Result of attempting to fix SDK customization code",
            Fields = new Dictionary<string, string>
            {
                ["type"] = "Either \"sdk_fix_applied\" (if code was fixed) or \"sdk_fix_failed\" (if fix couldn't be applied)",
                ["description"] = "If fix applied: description of what was changed",
                ["reason"] = "If failed: explanation of why the fix couldn't be applied"
            },
            Example = "{\"type\": \"sdk_fix_applied\", \"description\": \"Updated method signature to match new generated code\"}"
        };
    }

    private static ExpectedResult BuildBuildExpectedResult()
    {
        return new ExpectedResult
        {
            Type = "build_complete",
            Description = "Result of building the SDK package",
            Fields = new Dictionary<string, string>
            {
                ["type"] = "Must be \"build_complete\"",
                ["success"] = "Boolean: true if build succeeded, false if it failed",
                ["output"] = "Optional: build output or summary",
                ["errors"] = "If failed: build error messages"
            },
            Example = "{\"type\": \"build_complete\", \"success\": true}"
        };
    }

    #endregion

    private WorkflowResponse CreateErrorResponse(string message)
    {
        logger.LogError("Workflow error: {message}", message);
        return new WorkflowResponse
        {
            Phase = WorkflowPhase.Failure,
            Message = message,
            ResponseError = message,
            IsComplete = true,
            Status = "failure"
        };
    }
}
