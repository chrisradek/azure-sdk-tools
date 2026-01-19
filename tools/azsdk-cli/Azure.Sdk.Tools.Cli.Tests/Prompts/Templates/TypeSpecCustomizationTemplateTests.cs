// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Sdk.Tools.Cli.Prompts.Templates;
using Azure.Sdk.Tools.Cli.Tests.TestHelpers;

namespace Azure.Sdk.Tools.Cli.Tests.Prompts.Templates;

[TestFixture]
internal class TypeSpecCustomizationTemplateTests
{
    private TempDirectory? _temp;
    private string _referenceDocPath = string.Empty;

    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        _temp = TempDirectory.Create("tsptemplatetests");
        
        // Create a mock reference doc
        _referenceDocPath = Path.Combine(_temp.DirectoryPath, "customizing-client-tsp.md");
        File.WriteAllText(_referenceDocPath, """
            # Customizing client.tsp
            
            ## @clientName decorator
            Use `@@clientName` to rename types for specific languages.
            
            ## @access decorator
            Use `@@access` to control visibility of operations.
            
            ## Example
            ```typespec
            @@clientName(MyService.OldName, "NewName");
            ```
            """);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _temp?.Dispose();
    }

    [Test]
    public void BuildPrompt_IncludesReferenceDocContent()
    {
        // Arrange
        var template = new TypeSpecCustomizationTemplate(
            customizationRequest: "Rename FooClient to BarClient",
            typespecProjectPath: "/path/to/project",
            referenceDocPath: _referenceDocPath);

        // Act
        var prompt = template.BuildPrompt();

        // Assert
        Assert.That(prompt, Does.Contain("@clientName decorator"));
        Assert.That(prompt, Does.Contain("@access decorator"));
        Assert.That(prompt, Does.Contain("@@clientName(MyService.OldName"));
    }

    [Test]
    public void BuildPrompt_IncludesCustomizationRequest()
    {
        // Arrange
        var template = new TypeSpecCustomizationTemplate(
            customizationRequest: "Rename FooClient to BarClient for .NET",
            typespecProjectPath: "/path/to/project",
            referenceDocPath: _referenceDocPath);

        // Act
        var prompt = template.BuildPrompt();

        // Assert
        Assert.That(prompt, Does.Contain("Rename FooClient to BarClient for .NET"));
    }

    [Test]
    public void BuildPrompt_IncludesProjectPath()
    {
        // Arrange
        var template = new TypeSpecCustomizationTemplate(
            customizationRequest: "Some request",
            typespecProjectPath: "/my/custom/typespec/path",
            referenceDocPath: _referenceDocPath);

        // Act
        var prompt = template.BuildPrompt();

        // Assert
        Assert.That(prompt, Does.Contain("/my/custom/typespec/path"));
    }

    [Test]
    public void BuildPrompt_IncludesTaskInstructions()
    {
        // Arrange
        var template = new TypeSpecCustomizationTemplate(
            customizationRequest: "Some request",
            typespecProjectPath: "/path/to/project",
            referenceDocPath: _referenceDocPath);

        // Act
        var prompt = template.BuildPrompt();

        // Assert - Key instructions should be present
        Assert.That(prompt, Does.Contain("client.tsp"));
        Assert.That(prompt, Does.Contain("Compile"));
        Assert.That(prompt, Does.Contain("rollback"));
    }

    [Test]
    public void BuildPrompt_IncludesConstraints()
    {
        // Arrange
        var template = new TypeSpecCustomizationTemplate(
            customizationRequest: "Some request",
            typespecProjectPath: "/path/to/project",
            referenceDocPath: _referenceDocPath);

        // Act
        var prompt = template.BuildPrompt();

        // Assert - Key constraints should be present
        Assert.That(prompt, Does.Contain("Only write to the client.tsp file"));
        Assert.That(prompt, Does.Contain("incrementally"));
    }

    [Test]
    public void TemplateMetadata_HasCorrectValues()
    {
        // Arrange
        var template = new TypeSpecCustomizationTemplate(
            customizationRequest: "Some request",
            typespecProjectPath: "/path/to/project",
            referenceDocPath: _referenceDocPath);

        // Assert
        Assert.That(template.TemplateId, Is.EqualTo("typespec-customization"));
        Assert.That(template.Version, Is.EqualTo("1.0.0"));
        Assert.That(template.Description, Is.EqualTo("Apply TypeSpec client.tsp customizations"));
    }

    [Test]
    public void BuildPrompt_ReferenceDocNotFound_Throws()
    {
        // Arrange
        var template = new TypeSpecCustomizationTemplate(
            customizationRequest: "Some request",
            typespecProjectPath: "/path/to/project",
            referenceDocPath: "/nonexistent/path/doc.md");

        // Act & Assert - File.ReadAllText throws when path doesn't exist
        var ex = Assert.Catch<Exception>(() => template.BuildPrompt());
        Assert.That(ex, Is.InstanceOf<FileNotFoundException>().Or.InstanceOf<DirectoryNotFoundException>());
    }
}
