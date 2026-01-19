// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Sdk.Tools.Cli.Helpers;
using Azure.Sdk.Tools.Cli.Microagents;
using Azure.Sdk.Tools.Cli.Microagents.Tools;
using Azure.Sdk.Tools.Cli.Models;
using Azure.Sdk.Tools.Cli.Prompts.Templates;

namespace Azure.Sdk.Tools.Cli.Services.TypeSpec;

/// <summary>
/// Service for applying TypeSpec client.tsp customizations using a microagent.
/// </summary>
public class TypeSpecCustomizationService : ITypeSpecCustomizationService
{
    private readonly ILogger<TypeSpecCustomizationService> _logger;
    private readonly IMicroagentHostService _microagentHostService;
    private readonly INpxHelper _npxHelper;
    private readonly TokenUsageHelper _tokenUsageHelper;
    private readonly ITypeSpecHelper _typeSpecHelper;

    public TypeSpecCustomizationService(
        ILogger<TypeSpecCustomizationService> logger,
        IMicroagentHostService microagentHostService,
        INpxHelper npxHelper,
        TokenUsageHelper tokenUsageHelper,
        ITypeSpecHelper typeSpecHelper)
    {
        _logger = logger;
        _microagentHostService = microagentHostService;
        _npxHelper = npxHelper;
        _tokenUsageHelper = tokenUsageHelper;
        _typeSpecHelper = typeSpecHelper;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Thrown when typespecProjectPath is invalid (doesn't exist or missing tspconfig.yaml).</exception>
    /// <exception cref="FileNotFoundException">Thrown when the reference document cannot be found.</exception>
    public async Task<TypeSpecCustomizationServiceResult> ApplyCustomizationAsync(
        string typespecProjectPath,
        string customizationRequest,
        string? referenceDocPath = null,
        int maxToolCalls = 20,
        bool useSearchTools = true,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Starting TypeSpec customization for project: {Path}", typespecProjectPath);
        _logger.LogInformation("Customization request: {Request}", customizationRequest);

        // Validate TypeSpec project path using existing helper
        if (!_typeSpecHelper.IsValidTypeSpecProjectPath(typespecProjectPath))
        {
            throw new ArgumentException(
                $"Invalid TypeSpec project path: {typespecProjectPath}. Directory must exist and contain tspconfig.yaml.",
                nameof(typespecProjectPath));
        }

        // Find reference doc path if not provided
        if (string.IsNullOrEmpty(referenceDocPath))
        {
            referenceDocPath = FindReferenceDoc(typespecProjectPath);
            if (referenceDocPath == null)
            {
                throw new FileNotFoundException(
                    "Could not find customizing-client-tsp.md reference document. Please provide the reference doc path.");
            }
        }

        if (!File.Exists(referenceDocPath))
        {
            throw new FileNotFoundException(
                $"Reference document not found: {referenceDocPath}", referenceDocPath);
        }

        _logger.LogInformation("Using reference doc: {RefDoc}", referenceDocPath);

        // Build the prompt template
        var template = new TypeSpecCustomizationTemplate(
            customizationRequest: customizationRequest,
            typespecProjectPath: typespecProjectPath,
            referenceDocPath: referenceDocPath);

        var instructions = template.BuildPrompt();
        _logger.LogDebug("Generated prompt with {Length} characters", instructions.Length);

        // Create the tools
        var tools = CreateTools(typespecProjectPath, useSearchTools);

        // Add search tools guidance if using search tools
        if (useSearchTools)
        {
            instructions += GetSearchToolsGuidance();
        }

        // Create and run the microagent
        var microagent = new Microagent<TypeSpecCustomizationResult>
        {
            Instructions = instructions,
            Tools = tools,
            MaxToolCalls = maxToolCalls,
            Model = "gpt-5"
        };

        _logger.LogInformation("Running microagent with {ToolCount} tools and max {MaxToolCalls} tool calls...",
            tools.Count, maxToolCalls);

        var result = await _microagentHostService.RunAgentToCompletion(microagent, ct);

        _tokenUsageHelper.LogUsage();

        _logger.LogInformation("Microagent completed. Success: {Success}", result.Success);

        return new TypeSpecCustomizationServiceResult
        {
            Success = result.Success,
            ChangesSummary = result.ChangesSummary,
            FailureReason = result.FailureReason
        };
    }

    /// <summary>
    /// Creates the tools for the microagent.
    /// </summary>
    private List<IAgentTool> CreateTools(string typespecProjectPath, bool useSearchTools)
    {
        var tools = new List<IAgentTool>();

        // Add search tools if requested (for more efficient token usage)
        if (useSearchTools)
        {
            tools.Add(new GlobTool(typespecProjectPath));
            tools.Add(new GrepTool(typespecProjectPath));
            tools.Add(new ReadFileLinesTool(typespecProjectPath));
        }

        // Core tools
        tools.Add(new ReadFileTool(typespecProjectPath));
        tools.Add(new WriteFileTool(typespecProjectPath));
        tools.Add(new CompileTypeSpecTool(typespecProjectPath, _npxHelper));

        return tools;
    }

    /// <summary>
    /// Tries to find the customizing-client-tsp.md reference document by walking up 
    /// from the TypeSpec project path looking for eng/common/knowledge/.
    /// </summary>
    public static string? -FindReferenceDoc(string typespecProjectPath)
    {
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
}
