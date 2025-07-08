import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { spawn } from "cross-spawn";
import { stat } from "node:fs/promises";
import { extname } from "node:path";
import { server } from "./generated/index.js";
import { setToolHandler } from "./generated/tools.js";
import { registerPrompts } from "./prompts.js";

setToolHandler({
  onboarding: {
    async convertSwagger(pathToSwaggerReadme, outputDirectory, isAzureResourceManagement, fullyCompatible) {
      // Step 1: Validate inputs
      await validatePathToSwaggerReadme(pathToSwaggerReadme);
      await validateOutputDirectory(outputDirectory);

      return callConvert(pathToSwaggerReadme, outputDirectory, isAzureResourceManagement, fullyCompatible);
    },
  },
});

registerPrompts(server);

const transport = new StdioServerTransport();
await server.connect(transport);

async function validatePathToSwaggerReadme(pathToSwaggerReadme: string): Promise<true> {
  // Valid README would be a markdown file
  if (extname(pathToSwaggerReadme) !== ".md") {
    throw new Error("The provided `pathToSwaggerReadme` must be a markdown file with a .md extension.");
  }
  const fileStats = await stat(pathToSwaggerReadme);
  if (!fileStats.isFile()) {
    throw new Error("The provided `pathToSwaggerReadme` does not point to a file.");
  }

  return true;
}

async function validateOutputDirectory(outputDirectory: string): Promise<true> {
  // Validate that the output directory exists and is a directory
  const dirStats = await stat(outputDirectory);
  if (!dirStats.isDirectory()) {
    throw new Error("The provided `outputDirectory` does not point to a directory.");
  }

  return true;
}

async function callConvert(
  pathToSwaggerReadme: string,
  outputDirectory: string,
  isAzureResourceManagement?: boolean,
  fullyCompatible?: boolean,
): Promise<{ pathToProject: string }> {
  const args: string[] = [
    "tsp-client",
    "convert",
    "--swagger-readme",
    pathToSwaggerReadme,
    "--output-dir",
    outputDirectory,
  ];

  if (isAzureResourceManagement) {
    args.push("--arm");
  }
  if (fullyCompatible) {
    args.push("--fully-compatible");
  }

  return new Promise((resolve, reject) => {
    const tspClient = spawn("npx", args);
    tspClient.once("exit", (code) => {
      if (code === 0) {
        resolve({ pathToProject: outputDirectory });
      } else {
        // TODO: Get error from command
        reject(new Error(`tsp-client convert failed with exit code ${code}`));
      }
    });

    tspClient.once("error", (err) => {
      reject(new Error(`tsp-client convert failed with error: ${err}`));
    });
  });
}
