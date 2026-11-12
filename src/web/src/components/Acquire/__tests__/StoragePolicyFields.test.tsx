import { expect, it } from "vitest";
import {
  defaultStoragePolicy,
  resolveStoragePolicy,
  storagePolicyIsInvalid,
  storageTemplateIsInvalid,
} from "../StoragePolicyFields";

it("retains operation identities and nested attempt identity in editable templates", () => {
  expect(storagePolicyIsInvalid(defaultStoragePolicy)).toBe(false);
  expect(storageTemplateIsInvalid("{Provider}/{Title} [D{OperationId}]")).toBe(false);
  expect(storageTemplateIsInvalid("{Title}/{OperationId}/{Kind}/{Attempt}", true)).toBe(false);
  expect(storageTemplateIsInvalid("{Title}")).toBe(true);
  expect(storageTemplateIsInvalid("{Title}/{OperationId}", true)).toBe(true);
  expect(storageTemplateIsInvalid("{Title}/{TaskId}/{OperationId}")).toBe(true);
});
it("rejects unsafe or ambiguous relative paths before save", () => {
  for (const value of [
    "/{OperationId}",
    "../{OperationId}",
    "C:/{OperationId}",
    "a//{OperationId}",
    "CON/{OperationId}",
    "{OperationId}/tail.",
    "{OperationId}\\title",
    "a/b/c/d/e/{OperationId}",
  ])
    expect(storageTemplateIsInvalid(value)).toBe(true);
});
it("nullable overrides inherit while false and an empty workspace remain explicit", () => {
  const parent = {
    ...defaultStoragePolicy,
    processingWorkspaceDirectory: "/NAS/work",
    cleanupIntermediates: true,
  };
  const value = resolveStoragePolicy(parent, {
    cleanupIntermediates: false,
    processingWorkspaceDirectory: "",
    sourceCleanup: null,
  } as any);
  expect(value.cleanupIntermediates).toBe(false);
  expect(value.processingWorkspaceDirectory).toBe("");
  expect(value.sourceCleanup).toBe("keep");
  expect(resolveStoragePolicy(value, null)).toEqual(value);
});

it("rejects templates whose rendered UTF-8 names cannot fit the storage limit", () => {
  expect(storageTemplateIsInvalid("{Title}{Title}{Title}{OperationId}")).toBe(true);
  expect(storageTemplateIsInvalid("名".repeat(70) + "{OperationId}")).toBe(true);
  expect(storageTemplateIsInvalid("{Title}-{OperationId}")).toBe(false);
});
