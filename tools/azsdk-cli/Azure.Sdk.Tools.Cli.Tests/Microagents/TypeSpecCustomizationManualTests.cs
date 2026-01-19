// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Sdk.Tools.Cli.Helpers;
using Azure.Sdk.Tools.Cli.Microagents;
using Azure.Sdk.Tools.Cli.Microagents.Tools;
using Azure.Sdk.Tools.Cli.Prompts.Templates;
using Azure.Sdk.Tools.Cli.Tests.TestHelpers;
using Moq;

namespace Azure.Sdk.Tools.Cli.Tests.Microagents;

/// <summary>
/// Manual smoke tests for the TypeSpec Customization microagent components.
/// These tests require a real TypeSpec project with dependencies installed.
/// Run these manually to verify end-to-end functionality.
/// </summary>
[TestFixture]
internal class TypeSpecCustomizationManualTests
{
    // CONFIGURE THIS: Path to a TypeSpec project with dependencies installed
    private const string TypeSpecProjectPath = "/home/cradek/workplace/github/chrisradek/azure-rest-api-specs-2/specification/widget/data-plane/WidgetAnalytics";
    
    // Path to the reference documentation (absolute path for manual tests)
    private const string ReferenceDocPath = "/home/cradek/workplace/github/chrisradek/azure-sdk-tools/azsdk-client-tsp-microagent/eng/common/knowledge/customizing-client-tsp.md";

    [Test]
    [Ignore("Manual test - requires real TypeSpec project with dependencies installed")]
    public async Task CompileTypeSpecTool_RealProject_CompilesSuccessfully()
    {
        // Arrange
        var logger = new TestLogger<NpxHelper>();
        var outputHelper = Mock.Of<IRawOutputHelper>();
        var npxHelper = new NpxHelper(logger, outputHelper);
        var tool = new CompileTypeSpecTool(TypeSpecProjectPath, npxHelper);

        // Act
        var result = await tool.Invoke(new CompileTypeSpecInput(), CancellationToken.None);

        // Assert
        Console.WriteLine($"Success: {result.Success}");
        Console.WriteLine($"Output: {result.Output}");
        Assert.That(result.Success, Is.True, $"Compilation failed: {result.Output}");
    }

    [Test]
    [Ignore("Manual test - requires real TypeSpec project with dependencies installed")]
    public async Task CompileTypeSpecTool_InvalidClientTsp_ReturnsErrors()
    {
        // Arrange - Create a temp copy with invalid client.tsp
        using var tempDir = TempDirectory.Create("tsp-compile-test");
        
        // Copy the project files
        CopyDirectory(TypeSpecProjectPath, tempDir.DirectoryPath);
        
        // Write invalid client.tsp
        var clientTspPath = Path.Combine(tempDir.DirectoryPath, "client.tsp");
        await File.WriteAllTextAsync(clientTspPath, """
            import "./main.tsp";
            import "@azure-tools/typespec-client-generator-core";
            
            using Azure.ClientGenerator.Core;
            
            // Invalid syntax - missing semicolon and bad decorator usage
            @@clientName(NonExistentType, "NewName")
            """);

        var logger = new TestLogger<NpxHelper>();
        var outputHelper = Mock.Of<IRawOutputHelper>();
        var npxHelper = new NpxHelper(logger, outputHelper);
        var tool = new CompileTypeSpecTool(tempDir.DirectoryPath, npxHelper);

        // Act
        var result = await tool.Invoke(new CompileTypeSpecInput(), CancellationToken.None);

        // Assert
        Console.WriteLine($"Success: {result.Success}");
        Console.WriteLine($"Output: {result.Output}");
        Assert.That(result.Success, Is.False, "Expected compilation to fail");
        Assert.That(result.Output, Is.Not.Empty, "Expected error output");
    }

    [Test]
    [Ignore("Manual test - requires reference doc to exist")]
    public void TypeSpecCustomizationTemplate_RealReferenceDoc_BuildsPrompt()
    {
        // Arrange
        var template = new TypeSpecCustomizationTemplate(
            customizationRequest: "Rename the Widgets interface to AzureWidgets for all languages",
            typespecProjectPath: TypeSpecProjectPath,
            referenceDocPath: ReferenceDocPath);

        // Act
        var prompt = template.BuildPrompt();

        // Assert
        Console.WriteLine("=== GENERATED PROMPT ===");
        Console.WriteLine(prompt);
        Console.WriteLine("=== END PROMPT ===");
        Console.WriteLine($"\nPrompt length: {prompt.Length} characters");

        Assert.That(prompt, Does.Contain("TypeSpec Client Customizations Reference"));
        Assert.That(prompt, Does.Contain("Rename the Widgets interface"));
        Assert.That(prompt, Does.Contain(TypeSpecProjectPath));
    }

    [Test]
    [Ignore("Manual test - full integration requires LLM")]
    public async Task FullIntegration_AllComponentsWorkTogether()
    {
        // This test verifies all components can be assembled together
        // It doesn't actually run the LLM, but verifies the setup is correct
        
        // 1. Verify reference doc exists
        Assert.That(File.Exists(ReferenceDocPath), Is.True, 
            $"Reference doc not found at: {ReferenceDocPath}");

        // 2. Verify TypeSpec project exists
        Assert.That(Directory.Exists(TypeSpecProjectPath), Is.True,
            $"TypeSpec project not found at: {TypeSpecProjectPath}");

        // 3. Build the template
        var template = new TypeSpecCustomizationTemplate(
            customizationRequest: "Add @clientName decorator to rename Widget to AzureWidget",
            typespecProjectPath: TypeSpecProjectPath,
            referenceDocPath: ReferenceDocPath);
        var prompt = template.BuildPrompt();
        Assert.That(prompt.Length, Is.GreaterThan(1000), "Prompt seems too short");

        // 4. Create the tools
        var logger = new TestLogger<NpxHelper>();
        var outputHelper = Mock.Of<IRawOutputHelper>();
        var npxHelper = new NpxHelper(logger, outputHelper);
        
        var readFileTool = new ReadFileTool(TypeSpecProjectPath);
        var writeFileTool = new WriteFileTool(TypeSpecProjectPath);
        var compileTypeSpecTool = new CompileTypeSpecTool(TypeSpecProjectPath, npxHelper);

        // 5. Verify tools work
        var clientTspContent = await readFileTool.Invoke(
            new ReadFileInput("client.tsp"), CancellationToken.None);
        Console.WriteLine($"Current client.tsp:\n{clientTspContent.FileContent}");
        Assert.That(clientTspContent.FileContent, Does.Contain("import"));

        var compileResult = await compileTypeSpecTool.Invoke(
            new CompileTypeSpecInput(), CancellationToken.None);
        Console.WriteLine($"Compile result: {compileResult.Success}");
        Assert.That(compileResult.Success, Is.True, $"Initial compile failed: {compileResult.Output}");

        // 6. Create result type (just verify it compiles)
        var result = new TypeSpecCustomizationResult
        {
            Success = true,
            ChangesSummary = ["Test change applied"],
            FailureReason = null
        };
        Assert.That(result.Success, Is.True);

        Console.WriteLine("\n=== All components verified successfully ===");
        Console.WriteLine($"- Template generates {prompt.Length} char prompt");
        Console.WriteLine($"- ReadFileTool can read client.tsp");
        Console.WriteLine($"- CompileTypeSpecTool compiles successfully");
        Console.WriteLine($"- TypeSpecCustomizationResult can be created");
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectory(dir, destSubDir);
        }
    }
}
