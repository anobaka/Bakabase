import type { components } from "@/sdk/BApi2";

export type WorkflowParameter =
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.WorkflowParameterDefinition"];
export type ParameterBinding =
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.WorkflowParameterBinding"];
export type ParameterSource =
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowRunViewModel"]["parameterSources"][number];

export const parameterTypes = [
  "string",
  "boolean",
  "integer",
  "number",
  "object",
  "array",
] as const;

export function valueMatchesType(value: unknown, type: string): boolean {
  switch (type) {
    case "integer":
      return typeof value === "number" && Number.isSafeInteger(value);
    case "number":
      return typeof value === "number" && Number.isFinite(value);
    case "array":
      return Array.isArray(value);
    case "object":
      return value != null && typeof value === "object" && !Array.isArray(value);
    default:
      return typeof value === type;
  }
}

export function emptyParameterValue(type: string): unknown {
  return type === "boolean"
    ? false
    : type === "integer" || type === "number"
      ? 0
      : type === "array"
        ? []
        : type === "object"
          ? {}
          : "";
}

export function parameterValueIsValid(parameter: WorkflowParameter, value: unknown): boolean {
  if (value === undefined || value === null) return !parameter.required;
  return (
    valueMatchesType(value, parameter.type) &&
    (!parameter.allowedValues?.length ||
      parameter.allowedValues.some((allowed) => JSON.stringify(allowed) === JSON.stringify(value)))
  );
}

export function parameterDefinitionsAreValid(parameters: WorkflowParameter[]): boolean {
  const keys = new Set<string>();
  return parameters.every((parameter) => {
    if (!/^[A-Za-z][A-Za-z0-9_.-]*$/.test(parameter.key) || keys.has(parameter.key)) return false;
    keys.add(parameter.key);
    return (
      parameterTypes.includes(parameter.type as (typeof parameterTypes)[number]) &&
      (parameter.defaultValue == null ||
        parameterValueIsValid(parameter, parameter.defaultValue)) &&
      (parameter.allowedValues ?? []).every((value) => valueMatchesType(value, parameter.type))
    );
  });
}

export interface ConfigField {
  path: string;
  value: unknown;
}
/** Only existing object properties are bindable. Array members are deliberately excluded. */
export function configFields(configJson: string): ConfigField[] {
  try {
    const config: unknown = JSON.parse(configJson);
    const result: ConfigField[] = [];
    const walk = (value: unknown, path: string, depth: number) => {
      if (!value || typeof value !== "object" || Array.isArray(value) || depth > 12) return;
      for (const [key, child] of Object.entries(value)) {
        if (["__proto__", "prototype", "constructor"].includes(key)) continue;
        const next = `${path}/${key.replace(/~/g, "~0").replace(/\//g, "~1")}`;
        result.push({ path: next, value: child });
        walk(child, next, depth + 1);
      }
    };
    walk(config, "", 0);
    return result;
  } catch {
    return [];
  }
}

export function pathsOverlap(a: string, b: string): boolean {
  return a === b || a.startsWith(`${b}/`) || b.startsWith(`${a}/`);
}

export function parameterBindingsAreValid(
  configJson: string,
  bindings: ParameterBinding[],
  parameters: WorkflowParameter[],
): boolean {
  const fields = configFields(configJson);
  return bindings.every((binding, index) => {
    const field = fields.find((candidate) => candidate.path === binding.configPath);
    const parameter = parameters.find((candidate) => candidate.key === binding.parameterKey);
    return (
      !!field &&
      !!parameter &&
      (field.value == null || valueMatchesType(field.value, parameter.type)) &&
      !bindings
        .slice(0, index)
        .some((previous) => pathsOverlap(previous.configPath, binding.configPath))
    );
  });
}

/** Forms may know fewer fields than the server. Preserve additional root fields on edit. */
export function preserveConfigFields(previousJson: string, editedJson: string): string {
  try {
    const previous = JSON.parse(previousJson);
    const edited = JSON.parse(editedJson);
    if (
      previous &&
      edited &&
      !Array.isArray(previous) &&
      !Array.isArray(edited) &&
      typeof previous === "object" &&
      typeof edited === "object"
    )
      return JSON.stringify({ ...previous, ...edited });
  } catch {
    /* The form/server validator reports malformed JSON. */
  }
  return editedJson;
}
