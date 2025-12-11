// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Text.Json.Serialization;

namespace Azure.Sdk.Tools.Cli.Models.Workflow;

/// <summary>
/// Result reported by Copilot after completing a workflow phase.
/// Uses a flat structure with Type discriminator for simple JSON deserialization.
/// </summary>
public class StepResult
{
    /// <summary>
    /// Discriminator field. Valid values:
    /// - "classification" - from Classify phase
    /// - "tsp_fix_applied" - TSP fix was successful
    /// - "tsp_fix_not_applicable" - TSP cannot help
    /// - "sdk_fix_applied" - SDK fix was successful
    /// - "sdk_fix_failed" - SDK fix failed (terminal)
    /// - "generate_complete" - Generation finished
    /// - "build_complete" - Build finished
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    // Classification fields - support both snake_case and camelCase for flexibility
    [JsonPropertyName("tsp_applicable")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? TspApplicable { get; set; }

    // Alias for camelCase input (Copilot sometimes produces this format)
    [JsonPropertyName("tspApplicable")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? TspApplicableCamelCase
    {
        get => TspApplicable;
        set => TspApplicable ??= value;
    }

    // Fix result fields (TSP or SDK)
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; set; }

    // Generate/Build result fields
    [JsonPropertyName("success")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Success { get; set; }

    [JsonPropertyName("output")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Output { get; set; }

    [JsonPropertyName("errors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Errors { get; set; }
}
