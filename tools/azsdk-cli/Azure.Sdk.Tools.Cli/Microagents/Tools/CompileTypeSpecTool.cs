// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.ComponentModel;
using Azure.Sdk.Tools.Cli.Helpers;

namespace Azure.Sdk.Tools.Cli.Microagents.Tools;

public record CompileTypeSpecInput();

public record CompileTypeSpecOutput(
    [property: Description("Whether compilation succeeded")]
    bool Success,
    [property: Description("Compilation output/errors")]
    string Output
);

public class CompileTypeSpecTool(string typespecProjectPath, INpxHelper npxHelper) : AgentTool<CompileTypeSpecInput, CompileTypeSpecOutput>
{
    public override string Name { get; init; } = "CompileTypeSpec";
    public override string Description { get; init; } = "Compile the TypeSpec project to validate there are no errors in the TypeSpec definitions";

    public override async Task<CompileTypeSpecOutput> Invoke(CompileTypeSpecInput input, CancellationToken ct)
    {
        try
        {
            var npxOptions = new NpxOptions(
                package: null,
                args: ["tsp", "compile", "./client.tsp"],
                logOutputStream: true,
                workingDirectory: typespecProjectPath,
                timeout: TimeSpan.FromMinutes(2)
            );

            var compileCt = CancellationTokenSource.CreateLinkedTokenSource(ct);
            compileCt.CancelAfter(TimeSpan.FromMinutes(2));

            var result = await npxHelper.Run(npxOptions, compileCt.Token);

            if (result.ExitCode == 0)
            {
                return new CompileTypeSpecOutput(
                    Success: true,
                    Output: string.IsNullOrWhiteSpace(result.Output) ? "Compilation succeeded" : result.Output
                );
            }

            return new CompileTypeSpecOutput(
                Success: false,
                Output: result.Output
            );
        }
        catch (OperationCanceledException)
        {
            return new CompileTypeSpecOutput(
                Success: false,
                Output: "Compilation timed out after 2 minutes"
            );
        }
        catch (Exception ex)
        {
            return new CompileTypeSpecOutput(
                Success: false,
                Output: $"Failed to compile TypeSpec project: {ex.Message}"
            );
        }
    }
}
