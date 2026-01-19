// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.Sdk.Tools.Cli.Models;

/// <summary>
/// Result of a TypeSpec customization microagent operation.
/// </summary>
public class TypeSpecCustomizationServiceResult
{
    /// <summary>
    /// Whether the customization was successful.
    /// True if the microagent applied changes and compilation succeeded.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Summary of changes applied to client.tsp.
    /// Populated when Success is true.
    /// </summary>
    public string[]? ChangesSummary { get; init; }

    /// <summary>
    /// Reason for failure if Success is false.
    /// Describes why the microagent could not apply the requested customization.
    /// </summary>
    public string? FailureReason { get; init; }
}
