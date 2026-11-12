import { expect, it, vi } from "vitest";
vi.mock("@/components/bakaui", () => ({ Button: () => null, Input: () => null }));
vi.mock("@/components/FileSystemSelector/components/Button", () => ({ default: () => null }));
import { deliveryTemplateIsInvalid } from "../DeliverySettingsFields";

it.each([
  "{Title} [A{TaskId}]",
  "games/{Title} [A{TaskId}]",
  "archive/[A{TaskId}]",
])("permits customized task prefixes with the fixed identity suffix: %s", (template) => {
  expect(deliveryTemplateIsInvalid(template)).toBe(false);
});

it.each([
  "{Title}{TaskId}",
  "{Title} [A{TaskId}1]",
  "{Title} [A{TaskId}] ",
  "{TaskId}",
  "{TaskId} [A{TaskId}]",
  "../{Title} [A{TaskId}]",
  "/{Title} [A{TaskId}]",
  "folder\\{Title} [A{TaskId}]",
  "CON/{Title} [A{TaskId}]",
  "{Unknown} [A{TaskId}]",
])("rejects unsafe or unstable task templates consistently with the server: %s", (template) => {
  expect(deliveryTemplateIsInvalid(template)).toBe(true);
});

it("keeps part identities exact across template versions without requiring a title", () => {
  expect(deliveryTemplateIsInvalid("{PartTitle} [{PartId}]", true)).toBe(false);
  expect(deliveryTemplateIsInvalid("files/[{PartId}]", true)).toBe(false);
  expect(deliveryTemplateIsInvalid("{PartTitle} [{PartId}1]", true)).toBe(true);
  expect(deliveryTemplateIsInvalid("{PartId} [{PartId}]", true)).toBe(true);
  expect(deliveryTemplateIsInvalid("{Title} [{PartId}]", true)).toBe(true);
});
