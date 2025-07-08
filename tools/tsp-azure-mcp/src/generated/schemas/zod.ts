import { z } from "zod";

export const typeSpecProject = z.object({
  pathToProject: z.string().describe("The path to the TypeSpec project."),
});

export const onboardingConvertSwaggerToolZodSchemas = {
  parameters: z.object({
    pathToSwaggerReadme: z
      .string()
      .describe("The path or URL to an Azure swagger README file."),
    outputDirectory: z
      .string()

        .describe("The output directory for the generated TypeSpec project. This directory must already exist."),
    isAzureResourceManagement: z
      .boolean()
      .optional()

        .describe("Whether the generated TypeSpec project is for an Azure Resource Management (ARM) API. This should be true if the swagger's path contains `resource-manager`."),
    fullyCompatible: z
      .boolean()
      .optional()
      .default(false)

        .describe("Whether to generate a TypeSpec project that is fully compatible with the swagger. It is recommended not to set this to `true` so that the converted TypeSpec project leverages TypeSpec built-in libraries with standard patterns and templates."),
  }),
  returnType: typeSpecProject,
}