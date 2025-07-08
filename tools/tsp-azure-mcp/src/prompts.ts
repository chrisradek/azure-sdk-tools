import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { GetPromptRequestSchema, ListPromptsRequestSchema } from "@modelcontextprotocol/sdk/types.js";

const PROMPTS = {
  "convert-swagger": {
    name: "convert-swagger",
    description: "Convert an existing Azure service swagger definition to a TypeSpec project.",
    arguments: [
      {
        name: "service",
        description: "The name of the Azure service to convert to TypeSpec.",
        required: true,
      },
      {
        name: "is-arm",
        description: "Whether to prefer the ARM version of the service.",
        required: false,
      },
    ],
  },
};

export function registerPrompts(server: Server) {
  server.registerCapabilities({ prompts: {} });

  server.setRequestHandler(ListPromptsRequestSchema, async () => {
    return { prompts: Object.values(PROMPTS) };
  });

  server.setRequestHandler(GetPromptRequestSchema, async (request) => {
    const prompt = PROMPTS[request.params.name as keyof typeof PROMPTS];
    if (!prompt) {
      throw new Error(`Prompt with name '${request.params.name}' not found.`);
    }

    if (request.params.name === "convert-swagger") {
      let text = `Convert the Azure service swagger definition for ${request.params.arguments?.["service"]} to a TypeSpec project.`;
      if (request.params.arguments?.["is-arm"]) {
        text += " Prefer the ARM version of the service.";
      }
      return {
        messages: [
          {
            role: "user",
            content: {
              type: "text",
              text,
            },
          },
        ],
      };
    }

    throw new Error("Prompt implementation not found");
  });
}
