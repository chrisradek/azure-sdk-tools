using System.ComponentModel;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Azure.Sdk.Tools.Cli.Microagents.Tools;

public record GlobInput(
    [property: Description("Glob pattern to match files (e.g., '**/*.tsp', 'src/**/*.cs')")]
    string Pattern
);

public record GlobOutput(
    [property: Description("List of matching file paths relative to base directory")]
    string[] Files
);

public class GlobTool(string baseDir) : AgentTool<GlobInput, GlobOutput>
{
    public override string Name { get; init; } = "Glob";
    public override string Description { get; init; } = "Find files by name patterns using glob syntax";

    public override Task<GlobOutput> Invoke(GlobInput input, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(input.Pattern))
        {
            throw new ArgumentException("Pattern cannot be null or empty.", nameof(input));
        }

        var matcher = new Matcher();
        matcher.AddInclude(input.Pattern);

        var directoryInfo = new DirectoryInfo(baseDir);
        var result = matcher.Execute(new DirectoryInfoWrapper(directoryInfo));

        var files = result.Files
            .Select(f => f.Path)
            .ToArray();

        return Task.FromResult(new GlobOutput(files));
    }
}
