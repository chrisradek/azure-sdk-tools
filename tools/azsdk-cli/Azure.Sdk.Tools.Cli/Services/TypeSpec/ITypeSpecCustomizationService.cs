// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Sdk.Tools.Cli.Models;

namespace Azure.Sdk.Tools.Cli.Services.TypeSpec;

/// <summary>
/// Service for applying TypeSpec client.tsp customizations using a microagent.
/// </summary>
public interface ITypeSpecCustomizationService
{
    /// <summary>
    /// Applies TypeSpec client.tsp customizations based on the provided request.
    /// </summary>
    /// <param name="typespecProjectPath">Path to the TypeSpec project directory (must contain tspconfig.yaml)</param>
    /// <param name="customizationRequest">Description of the customization to apply</param>
    /// <param name="referenceDocPath">Optional path to the customizing-client-tsp.md reference document</param>
    /// <param name="maxToolCalls">Maximum number of tool calls the microagent can make (default: 20)</param>
    /// <param name="useSearchTools">Whether to include search tools (GlobTool, GrepTool, ReadFileLinesTool) for more efficient token usage</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Result of the customization operation</returns>
    Task<TypeSpecCustomizationServiceResult> ApplyCustomizationAsync(
        string typespecProjectPath,
        string customizationRequest,
        string? referenceDocPath = null,
        int maxToolCalls = 20,
        bool useSearchTools = true,
        CancellationToken ct = default);
}

