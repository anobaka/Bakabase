import { describe, expect, it } from "vitest";
import {
  configFields,
  parameterBindingsAreValid,
  parameterDefinitionsAreValid,
  parameterValueIsValid,
  preserveConfigFields,
  type WorkflowParameter,
} from "../model";

const parameter = (changes: Partial<WorkflowParameter> = {}): WorkflowParameter => ({
  key: "target",
  type: "string",
  required: false,
  sensitive: false,
  displayInRun: false,
  ...changes,
});

describe("workflow invocation editor contract", () => {
  it("keeps falsy defaults distinct from missing required input and enforces allowed values", () => {
    expect(parameterValueIsValid(parameter({ type: "boolean", required: true }), false)).toBe(true);
    expect(parameterValueIsValid(parameter({ type: "integer", required: true }), 0)).toBe(true);
    expect(parameterValueIsValid(parameter({ required: true }), undefined)).toBe(false);
    expect(
      parameterValueIsValid(parameter({ allowedValues: ["keep", "delete"] }), "quarantine"),
    ).toBe(false);
    expect(parameterDefinitionsAreValid([parameter({ required: true })])).toBe(true);
    expect(parameterDefinitionsAreValid([parameter(), parameter()])).toBe(false);
  });

  it("binds existing object members with escaped pointers, excluding array members and unsafe keys", () => {
    const json =
      '{"workspace":{"a/b~c":"/volume"},"items":[{"path":"/private"}],"__proto__":{"unsafe":true}}';
    expect(configFields(json).map((field) => field.path)).toEqual([
      "/workspace",
      "/workspace/a~1b~0c",
      "/items",
    ]);
    expect(
      parameterBindingsAreValid(
        json,
        [{ parameterKey: "target", configPath: "/workspace/a~1b~0c" }],
        [parameter()],
      ),
    ).toBe(true);
    expect(
      parameterBindingsAreValid(
        json,
        [{ parameterKey: "target", configPath: "/items/0/path" }],
        [parameter()],
      ),
    ).toBe(false);
  });

  it("rejects removed parameters, incompatible types and overlapping parent/child bindings", () => {
    const config = '{"storage":{"cleanup":false}}';
    const parameters = [
      parameter({ key: "storage", type: "object" }),
      parameter({ key: "cleanup", type: "boolean" }),
    ];
    expect(
      parameterBindingsAreValid(
        config,
        [
          { parameterKey: "storage", configPath: "/storage" },
          { parameterKey: "cleanup", configPath: "/storage/cleanup" },
        ],
        parameters,
      ),
    ).toBe(false);
    expect(
      parameterBindingsAreValid(
        config,
        [{ parameterKey: "missing", configPath: "/storage" }],
        parameters,
      ),
    ).toBe(false);
    expect(
      parameterBindingsAreValid(
        config,
        [{ parameterKey: "cleanup", configPath: "/storage" }],
        parameters,
      ),
    ).toBe(false);
  });

  it("preserves extra backend config fields when an older node form edits known fields", () => {
    const result = JSON.parse(
      preserveConfigFields(
        '{"archivePolicy":"keep","workspaceRootDirectory":"/nas","storagePolicy":{"sourceCleanup":"keep"}}',
        '{"archivePolicy":"instructions"}',
      ),
    );
    expect(result).toEqual({
      archivePolicy: "instructions",
      workspaceRootDirectory: "/nas",
      storagePolicy: { sourceCleanup: "keep" },
    });
  });
});
