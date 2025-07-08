import type { TypeSpecProject } from "./ts-types.js";

export interface Tools {

  readonly onboarding: {
    /**
     * Converts an existing Azure service swagger definition to a TypeSpec project.
     * This command should only be ran once to get started working on a TypeSpec project.
     * Verify whether the source swagger describes an Azure Resource Management (ARM) API
     * or a data plane API if unsure.
     */
    convertSwagger(
      pathToSwaggerReadme: string,
      outputDirectory: string,
      isAzureResourceManagement?: boolean,
      fullyCompatible?: boolean,
    ): TypeSpecProject | Promise<TypeSpecProject>;

  };
}

export let toolHandler: Tools = undefined as any;

export function setToolHandler(handler: Tools) {
  toolHandler = handler;
}