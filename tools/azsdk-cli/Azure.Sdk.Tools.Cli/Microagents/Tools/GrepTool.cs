using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Azure.Sdk.Tools.Cli.Microagents.Tools;

public record GrepInput(
    [property: Description("Regex pattern to search for")]
    string Pattern,

    [property: Description("Optional glob pattern to filter files (e.g., '**/*.tsp'). Defaults to all files.")]
    string? FilePattern = null,

    [property: Description("If true, performs case-insensitive matching. Defaults to false.")]
    bool IgnoreCase = false,

    [property: Description("Number of context lines to include before each match. Defaults to 0.")]
    int ContextBefore = 0,

    [property: Description("Number of context lines to include after each match. Defaults to 0.")]
    int ContextAfter = 0,

    [property: Description("If true, returns only file paths without match details. Defaults to false.")]
    bool FilesOnly = false,

    [property: Description("Maximum number of results to return. Defaults to 100.")]
    int MaxResults = 100
);

public record GrepMatch(
    [property: Description("File path relative to base directory")]
    string File,

    [property: Description("Line number of the match (1-indexed)")]
    int Line,

    [property: Description("The matching line content (with context if requested)")]
    string Content
);

public record GrepOutput(
    [property: Description("List of matches found")]
    GrepMatch[] Matches,

    [property: Description("Total number of matches (may exceed returned results if MaxResults was hit)")]
    int TotalMatches,

    [property: Description("Whether results were truncated due to MaxResults limit")]
    bool Truncated
);

public class GrepTool(string baseDir) : AgentTool<GrepInput, GrepOutput>
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);
    private const int BinaryCheckSize = 8192;

    public override string Name { get; init; } = "Grep";
    public override string Description { get; init; } = "Search file contents for regex patterns";

    public override Task<GrepOutput> Invoke(GrepInput input, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(input.Pattern))
        {
            throw new ArgumentException("Pattern cannot be null or empty.", nameof(input));
        }

        // Validate and compile regex
        var regexOptions = RegexOptions.Compiled;
        if (input.IgnoreCase)
        {
            regexOptions |= RegexOptions.IgnoreCase;
        }

        Regex regex;
        try
        {
            regex = new Regex(input.Pattern, regexOptions, RegexTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"Invalid regex pattern: {ex.Message}", nameof(input));
        }

        // Get files to search
        var files = GetFilesToSearch(input.FilePattern);

        var matches = new List<GrepMatch>();
        int totalMatches = 0;
        bool truncated = false;

        foreach (var relativePath in files)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            var fullPath = Path.Combine(baseDir, relativePath);
            if (!File.Exists(fullPath))
            {
                continue;
            }

            // Skip binary files
            if (IsBinaryFile(fullPath))
            {
                continue;
            }

            try
            {
                var fileMatches = SearchFile(fullPath, relativePath, regex, input, ref totalMatches, input.MaxResults - matches.Count);

                if (input.FilesOnly && fileMatches.Count > 0)
                {
                    // In FilesOnly mode, we just need one match per file
                    matches.Add(fileMatches[0]);
                }
                else
                {
                    matches.AddRange(fileMatches);
                }

                if (matches.Count >= input.MaxResults)
                {
                    truncated = true;
                    break;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Skip files that cause regex timeout
                continue;
            }
            catch (IOException)
            {
                // Skip files that can't be read
                continue;
            }
        }

        return Task.FromResult(new GrepOutput(matches.ToArray(), totalMatches, truncated));
    }

    private string[] GetFilesToSearch(string? filePattern)
    {
        var matcher = new Matcher();

        if (string.IsNullOrEmpty(filePattern))
        {
            matcher.AddInclude("**/*");
        }
        else
        {
            matcher.AddInclude(filePattern);
        }

        // Exclude common non-text directories
        matcher.AddExclude("**/node_modules/**");
        matcher.AddExclude("**/.git/**");
        matcher.AddExclude("**/bin/**");
        matcher.AddExclude("**/obj/**");

        var directoryInfo = new DirectoryInfo(baseDir);
        var result = matcher.Execute(new DirectoryInfoWrapper(directoryInfo));

        return result.Files.Select(f => f.Path).ToArray();
    }

    private static bool IsBinaryFile(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[BinaryCheckSize];
            int bytesRead = stream.Read(buffer, 0, BinaryCheckSize);

            for (int i = 0; i < bytesRead; i++)
            {
                if (buffer[i] == 0)
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            // If we can't read the file, assume it's not binary
            return false;
        }
    }

    private List<GrepMatch> SearchFile(
        string fullPath,
        string relativePath,
        Regex regex,
        GrepInput input,
        ref int totalMatches,
        int maxToReturn)
    {
        var matches = new List<GrepMatch>();
        var lines = File.ReadAllLines(fullPath);

        for (int i = 0; i < lines.Length; i++)
        {
            if (regex.IsMatch(lines[i]))
            {
                totalMatches++;

                if (matches.Count < maxToReturn)
                {
                    int lineNumber = i + 1; // 1-indexed
                    string content;

                    if (input.FilesOnly)
                    {
                        // For FilesOnly mode, just return the file path
                        content = "";
                        matches.Add(new GrepMatch(relativePath, lineNumber, content));
                        break; // Only need one match per file
                    }
                    else if (input.ContextBefore > 0 || input.ContextAfter > 0)
                    {
                        content = BuildContextContent(lines, i, input.ContextBefore, input.ContextAfter);
                    }
                    else
                    {
                        content = $"{lineNumber}: {lines[i]}";
                    }

                    matches.Add(new GrepMatch(relativePath, lineNumber, content));
                }
            }
        }

        return matches;
    }

    private static string BuildContextContent(string[] lines, int matchIndex, int contextBefore, int contextAfter)
    {
        var sb = new StringBuilder();
        int startLine = Math.Max(0, matchIndex - contextBefore);
        int endLine = Math.Min(lines.Length - 1, matchIndex + contextAfter);

        for (int i = startLine; i <= endLine; i++)
        {
            int lineNumber = i + 1; // 1-indexed
            string marker = i == matchIndex ? ">" : " ";
            sb.AppendLine($"{marker}{lineNumber}: {lines[i]}");
        }

        return sb.ToString().TrimEnd();
    }
}
