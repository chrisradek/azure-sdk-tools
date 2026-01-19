// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.ComponentModel;
using Azure.Sdk.Tools.Cli.Commands;
using Azure.Sdk.Tools.Cli.Helpers;
using Azure.Sdk.Tools.Cli.Microagents;
using Azure.Sdk.Tools.Cli.Microagents.Tools;
using Azure.Sdk.Tools.Cli.Models;
using Azure.Sdk.Tools.Cli.Prompts.Templates;
using Azure.Sdk.Tools.Cli.Tools.Core;
using ModelContextProtocol.Server;

namespace Azure.Sdk.Tools.Cli.Tools.TypeSpec;

#if DEBUG
[McpServerToolType, Description("Demo tool for testing TypeSpec client.tsp customization microagent")]
public class TypeSpecCustomizationDemoTool(
    ILogger<TypeSpecCustomizationDemoTool> logger,
    IMicroagentHostService microagentHostService,
    INpxHelper npxHelper,
    TokenUsageHelper tokenUsageHelper
) : MCPTool
{
    private const string ToolName = "azsdk_tsp_customization_demo";

    // azsdk tsp client customize
    public override CommandGroup[] CommandHierarchy { get; set; } = [
        SharedCommandGroups.TypeSpec,
        SharedCommandGroups.TypeSpecClient
    ];

    // CLI Options and Arguments
    private readonly Argument<string> typespecProjectPathArg = new("typespec-project-path")
    {
        Description = "Path to the TypeSpec project directory (contains tspconfig.yaml)",
        Arity = ArgumentArity.ExactlyOne
    };

    private readonly Argument<string> customizationRequestArg = new("customization-request")
    {
        Description = "Description of the customization to apply (e.g., 'Rename FooClient to BarClient for .NET')",
        Arity = ArgumentArity.ExactlyOne
    };

    private readonly Option<string> referenceDocPathOption = new("--reference-doc", "-r")
    {
        Description = "Path to the customizing-client-tsp.md reference document. If not provided, will look in eng/common/knowledge/ relative to the spec repo root.",
        Required = false
    };

    private readonly Option<int> maxToolCallsOption = new("--max-tool-calls", "-m")
    {
        Description = "Maximum number of tool calls the microagent can make",
        Required = false
    };

    protected override Command GetCommand() => new("customize", "Apply TypeSpec client.tsp customizations using a microagent")
    {
        typespecProjectPathArg,
        customizationRequestArg,
        referenceDocPathOption,
        maxToolCallsOption
    };

    public override async Task<CommandResponse> HandleCommand(ParseResult parseResult, CancellationToken ct)
    {
        var typespecProjectPath = parseResult.GetValue(typespecProjectPathArg)!;
        var customizationRequest = parseResult.GetValue(customizationRequestArg)!;
        var referenceDocPath = parseResult.GetValue(referenceDocPathOption);
        var maxToolCalls = parseResult.GetValue(maxToolCallsOption);

        return await ApplyTypeSpecCustomization(
            typespecProjectPath,
            customizationRequest,
            referenceDocPath,
            maxToolCalls > 0 ? maxToolCalls : 20,
            ct);
    }

    [McpServerTool(Name = ToolName), Description("Apply TypeSpec client.tsp customizations using a microagent. The microagent will read the TypeSpec project, apply decorators to client.tsp, and compile to validate.")]
    public async Task<TypeSpecCustomizationDemoResponse> ApplyTypeSpecCustomization(
        string typespecProjectPath,
        string customizationRequest,
        string? referenceDocPath = null,
        int maxToolCalls = 20,
        CancellationToken ct = default)
    {
        try
        {
            logger.LogInformation("Starting TypeSpec customization for project: {Path}", typespecProjectPath);
            logger.LogInformation("Customization request: {Request}", customizationRequest);

            // Validate TypeSpec project path
            if (!Directory.Exists(typespecProjectPath))
            {
                return new TypeSpecCustomizationDemoResponse
                {
                    ResponseError = $"TypeSpec project path does not exist: {typespecProjectPath}"
                };
            }

            var tspConfigPath = Path.Combine(typespecProjectPath, "tspconfig.yaml");
            if (!File.Exists(tspConfigPath))
            {
                return new TypeSpecCustomizationDemoResponse
                {
                    ResponseError = $"No tspconfig.yaml found in: {typespecProjectPath}"
                };
            }

            // Find reference doc path if not provided
            if (string.IsNullOrEmpty(referenceDocPath))
            {
                referenceDocPath = FindReferenceDoc(typespecProjectPath);
                if (referenceDocPath == null)
                {
                    return new TypeSpecCustomizationDemoResponse
                    {
                        ResponseError = "Could not find customizing-client-tsp.md reference document. Please provide --reference-doc path."
                    };
                }
            }

            if (!File.Exists(referenceDocPath))
            {
                return new TypeSpecCustomizationDemoResponse
                {
                    ResponseError = $"Reference document not found: {referenceDocPath}"
                };
            }

            logger.LogInformation("Using reference doc: {RefDoc}", referenceDocPath);

            // Build the prompt template
            var template = new TypeSpecCustomizationTemplate(
                customizationRequest: customizationRequest,
                typespecProjectPath: typespecProjectPath,
                referenceDocPath: referenceDocPath);

            var instructions = template.BuildPrompt();
            logger.LogDebug("Generated prompt with {Length} characters", instructions.Length);

            // Create the tools
            var readFileTool = new ReadFileTool(typespecProjectPath);
            var writeFileTool = new WriteFileTool(typespecProjectPath);
            var compileTypeSpecTool = new CompileTypeSpecTool(typespecProjectPath, npxHelper);

            // Create and run the microagent
            var microagent = new Microagent<TypeSpecCustomizationResult>
            {
                Instructions = instructions,
                Tools = [readFileTool, writeFileTool, compileTypeSpecTool],
                MaxToolCalls = maxToolCalls
            };

            logger.LogInformation("Running microagent with max {MaxToolCalls} tool calls...", maxToolCalls);

            var result = await microagentHostService.RunAgentToCompletion(microagent, ct);

            tokenUsageHelper.LogUsage();

            logger.LogInformation("Microagent completed. Success: {Success}", result.Success);

            return new TypeSpecCustomizationDemoResponse
            {
                Success = result.Success,
                ChangesSummary = result.ChangesSummary,
                FailureReason = result.FailureReason,
                TypeSpecProjectPath = typespecProjectPath
            };
        }
        catch (Exception ex)
        {
            tokenUsageHelper.LogUsage();
            logger.LogError(ex, "Error applying TypeSpec customization");
            return new TypeSpecCustomizationDemoResponse
            {
                ResponseError = $"Failed to apply TypeSpec customization: {ex.Message}"
            };
        }
    }

    private static string? FindReferenceDoc(string typespecProjectPath)
    {
        // Try to find eng/common/knowledge/customizing-client-tsp.md
        // by walking up from the TypeSpec project path
        var current = new DirectoryInfo(typespecProjectPath);

        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "eng", "common", "knowledge", "customizing-client-tsp.md");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            current = current.Parent;
        }

        return null;
    }
}

/// <summary>
/// Response for TypeSpec customization demo tool.
/// </summary>
public class TypeSpecCustomizationDemoResponse : CommandResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("success")]
    public bool Success { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("changes_summary")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? ChangesSummary { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("failure_reason")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? FailureReason { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("typespec_project_path")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? TypeSpecProjectPath { get; set; }

    protected override string Format()
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"Success: {Success}");

        if (!string.IsNullOrEmpty(TypeSpecProjectPath))
        {
            sb.AppendLine($"TypeSpec Project: {TypeSpecProjectPath}");
        }

        if (ChangesSummary != null && ChangesSummary.Length > 0)
        {
            sb.AppendLine("Changes Applied:");
            foreach (var change in ChangesSummary)
            {
                sb.AppendLine($"  - {change}");
            }
        }

        if (!string.IsNullOrEmpty(FailureReason))
        {
            sb.AppendLine($"Failure Reason: {FailureReason}");
        }

        return sb.ToString();
    }
}
#endif
