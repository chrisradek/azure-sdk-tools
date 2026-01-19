using System.ComponentModel;
using System.Text;

namespace Azure.Sdk.Tools.Cli.Microagents.Tools;

public record ReadFileLinesInput(
    [property: Description("Relative path of the file to read")]
    string FilePath,

    [property: Description("Starting line number (1-indexed, inclusive)")]
    int StartLine,

    [property: Description("Ending line number (1-indexed, inclusive). Use -1 to read to end of file.")]
    int EndLine = -1
);

public record ReadFileLinesOutput(
    [property: Description("The content of the requested lines, with line numbers prefixed")]
    string Content,

    [property: Description("The actual start line returned")]
    int StartLine,

    [property: Description("The actual end line returned")]
    int EndLine,

    [property: Description("Total number of lines in the file")]
    int TotalLines
);

public class ReadFileLinesTool(string baseDir) : AgentTool<ReadFileLinesInput, ReadFileLinesOutput>
{
    public override string Name { get; init; } = "ReadFileLines";
    public override string Description { get; init; } = "Read specific line ranges from a file with line numbers prefixed";

    public override async Task<ReadFileLinesOutput> Invoke(ReadFileLinesInput input, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(input.FilePath))
        {
            throw new ArgumentException("Input path cannot be null or empty.", nameof(input));
        }

        if (!ToolHelpers.TryGetSafeFullPath(baseDir, input.FilePath, out var path))
        {
            throw new ArgumentException("The provided path is invalid or outside the allowed base directory.");
        }

        if (!File.Exists(path))
        {
            throw new ArgumentException($"{path} does not exist");
        }

        var lines = await File.ReadAllLinesAsync(path, ct);
        var totalLines = lines.Length;

        if (totalLines == 0)
        {
            return new ReadFileLinesOutput(string.Empty, 0, 0, 0);
        }

        // Clamp StartLine to valid range (1 to totalLines)
        var actualStart = Math.Max(1, Math.Min(input.StartLine, totalLines));

        // Determine actual end line
        int actualEnd;
        if (input.EndLine == -1)
        {
            actualEnd = totalLines;
        }
        else
        {
            actualEnd = Math.Max(actualStart, Math.Min(input.EndLine, totalLines));
        }

        // If StartLine was beyond total lines, return empty content
        if (input.StartLine > totalLines)
        {
            return new ReadFileLinesOutput(string.Empty, input.StartLine, input.StartLine, totalLines);
        }

        // Build content with line number prefixes
        var sb = new StringBuilder();
        for (int i = actualStart; i <= actualEnd; i++)
        {
            sb.AppendLine($"{i}. {lines[i - 1]}");
        }

        // Remove trailing newline
        if (sb.Length > 0 && sb[sb.Length - 1] == '\n')
        {
            sb.Length--;
            if (sb.Length > 0 && sb[sb.Length - 1] == '\r')
            {
                sb.Length--;
            }
        }

        return new ReadFileLinesOutput(sb.ToString(), actualStart, actualEnd, totalLines);
    }
}
