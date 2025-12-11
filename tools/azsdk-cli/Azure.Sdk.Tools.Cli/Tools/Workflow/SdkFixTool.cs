// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.CommandLine;
using System.ComponentModel;
using Azure.Sdk.Tools.Cli.Commands;
using Azure.Sdk.Tools.Cli.Helpers;
using Azure.Sdk.Tools.Cli.Models;
using Azure.Sdk.Tools.Cli.Services.Languages;
using Azure.Sdk.Tools.Cli.Tools.Core;
using ModelContextProtocol.Server;

namespace Azure.Sdk.Tools.Cli.Tools.Workflow;

/// <summary>
/// Tool for fixing SDK customization code build errors using language-specific microagent.
/// </summary>
[McpServerToolType, Description("Fix SDK customization code build errors")]
public class SdkFixTool : LanguageMcpTool
{
    private const string ToolName = "azsdk_sdk_fix";

    public SdkFixTool(
        ILogger<SdkFixTool> logger,
        IEnumerable<LanguageService> languageServices,
        IGitHelper gitHelper
    ) : base(languageServices, gitHelper, logger) { }

    public override CommandGroup[] CommandHierarchy { get; set; } = [SharedCommandGroups.Package];

    private readonly Option<string> errorsOption = new("--errors", "-e")
    {
        Description = "Build errors to fix"
    };

    protected override Command GetCommand() =>
        new McpCommand("sdk-fix", "Fix SDK customization code build errors", ToolName)
        {
            SharedOptions.PackagePath,
            errorsOption
        };

    public override async Task<CommandResponse> HandleCommand(ParseResult parseResult, CancellationToken ct)
    {
        var packagePath = parseResult.GetValue(SharedOptions.PackagePath);
        var errors = parseResult.GetValue(errorsOption);
        return await FixAsync(packagePath!, errors, ct);
    }

    [McpServerTool(Name = ToolName), Description("Fix SDK customization code build errors using language-specific microagent")]
    public async Task<SdkFixResponse> FixAsync(
        [Description("Absolute path to SDK package")] string packagePath,
        [Description("Build errors to fix")] string? errors = null,
        CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrEmpty(packagePath))
            {
                return SdkFixResponse.CreateFailure("packagePath is required");
            }

            if (!Directory.Exists(packagePath))
            {
                return SdkFixResponse.CreateFailure($"Package path does not exist: {packagePath}");
            }

            var languageService = GetLanguageService(packagePath);
            if (languageService == null)
            {
                return SdkFixResponse.CreateFailure("Could not determine language for package path");
            }

            if (!languageService.IsCustomizedCodeUpdateSupported)
            {
                return SdkFixResponse.CreateFailure("Language does not support customization updates");
            }

            var customizationRoot = languageService.GetCustomizationRoot(packagePath, ct);
            if (string.IsNullOrEmpty(customizationRoot) || !Directory.Exists(customizationRoot))
            {
                return SdkFixResponse.CreateFailure("No customization directory found");
            }

            logger.LogInformation("Applying SDK customization fixes for {packagePath}", packagePath);

            // Call ApplyPatchesAsync with empty commitSha (POC - relies on microagent context)
            var success = await languageService.ApplyPatchesAsync(
                commitSha: string.Empty,
                customizationRoot: customizationRoot,
                packagePath: packagePath,
                ct: ct);

            return success
                ? SdkFixResponse.CreateSuccess("SDK customization fix applied")
                : SdkFixResponse.CreateFailure("SDK customization fix was not successful");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fixing SDK customization code");
            return SdkFixResponse.CreateFailure($"Error: {ex.Message}");
        }
    }
}

/// <summary>
/// Response from SDK fix operation.
/// </summary>
public class SdkFixResponse : CommandResponse
{
    public bool FixApplied { get; set; }
    public string? Description { get; set; }

    public static SdkFixResponse CreateSuccess(string description) =>
        new() { FixApplied = true, Description = description };

    public static SdkFixResponse CreateFailure(string reason) =>
        new() { FixApplied = false, ResponseError = reason };

    protected override string Format()
    {
        return FixApplied
            ? $"Fix applied: {Description}"
            : $"Fix failed: {ResponseError}";
    }
}
