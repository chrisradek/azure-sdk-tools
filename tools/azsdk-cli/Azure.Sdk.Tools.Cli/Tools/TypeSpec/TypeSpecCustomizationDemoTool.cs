// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.ComponentModel;
using Azure.Sdk.Tools.Cli.Commands;
using Azure.Sdk.Tools.Cli.Services.TypeSpec;
using Azure.Sdk.Tools.Cli.Tools.Core;
using Azure.Sdk.Tools.Cli.Models;
using ModelContextProtocol.Server;

namespace Azure.Sdk.Tools.Cli.Tools.TypeSpec;

#if DEBUG
[McpServerToolType, Description("Demo tool for testing TypeSpec client.tsp customization copilot agent")]
public class TypeSpecCustomizationDemoTool(
    ILogger<TypeSpecCustomizationDemoTool> logger,
    ITypeSpecCustomizationService typeSpecCustomizationService
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

    private readonly Option<int> maxIterationsOption = new("--max-iterations", "-m")
    {
        Description = "Maximum number of iterations the copilot agent can make",
        Required = false
    };

    protected override Command GetCommand() => new("customize", "Apply TypeSpec client.tsp customizations using a copilot agent")
    {
        typespecProjectPathArg,
        customizationRequestArg,
        referenceDocPathOption,
        maxIterationsOption
    };

    public override async Task<CommandResponse> HandleCommand(ParseResult parseResult, CancellationToken ct)
    {
        var typespecProjectPath = parseResult.GetValue(typespecProjectPathArg)!;
        var customizationRequest = parseResult.GetValue(customizationRequestArg)!;
        var referenceDocPath = parseResult.GetValue(referenceDocPathOption);
        var maxIterations = parseResult.GetValue(maxIterationsOption);

        return await ApplyTypeSpecCustomization(
            typespecProjectPath,
            customizationRequest,
            referenceDocPath,
            maxIterations > 0 ? maxIterations : 20,
            ct);
    }

    [McpServerTool(Name = ToolName), Description("Apply TypeSpec client.tsp customizations using a copilot agent. The agent will read the TypeSpec project, apply decorators to client.tsp, and compile to validate.")]
    public async Task<TypeSpecCustomizationDemoResponse> ApplyTypeSpecCustomization(
        string typespecProjectPath,
        string customizationRequest,
        string? referenceDocPath = null,
        int maxIterations = 20,
        CancellationToken ct = default)
    {
        try
        {
            var result = await typeSpecCustomizationService.ApplyCustomizationAsync(
                typespecProjectPath,
                customizationRequest,
                referenceDocPath,
                maxIterations,
                ct);

            return new TypeSpecCustomizationDemoResponse
            {
                Success = result.Success,
                ChangesSummary = result.ChangesSummary,
                FailureReason = result.FailureReason,
                TypeSpecProjectPath = typespecProjectPath
            };
        }
        catch (ArgumentException ex)
        {
            logger.LogError(ex, "Invalid argument for TypeSpec customization");
            return new TypeSpecCustomizationDemoResponse
            {
                ResponseError = ex.Message
            };
        }
        catch (FileNotFoundException ex)
        {
            logger.LogError(ex, "Required file not found for TypeSpec customization");
            return new TypeSpecCustomizationDemoResponse
            {
                ResponseError = ex.Message
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error applying TypeSpec customization");
            return new TypeSpecCustomizationDemoResponse
            {
                ResponseError = $"Failed to apply TypeSpec customization: {ex.Message}"
            };
        }
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
