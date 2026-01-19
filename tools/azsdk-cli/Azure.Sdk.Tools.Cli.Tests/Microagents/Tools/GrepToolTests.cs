using Azure.Sdk.Tools.Cli.Microagents.Tools;
using Azure.Sdk.Tools.Cli.Tests.TestHelpers;

namespace Azure.Sdk.Tools.Cli.Tests.Microagents.Tools;

internal class GrepToolTests
{
    private TempDirectory? _temp;
    private string baseDir => _temp!.DirectoryPath;

    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        _temp = TempDirectory.Create("greptooltests");

        // Directory structure:
        // baseDir/
        //   file1.tsp           - contains "model Employee"
        //   file2.tsp           - contains "model Manager"
        //   readme.md           - contains "Hello World" and "HELLO lowercase"
        //   subdir/
        //     nested.tsp        - contains "model Employee" (duplicate for counting)
        //     config.json       - contains "employee_id"

        File.WriteAllText(Path.Combine(baseDir, "file1.tsp"), "model Employee {\n  name: string;\n  age: int32;\n}");
        File.WriteAllText(Path.Combine(baseDir, "file2.tsp"), "model Manager {\n  department: string;\n}");
        File.WriteAllText(Path.Combine(baseDir, "readme.md"), "Hello World\nThis is a test.\nHELLO lowercase test.");

        var subdir = Path.Combine(baseDir, "subdir");
        Directory.CreateDirectory(subdir);
        File.WriteAllText(Path.Combine(subdir, "nested.tsp"), "// Another file\nmodel Employee {\n  id: string;\n}");
        File.WriteAllText(Path.Combine(subdir, "config.json"), "{\n  \"employee_id\": \"12345\"\n}");
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _temp?.Dispose();
    }

    [Test]
    public async Task Grep_SimpleStringPattern_FindsMatches()
    {
        // Arrange
        var tool = new GrepTool(baseDir);

        // Act
        var result = await tool.Invoke(new GrepInput("Employee"), CancellationToken.None);

        // Assert
        Assert.That(result.TotalMatches, Is.EqualTo(2));
        Assert.That(result.Matches.Length, Is.EqualTo(2));
        Assert.That(result.Matches.Select(m => m.File), Has.Some.Contains("file1.tsp"));
        Assert.That(result.Matches.Select(m => m.File), Has.Some.Contains("nested.tsp"));
    }

    [Test]
    public async Task Grep_RegexPattern_FindsMatches()
    {
        // Arrange
        var tool = new GrepTool(baseDir);

        // Act
        var result = await tool.Invoke(new GrepInput(@"model\s+\w+"), CancellationToken.None);

        // Assert
        Assert.That(result.TotalMatches, Is.GreaterThanOrEqualTo(3));
    }

    [Test]
    public async Task Grep_CaseInsensitiveSearch_FindsMatches()
    {
        // Arrange
        var tool = new GrepTool(baseDir);

        // Act
        var result = await tool.Invoke(new GrepInput("hello", IgnoreCase: true), CancellationToken.None);

        // Assert
        Assert.That(result.TotalMatches, Is.EqualTo(2));
        Assert.That(result.Matches.All(m => m.File.Contains("readme.md")), Is.True);
    }

    [Test]
    public async Task Grep_CaseSensitiveSearch_FindsOnlyExactMatch()
    {
        // Arrange
        var tool = new GrepTool(baseDir);

        // Act
        var result = await tool.Invoke(new GrepInput("Hello", IgnoreCase: false), CancellationToken.None);

        // Assert
        Assert.That(result.TotalMatches, Is.EqualTo(1));
    }

    [Test]
    public async Task Grep_ContextLinesBefore_IncludesContextInContent()
    {
        // Arrange
        var tool = new GrepTool(baseDir);

        // Act
        var result = await tool.Invoke(new GrepInput("age:", ContextBefore: 1), CancellationToken.None);

        // Assert
        Assert.That(result.Matches.Length, Is.EqualTo(1));
        var match = result.Matches[0];
        Assert.That(match.Content, Does.Contain("name:"));
        Assert.That(match.Content, Does.Contain("age:"));
    }

    [Test]
    public async Task Grep_ContextLinesAfter_IncludesContextInContent()
    {
        // Arrange
        var tool = new GrepTool(baseDir);

        // Act
        var result = await tool.Invoke(new GrepInput("name:", ContextAfter: 1), CancellationToken.None);

        // Assert
        Assert.That(result.Matches.Length, Is.EqualTo(1));
        var match = result.Matches[0];
        Assert.That(match.Content, Does.Contain("name:"));
        Assert.That(match.Content, Does.Contain("age:"));
    }

    [Test]
    public async Task Grep_FilesOnlyMode_ReturnsOnlyFilePaths()
    {
        // Arrange
        var tool = new GrepTool(baseDir);

        // Act
        var result = await tool.Invoke(new GrepInput("Employee", FilesOnly: true), CancellationToken.None);

        // Assert
        Assert.That(result.Matches.Length, Is.EqualTo(2));
        Assert.That(result.Matches.All(m => string.IsNullOrEmpty(m.Content)), Is.True);
    }

    [Test]
    public async Task Grep_MaxResultsTruncation_TruncatesResults()
    {
        // Arrange
        var tool = new GrepTool(baseDir);

        // Act - search for "model" which appears 3 times, but limit to 2
        var result = await tool.Invoke(new GrepInput("model", MaxResults: 2), CancellationToken.None);

        // Assert
        Assert.That(result.Matches.Length, Is.EqualTo(2));
        Assert.That(result.Truncated, Is.True);
        Assert.That(result.TotalMatches, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public async Task Grep_FileFilteringWithGlob_FiltersFiles()
    {
        // Arrange
        var tool = new GrepTool(baseDir);

        // Act - search for "Employee" but only in .tsp files at top level
        var result = await tool.Invoke(new GrepInput("Employee", FilePattern: "*.tsp"), CancellationToken.None);

        // Assert
        Assert.That(result.Matches.Length, Is.EqualTo(1));
        Assert.That(result.Matches[0].File, Is.EqualTo("file1.tsp"));
    }

    [Test]
    public async Task Grep_NoMatches_ReturnsEmpty()
    {
        // Arrange
        var tool = new GrepTool(baseDir);

        // Act
        var result = await tool.Invoke(new GrepInput("NonExistentPattern12345"), CancellationToken.None);

        // Assert
        Assert.That(result.Matches, Is.Empty);
        Assert.That(result.TotalMatches, Is.EqualTo(0));
        Assert.That(result.Truncated, Is.False);
    }

    [Test]
    public void Grep_NullOrEmptyPattern_ThrowsArgumentException()
    {
        // Arrange
        var tool = new GrepTool(baseDir);

        // Act / Assert
        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await tool.Invoke(new GrepInput(""), CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("Pattern cannot be null or empty"));
    }

    [Test]
    public void Grep_InvalidRegexPattern_ThrowsArgumentException()
    {
        // Arrange
        var tool = new GrepTool(baseDir);

        // Act / Assert
        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await tool.Invoke(new GrepInput("[invalid"), CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("Invalid regex pattern"));
    }

    [Test]
    public async Task Grep_LineNumbersAre1Indexed_CorrectLineNumbers()
    {
        // Arrange
        var tool = new GrepTool(baseDir);

        // Act - "age:" is on line 3 of file1.tsp
        var result = await tool.Invoke(new GrepInput("age:"), CancellationToken.None);

        // Assert
        Assert.That(result.Matches.Length, Is.EqualTo(1));
        Assert.That(result.Matches[0].Line, Is.EqualTo(3));
    }
}
