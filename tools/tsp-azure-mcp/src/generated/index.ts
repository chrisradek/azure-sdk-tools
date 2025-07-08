import { fromZodError } from "zod-validation-error";
import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { CallToolRequestSchema, ListToolsRequestSchema } from "@modelcontextprotocol/sdk/types.js";
import { onboardingConvertSwaggerToolJsonSchemas } from "./schemas/json-schemas.js";
import { onboardingConvertSwaggerToolZodSchemas } from "./schemas/zod.js";
import { toolHandler } from "./tools.js";

export const server = new Server(
  {
    name: "TypeSpec Azure MCP",
    version: "0.1.0",
    instructions: "Use this MCP server to onboard Azure services to TypeSpec.\nThis server supports converting existing Azure services to TypeSpec,\nand scaffolding new TypeSpec projects for Azure services.\n- DO NOT pass optional parameters if they are empty. DO NOT PASS an empty string",
  },
  {
    capabilities: {
      tools: {},
    },
  }
)

server.setRequestHandler(
  ListToolsRequestSchema,
  async function listTools(request) {
    return {
      tools: [
        {
          name: "onboarding_convert_swagger",
          description: "Converts an existing Azure service swagger definition to a TypeSpec project.\nThis command should only be ran once to get started working on a TypeSpec project.\nVerify whether the source swagger describes an Azure Resource Management (ARM) API\nor a data plane API if unsure.",
          inputSchema: onboardingConvertSwaggerToolJsonSchemas.parameters,
          annotations: {
            readonlyHint: false,
            destructiveHint: true,
            idempotentHint: false,
            openWorldHint: true,
          },
        }
      ],
    };
  }
)

server.setRequestHandler(
  CallToolRequestSchema,
  async function callTool(request) {
    const name = request.params.name;
    const args = request.params.arguments;
    switch (name) {
      case "onboarding_convert_swagger": {
        const parsed = onboardingConvertSwaggerToolZodSchemas.parameters.safeParse(args);
        if (!parsed.success) {
          throw fromZodError(parsed.error, { prefix: "Request validation error" });
        }
        const rawResult = await toolHandler.onboarding.convertSwagger(
          parsed.data.pathToSwaggerReadme,
          parsed.data.outputDirectory,
          parsed.data.isAzureResourceManagement,
          parsed.data.fullyCompatible
        );
        const maybeResult = onboardingConvertSwaggerToolZodSchemas.returnType.safeParse(rawResult);
        if (!maybeResult.success) {
          throw fromZodError(maybeResult.error, { prefix: "Response validation error"});
        };
        const result = maybeResult.data;
        return {
          content: [
            {
              type: "text",
              text: JSON.stringify(result, null, 2),
            }
          ],
        };
      }
    };
    return { content: [{ type: "text", text: "Unknown tool" }] };
  }
)