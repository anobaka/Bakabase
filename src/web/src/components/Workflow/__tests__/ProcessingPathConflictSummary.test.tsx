import { cleanup, render, screen } from "@testing-library/react";
import { createInstance } from "i18next";
import { afterEach, beforeAll, expect, it, vi } from "vitest";
import ProcessingPathConflictSummary from "../ProcessingPathConflictSummary";
import cn from "@/locales/cn/pages/workflowFileRecovery.json";
import en from "@/locales/en/pages/workflowFileRecovery.json";

const i18n = createInstance();
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: i18n.t }) }));
beforeAll(async () => {
  await i18n.init({
    lng: "cn",
    keySeparator: false,
    interpolation: { escapeValue: false },
    resources: { cn: { translation: cn }, en: { translation: en } },
  });
});
afterEach(cleanup);

it.each(["cn", "en"])(
  "shows the node and exact source-to-target mappings in %s",
  async (language) => {
    await i18n.changeLanguage(language);
    render(
      <ProcessingPathConflictSummary
        conflict={{
          kind: "PlannedTargetCollision",
          stepId: "rename-archive",
          targetPath: "folder/archive.zip",
          message: "private exception detail",
          entries: [
            {
              sourcePath: "folder/archive.mp4",
              targetPath: "folder/archive.zip",
              isDirectory: false,
            },
            {
              sourcePath: "folder/archive.z01",
              targetPath: "folder/archive.zip",
              isDirectory: false,
            },
          ],
        }}
      />,
    );
    expect(screen.getByText(/rename-archive/)).toBeVisible();
    expect(screen.getByText("folder/archive.mp4")).toBeVisible();
    expect(screen.getByText("folder/archive.z01")).toBeVisible();
    expect(screen.getAllByText("folder/archive.zip")).toHaveLength(2);
    expect(screen.getAllByRole("listitem")[0]).toHaveTextContent(
      "folder/archive.mp4 → folder/archive.zip",
    );
    expect(screen.queryByText("private exception detail")).toBeNull();
    expect(document.body).not.toHaveTextContent("workflow.fileRecovery.");
  },
);

it.each([null, [], "bad data", { kind: {}, stepId: [], entries: [null, 42, { sourcePath: "x" }] }])(
  "tolerates malformed saved diagnostics: %j",
  (conflict) => {
    render(<ProcessingPathConflictSummary conflict={conflict} />);
    expect(screen.queryByRole("listitem")).toBeNull();
  },
);
