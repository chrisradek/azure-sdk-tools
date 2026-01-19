// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.CommandLine;
using System.ComponentModel;
using System.Text.Json;
using Azure.Sdk.Tools.Cli.Commands;
using Azure.Sdk.Tools.Cli.Models;
using Azure.Sdk.Tools.Cli.Models.Workflow;
using Azure.Sdk.Tools.Cli.Services;
using Azure.Sdk.Tools.Cli.Tools.Package;
using ModelContextProtocol.Server;

namespace Azure.Sdk.Tools.Cli.Tools.Workflow;

/// <summary>
/// Coordinates SDK customization workflow for fixing build errors and applying customizations.
/// This tool acts as a state machine that handles regeneration and building internally,
/// only returning to the agent for classification and fix application steps.
/// </summary>
[McpServerToolType, Description("Coordinates SDK customization workflow for fixing build errors and applying customizations")]
public class SdkCustomizationWorkflowTool(
    ILogger<SdkCustomizationWorkflowTool> logger,
    IWorkflowStateService workflowStateService,
    SdkGenerationTool generationTool,
    SdkBuildTool buildTool
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
                return StartWorkflow(request, requestType, packagePath, typeSpecPath, maxIterations);
            }
            else
            {
                return await ContinueWorkflowAsync(workflowId, result, ct);
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
            IsComplete = false,
            ContinuationRequired = true,
            ContinuationInstruction = BuildContinuationInstruction(newWorkflowId, "classifying the errors"),
            Progress = BuildProgress(state, "Classify", new List<string>(), new List<string> { "Fix", "Build", "Verify" })
        };
    }

    private async Task<WorkflowResponse> ContinueWorkflowAsync(string workflowId, string? resultJson, CancellationToken ct)
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
        var response = await ApplyTransitionAsync(state, result, ct);

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
    private async Task<WorkflowResponse> ApplyTransitionAsync(WorkflowState state, StepResult result, CancellationToken ct)
    {
        return state.Phase switch
        {
            WorkflowPhase.Classify => HandleClassifyResult(state, result),
            WorkflowPhase.AttemptTspFix => await HandleTspFixResultAsync(state, result, ct),
            WorkflowPhase.AttemptSdkFix => await HandleSdkFixResultAsync(state, result, ct),
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
                IsComplete = false,
                ContinuationRequired = true,
                ContinuationInstruction = BuildContinuationInstruction(state.WorkflowId, "applying TypeSpec decorators"),
                Progress = BuildProgress(state, "AttemptTspFix", new List<string> { "Classify" }, new List<string> { "Verify" })
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
                IsComplete = false,
                ContinuationRequired = true,
                ContinuationInstruction = BuildContinuationInstruction(state.WorkflowId, "running the SDK fix tool"),
                Progress = BuildProgress(state, "AttemptSdkFix", new List<string> { "Classify" }, new List<string> { "Verify" })
            };
        }
    }

    private async Task<WorkflowResponse> HandleTspFixResultAsync(WorkflowState state, StepResult result, CancellationToken ct)
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
            // TSP fix applied - regenerate and build internally
            logger.LogInformation("TypeSpec fix applied, regenerating SDK for {packagePath}", state.PackagePath);
            
            // Regenerate SDK
            var generateResult = await generationTool.GenerateSdkAsync(
                localSdkRepoPath: state.PackagePath,
                tspConfigPath: null,
                tspLocationPath: null,  // Will discover from package
                emitterOptions: null,
                ct: ct);

            state.History.Add(new WorkflowHistoryEntry
            {
                Iteration = state.Iteration,
                Phase = WorkflowPhase.Regenerate,
                Action = "SDK regeneration (internal)",
                Result = generateResult.OperationStatus == Status.Succeeded ? "success" : "failure",
                Details = generateResult.ResponseError
            });

            if (generateResult.OperationStatus != Status.Succeeded)
            {
                state.Phase = WorkflowPhase.Failure;
                return new WorkflowResponse
                {
                    Phase = WorkflowPhase.Failure,
                    Message = "❌ SDK regeneration failed after TypeSpec fix.",
                    IsComplete = true,
                    Status = "failure",
                    Summary = GenerateSummary(state, "Regeneration failed"),
                    WorkflowId = state.WorkflowId,
                    ResponseError = generateResult.ResponseError ?? "Regeneration failed",
                    ContinuationRequired = false
                };
            }

            // Build SDK
            logger.LogInformation("SDK regenerated, building to verify fix for {packagePath}", state.PackagePath);
            var buildResult = await buildTool.BuildSdkAsync(state.PackagePath, ct);

            state.History.Add(new WorkflowHistoryEntry
            {
                Iteration = state.Iteration,
                Phase = WorkflowPhase.Build,
                Action = "Build verification (internal)",
                Result = buildResult.OperationStatus == Status.Succeeded ? "success" : "failure",
                Details = buildResult.ResponseError
            });

            return HandleBuildOutcome(state, buildResult);
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
                IsComplete = false,
                ContinuationRequired = true,
                ContinuationInstruction = BuildContinuationInstruction(state.WorkflowId, "running the SDK fix tool"),
                Progress = BuildProgress(state, "AttemptSdkFix", new List<string> { "Classify", "TypeSpec Fix (skipped)" }, new List<string> { "Verify" })
            };
        }
    }

    private async Task<WorkflowResponse> HandleSdkFixResultAsync(WorkflowState state, StepResult result, CancellationToken ct)
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
            // SDK fix applied - build directly (no regeneration needed for SDK fixes)
            logger.LogInformation("SDK fix applied, building to verify fix for {packagePath}", state.PackagePath);
            var buildResult = await buildTool.BuildSdkAsync(state.PackagePath, ct);

            state.History.Add(new WorkflowHistoryEntry
            {
                Iteration = state.Iteration,
                Phase = WorkflowPhase.Build,
                Action = "Build verification (internal)",
                Result = buildResult.OperationStatus == Status.Succeeded ? "success" : "failure",
                Details = buildResult.ResponseError
            });

            return HandleBuildOutcome(state, buildResult);
        }
        else
        {
            // SDK fix failed - terminal
            state.Phase = WorkflowPhase.Failure;
            return new WorkflowResponse
            {
                Phase = WorkflowPhase.Failure,
                Message = "❌ SDK fix could not be applied.",
                IsComplete = true,
                Status = "failure",
                Summary = GenerateSummary(state, "SDK fix failed"),
                WorkflowId = state.WorkflowId,
                ResponseError = result.Reason ?? "SDK fix failed",
                ContinuationRequired = false
            };
        }
    }

    /// <summary>
    /// Shared handler for build outcomes - used after both TSP fix+regenerate and SDK fix paths.
    /// </summary>
    private WorkflowResponse HandleBuildOutcome(WorkflowState state, Models.Responses.Package.PackageOperationResponse buildResult)
    {
        var success = buildResult.OperationStatus == Status.Succeeded;

        if (success)
        {
            // Success!
            state.Phase = WorkflowPhase.Success;
            return new WorkflowResponse
            {
                Phase = WorkflowPhase.Success,
                Message = "✅ Build succeeded! Workflow complete.",
                IsComplete = true,
                Status = "success",
                Summary = GenerateSummary(state, "Build succeeded"),
                WorkflowId = state.WorkflowId,
                ContinuationRequired = false,
                Progress = BuildProgress(state, "Complete", new List<string> { "Classify", "Fix", "Verify" }, new List<string>())
            };
        }

        // Build failed - extract errors and check if we can retry
        var errors = buildResult.ResponseError ?? "Build failed";
        state.CurrentErrors = errors;
        state.Iteration++;

        if (state.Iteration > state.MaxIterations)
        {
            state.Phase = WorkflowPhase.Failure;
            return new WorkflowResponse
            {
                Phase = WorkflowPhase.Failure,
                Message = $"❌ Max iterations ({state.MaxIterations}) reached. Build still failing.",
                IsComplete = true,
                Status = "failure",
                Summary = GenerateSummary(state, "Max iterations reached"),
                WorkflowId = state.WorkflowId,
                ResponseError = errors,
                ContinuationRequired = false
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
            IsComplete = false,
            ContinuationRequired = true,
            ContinuationInstruction = BuildContinuationInstruction(state.WorkflowId, "classifying the new errors"),
            Progress = BuildProgress(state, "Classify", new List<string> { $"Iteration {state.Iteration - 1} (failed)" }, new List<string> { "Fix", "Verify" })
        };
    }

    private static string BuildClassificationInstruction(string errors)
    {
        return $@"TASK: Analyze these build errors and determine if any could potentially be fixed with TypeSpec client.tsp customizations.

Use runSubagent to read `eng/common/knowledge/customizing-client-tsp.md` to understand the allowed customizations and their usage. You must use a subagent to preserve your context window.

Build Errors:
{errors}

After analysis, respond with a JSON object: {{""type"": ""classification"", ""tspApplicable"": true/false}}

Set tspApplicable to true if TypeSpec decorators could help fix any of these errors (naming issues, visibility, model usage). Set to false for implementation issues, logic errors, or issues that require code changes.";
    }

    private static string BuildContinuationInstruction(string workflowId, string actionDescription)
    {
        return $@"⚠️ CRITICAL: After {actionDescription}, you MUST immediately call the azsdk_sdk_customization_workflow tool again with:
  - workflowId: ""{workflowId}""
  - result: <JSON result of the completed action>

DO NOT proceed to other tasks. DO NOT report completion to the user. The workflow is NOT complete until you receive a response with is_complete=true.";
    }

    private static WorkflowProgress BuildProgress(WorkflowState state, string currentStepName, List<string> completedSteps, List<string> remainingSteps)
    {
        return new WorkflowProgress
        {
            CurrentStep = completedSteps.Count + 1,
            TotalSteps = completedSteps.Count + 1 + remainingSteps.Count,
            CompletedSteps = completedSteps,
            RemainingSteps = remainingSteps
        };
    }

    private static string BuildTspFixPrompt(WorkflowState state)
    {
        var typeSpecPathInfo = string.IsNullOrEmpty(state.TypeSpecPath)
            ? $"Package Path: {state.PackagePath}\n(Discover the TypeSpec project path from the package's tsp-location.yaml file)"
            : $"TypeSpec Path: {state.TypeSpecPath}";

        return $@"Use runSubagent to fix the build errors that can be resolved through TypeSpec client.tsp customizations. Have the subagent read `eng/common/knowledge/customizing-client-tsp.md` to understand the allowed customizations and their usage.  You must use a subagent to preserve your context window.

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

    #endregion

    private WorkflowResponse CreateErrorResponse(string message)
    {
        logger.LogError("Workflow error: {message}", message);
        return new WorkflowResponse
        {
            Phase = WorkflowPhase.Failure,
            Message = $"❌ {message}",
            ResponseError = message,
            IsComplete = true,
            Status = "failure",
            ContinuationRequired = false
        };
    }
}
