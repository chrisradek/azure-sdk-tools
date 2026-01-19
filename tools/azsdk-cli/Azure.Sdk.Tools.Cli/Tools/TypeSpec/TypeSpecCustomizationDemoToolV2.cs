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
/// <summary>
/// V2 of the TypeSpec customization demo tool that uses the new search tools
/// (GlobTool, GrepTool, ReadFileLinesTool) for more efficient token usage.
/// </summary>
[McpServerToolType, Description("Demo tool V2 for testing TypeSpec client.tsp customization microagent with search tools")]
public class TypeSpecCustomizationDemoToolV2(
    ILogger<TypeSpecCustomizationDemoToolV2> logger,
    IMicroagentHostService microagentHostService,
    INpxHelper npxHelper,
    TokenUsageHelper tokenUsageHelper
) : MCPTool
{
    private const string ToolName = "azsdk_tsp_customization_demo_v2";

    // azsdk tsp client customize-v2
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

    protected override Command GetCommand() => new("customize-v2", "Apply TypeSpec client.tsp customizations using a microagent with search tools (V2)")
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

    [McpServerTool(Name = ToolName), Description("Apply TypeSpec client.tsp customizations using a microagent with search tools (V2). Uses GlobTool, GrepTool, and ReadFileLinesTool for more efficient token usage.")]
    public async Task<TypeSpecCustomizationDemoResponse> ApplyTypeSpecCustomization(
        string typespecProjectPath,
        string customizationRequest,
        string? referenceDocPath = null,
        int maxToolCalls = 20,
        CancellationToken ct = default)
    {
        try
        {
            logger.LogInformation("Starting TypeSpec customization V2 for project: {Path}", typespecProjectPath);
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

            // Create the tools - V2 includes search tools for efficient token usage
            var tools = new List<IAgentTool>
            {
                // Search tools (new in V2) - use these first to find relevant code
                new GlobTool(typespecProjectPath),
                new GrepTool(typespecProjectPath),
                new ReadFileLinesTool(typespecProjectPath),

                // Original tools
                new ReadFileTool(typespecProjectPath),
                new WriteFileTool(typespecProjectPath),
                new CompileTypeSpecTool(typespecProjectPath, npxHelper)
            };

            // Create and run the microagent
            var microagent = new Microagent<TypeSpecCustomizationResult>
            {
                Instructions = instructions + GetSearchToolsGuidance(),
                Tools = tools,
                MaxToolCalls = maxToolCalls,
                Model = "gpt-5"
            };

            logger.LogInformation("Running microagent V2 with {ToolCount} tools and max {MaxToolCalls} tool calls...", tools.Count, maxToolCalls);

            var result = await microagentHostService.RunAgentToCompletion(microagent, ct);

            tokenUsageHelper.LogUsage();

            logger.LogInformation("Microagent V2 completed. Success: {Success}", result.Success);

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
            logger.LogError(ex, "Error applying TypeSpec customization V2");
            return new TypeSpecCustomizationDemoResponse
            {
                ResponseError = $"Failed to apply TypeSpec customization: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Additional guidance for using search tools efficiently.
    /// </summary>
    private static string GetSearchToolsGuidance()
    {
        return """

## Tool Usage Guidelines

**Search Tools (use sparingly):**
You have Glob, Grep, and ReadFileLines tools available. These are optional helpers - only use them when you need to search for something specific across many files.

**Recommended approach for this task:**
1. Start by reading client.tsp directly with ReadFile (it's usually small)
2. If you need to find type/operation names referenced in the customization request, use Grep once to locate them
3. Only use ReadFileLines if a file is very large and you only need a specific section

**Important:**
- Do NOT use Glob/Grep just to explore - you already know the key files: client.tsp, main.tsp, tspconfig.yaml
- Do NOT compile until you have actually written changes to client.tsp
- Compile only AFTER making a change, not before or to "check" the current state
- Keep tool calls minimal - aim for efficiency
""";
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
#endif
