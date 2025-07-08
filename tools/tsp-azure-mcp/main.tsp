import "typespec-mcp";

using MCP;

@mcpServer(#{
  name: "TypeSpec Azure MCP",
  version: "0.1.0",
  instructions: """
    Use this MCP server to onboard Azure services to TypeSpec.
    This server supports converting existing Azure services to TypeSpec,
    and scaffolding new TypeSpec projects for Azure services.
    """,
})
namespace TspAzureMCP;

model TypeSpecProject {
  /**
   * The path to the TypeSpec project.
   */
  pathToProject: string;
}

interface Onboarding {
  /**
   * Converts an existing Azure service swagger definition to a TypeSpec project.
   * **Call this tool when trying to convert an existing Azure service to TypeSpec.**
   * This command should only be ran once to get started working on a TypeSpec project.
   * Verify whether the source swagger describes an Azure Resource Management (ARM) API
   * or a data plane API if unsure.
   */
  @tool convertSwagger(
    /**
     * The path or URL to an Azure swagger README file.
     */
    pathToSwaggerReadme: string,

    /**
     * The output directory for the generated TypeSpec project.
     * This directory must already exist.
     */
    outputDirectory: string,

    /**
     * Whether the generated TypeSpec project is for an Azure Resource Management (ARM) API.
     * This should be true if the swagger's path contains `resource-manager`.
     */
    isAzureResourceManagement?: boolean,

    /**
     * Whether to generate a TypeSpec project that is fully compatible with the swagger.
     * It is recommended not to set this to `true` so that the converted TypeSpec project
     * leverages TypeSpec built-in libraries with standard patterns and templates.
     */
    fullyCompatible?: boolean = false,
  ): TypeSpecProject;

  @tool scaffoldProject(
    /**
     * The name of the Azure service to scaffold.
     */
    serviceName: string,

    /**
     * The output directory for the generated TypeSpec project.
     * This directory must already exist.
     */
    outputDirectory: string,
  ): TypeSpecProject;
}
