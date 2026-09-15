import { readFileSync, readdirSync } from "node:fs";
import { resolve } from "node:path";

import { createInstance } from "i18next";
import { describe, expect, it } from "vitest";

import { workflowDescription } from "../metadata";

import cnWorkflow from "@/locales/cn/pages/workflow.json";
import enWorkflow from "@/locales/en/pages/workflow.json";
import cnAcquisition from "@/locales/cn/pages/acquisition.json";
import enAcquisition from "@/locales/en/pages/acquisition.json";

const resources = {
  cn: { ...cnWorkflow, ...cnAcquisition },
  en: { ...enWorkflow, ...enAcquisition },
};
const i18n = createInstance();

await i18n.init({
  lng: "cn",
  fallbackLng: "en",
  keySeparator: false,
  resources: { cn: { translation: resources.cn }, en: { translation: resources.en } },
});

const backendSourceRoot = resolve(__dirname, "../../../../..");
const metadataSources = [
  resolve(
    backendSourceRoot,
    "modules/Bakabase.Modules.Acquisition/Components/BuiltinAcquisitionRecipes.cs",
  ),
  resolve(
    backendSourceRoot,
    "modules/Bakabase.Modules.Acquisition/Components/Workflow/AcquisitionStepActivity.cs",
  ),
  ...readdirSync(resolve(backendSourceRoot, "apps/Bakabase.Service/Components/Acquisition/Steps"))
    .filter((name) => name.endsWith(".cs"))
    .map((name) =>
      resolve(backendSourceRoot, "apps/Bakabase.Service/Components/Acquisition/Steps", name),
    ),
];

describe("authored workflow metadata", () => {
  it("localizes metadata keys while preserving literal custom descriptions and unknown-key fallbacks", () => {
    expect(workflowDescription({ description: "My custom purpose" }, i18n.t)).toBe(
      "My custom purpose",
    );
    expect(
      workflowDescription(
        { descriptionKey: "acquisition.workflow.manualMagnet.description" },
        i18n.t,
      ),
    ).toBe(resources.cn["acquisition.workflow.manualMagnet.description"]);
    expect(
      workflowDescription(
        { descriptionKey: "unregistered.description", description: "Server fallback" },
        i18n.t,
      ),
    ).toBe("Server fallback");
    expect(workflowDescription({}, i18n.t)).toBe("");
  });

  it("resolves description and diagnostic keys supplied by backend metadata in both languages", () => {
    const keys = new Set(
      metadataSources.flatMap((path) =>
        [
          ...readFileSync(path, "utf8").matchAll(
            /"((?:workflow\.(?:activity|validation)\.acquisition|acquisition\.workflow)\.[\w.]+)"/g,
          ),
        ].map((match) => match[1]),
      ),
    );

    expect(keys.size).toBeGreaterThanOrEqual(7);
    for (const [language, dictionary] of Object.entries(resources)) {
      for (const key of keys) expect(dictionary, `${language}: ${key}`).toHaveProperty(key);
    }
  });
});
