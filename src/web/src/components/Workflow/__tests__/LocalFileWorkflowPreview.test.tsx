import type { ReactNode } from "react";
import type { BakabaseModulesWorkflowAbstractionsModelsViewWorkflowDefinitionViewModel as Definition } from "@/sdk/Api";
import { render, screen } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import LocalFileWorkflowPreview from "../LocalFileWorkflowPreview";

vi.mock("../Activities", () => ({ getWorkflowActivityUI: () => undefined }));
vi.mock("../displayNames", () => ({ activityDisplayName: (_t: unknown, kind: string) => kind }));
vi.mock("@/components/bakaui", () => ({
  Accordion: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  AccordionItem: ({ title, children }: { title: string; children: ReactNode }) => (
    <section aria-label={title}>{children}</section>
  ),
}));
vi.mock("react-i18next", async () => {
  const translations = (await import("@/locales/en/pages/workflow.json")).default;
  return {
    useTranslation: () => ({
      t: (key: string, values: Record<string, unknown> = {}) =>
        String(
          translations[key as keyof typeof translations] ?? values.defaultValue ?? key,
        ).replace(/\{\{(\w+)\}\}/g, (_, name: string) => String(values[name] ?? "")),
    }),
  };
});

it("previews literal replacement and deletion as different explicit instructions", () => {
  const workflow: Definition = {
    id: 1,
    name: "Local processing",
    triggerKind: "fs.processingPlan",
    enabled: true,
    isBuiltin: false,
    createdAt: "2026-10-21T15:00:00Z",
    parameters: [],
    activities: [],
  };
  render(
    <LocalFileWorkflowPreview
      workflow={workflow}
      instructions={{
        requirement: "required",
        steps: [
          { op: "renameLiteral", findText: "literal[1]", replaceText: "", selector: "*.rar" },
          { op: "renameLiteral", findText: "Before", replaceText: "After" },
        ],
      }}
    />,
  );
  expect(
    screen.getByText(
      /Remove every “literal\[1\]” from file names \(case-sensitive; directories unchanged\)/,
    ),
  ).toBeVisible();
  expect(
    screen.getByText(
      /Replace every “Before” in file names with “After” \(case-sensitive; directories unchanged\)/,
    ),
  ).toBeVisible();
});

it("previews exact directory relocation and an empty output-root target", () => {
  render(
    <LocalFileWorkflowPreview
      workflow={{ activities: [] } as unknown as Definition}
      instructions={{
        requirement: "required",
        steps: [{ op: "rebaseDirectory", sourceDirectory: "outer/author", targetDirectory: "" }],
      }}
    />,
  );
  expect(
    screen.getByText(
      /Move contents from outer\/author to this step’s output root, preserving their directory structure and other files/,
    ),
  ).toBeVisible();
});
