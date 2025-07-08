import { zodToJsonSchema } from "zod-to-json-schema";
import { onboardingConvertSwaggerToolZodSchemas } from "./zod.js";

export const onboardingConvertSwaggerToolJsonSchemas = {
  parameters: zodToJsonSchema(
    onboardingConvertSwaggerToolZodSchemas.parameters,
    {
      $refStrategy: "none",
    }
  ),
}