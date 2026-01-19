using Azure.Sdk.Tools.Cli.Microagents.Tools;
using Azure.Sdk.Tools.Cli.Tests.TestHelpers;

namespace Azure.Sdk.Tools.Cli.Tests.Microagents.Tools;

internal class GlobToolTests
{
    private TempDirectory? _temp;
    private string baseDir => _temp!.DirectoryPath;

    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        _temp = TempDirectory.Create("globtooltests");

        // Directory structure:
        // baseDir/
        //   file1.tsp
        //   file2.tsp
        //   config.json
        //   subdir/
        //     nested.tsp
        //     settings.json
        //     deep/
        //       model.tsp

        File.WriteAllText(Path.Combine(baseDir, "file1.tsp"), "model File1 {}");
        File.WriteAllText(Path.Combine(baseDir, "file2.tsp"), "model File2 {}");
        File.WriteAllText(Path.Combine(baseDir, "config.json"), "{}");

        var subdir = Path.Combine(baseDir, "subdir");
        Directory.CreateDirectory(subdir);
        File.WriteAllText(Path.Combine(subdir, "nested.tsp"), "model Nested {}");
        File.WriteAllText(Path.Combine(subdir, "settings.json"), "{}");

        var deep = Path.Combine(subdir, "deep");
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(deep, "model.tsp"), "model Deep {}");
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _temp?.Dispose();
    }

    [Test]
    public async Task Glob_FindAllTspFiles_ReturnsAllTspFiles()
    {
        // Arrange
        var tool = new GlobTool(baseDir);

        // Act
        var result = await tool.Invoke(new GlobInput("**/*.tsp"), CancellationToken.None);

        // Assert
        var expected = new[]
        {
            "file1.tsp",
            "file2.tsp",
            Path.Join("subdir", "nested.tsp"),
            Path.Join("subdir", "deep", "model.tsp"),
        };
        Assert.That(result.Files, Is.EquivalentTo(expected));
    }

    [Test]
    public async Task Glob_FindFilesInSpecificDirectory_ReturnsFilesInThatDirectory()
    {
        // Arrange
        var tool = new GlobTool(baseDir);

        // Act
        var result = await tool.Invoke(new GlobInput("subdir/*.tsp"), CancellationToken.None);

        // Assert
        var expected = new[] { Path.Join("subdir", "nested.tsp") };
        Assert.That(result.Files, Is.EquivalentTo(expected));
    }

    [Test]
    public async Task Glob_NonMatchingPattern_ReturnsEmptyResult()
    {
        // Arrange
        var tool = new GlobTool(baseDir);

        // Act
        var result = await tool.Invoke(new GlobInput("**/*.nonexistent"), CancellationToken.None);

        // Assert
        Assert.That(result.Files, Is.Empty);
    }

    [Test]
    public async Task Glob_BraceExpansionNotSupported_ReturnsEmpty()
    {
        // Note: Microsoft.Extensions.FileSystemGlobbing doesn't support brace expansion {tsp,json}
        // This test documents the actual behavior
        // Arrange
        var tool = new GlobTool(baseDir);

        // Act
        var result = await tool.Invoke(new GlobInput("**/*.{tsp,json}"), CancellationToken.None);

        // Assert - brace expansion is not supported, returns empty
        Assert.That(result.Files, Is.Empty);
    }

    [Test]
    public void Glob_NullOrEmptyPattern_ThrowsArgumentException()
    {
        // Arrange
        var tool = new GlobTool(baseDir);

        // Act / Assert
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await tool.Invoke(new GlobInput(""), CancellationToken.None));
    }

    [Test]
    public async Task Glob_TopLevelFilesOnly_ReturnsOnlyTopLevelFiles()
    {
        // Arrange
        var tool = new GlobTool(baseDir);

        // Act
        var result = await tool.Invoke(new GlobInput("*.tsp"), CancellationToken.None);

        // Assert
        var expected = new[] { "file1.tsp", "file2.tsp" };
        Assert.That(result.Files, Is.EquivalentTo(expected));
    }
}
