import type { ReactNode } from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import PlanRevisionHistory, { planRevisionReason } from "../PlanRevisionHistory";
import type { TFunction } from "i18next";
import cnAcquisition from "@/locales/cn/pages/acquisition.json";
import enAcquisition from "@/locales/en/pages/acquisition.json";
import cnAcquire from "@/locales/cn/components/acquire.json";
import enAcquire from "@/locales/en/components/acquire.json";
import type { PlanTaskDetail } from "../models";

vi.mock("@/components/bakaui", () => ({
  Tooltip: ({ children }: { children: ReactNode }) => <>{children}</>,
  Accordion: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  AccordionItem: ({ title, children }: { title: string; children: ReactNode }) => (
    <section aria-label={title}>{children}</section>
  ),
  Button: ({ children, onPress }: { children: ReactNode; onPress: () => void }) => (
    <button onClick={onPress}>{children}</button>
  ),
}));

it("retains each snapshot, explains the repair and opens the prior run without exposing passwords", () => {
  const history: PlanTaskDetail["planHistory"] = [
    {
      revision: 4,
      createdAt: "2026-10-21T15:00:00Z",
      previousRunId: 100,
      reason: "Observed a wrapped first volume.",
      sourceRefs: [],
      plan: {
        version: 1,
        adaptiveFileProcessing: false,
    adaptiveFileProcessingMaxIterations: 3,
    publishRetainedFiles: false,
        coverCandidates: [],
        title: "Content",
        outputDirectory: "/downloads",
        delivery: "files",
        parts: [
          {
            id: "main",
            sources: [],
            processing: {
              requirement: "required",
              evidence: [],
              steps: [
                {
                  id: "extract",
                  op: "extractArchive",
                  input: "download",
                  inputs: ["download", "move"],
                  password: "private-password-canary",
                },
              ],
            },
          },
        ],
      },
      artifactEvidence: [
        {
          partId: "main",
          artifactId: "artifact",
          rootDirectory: "/downloads/observed",
          operationId: "operation",
          kind: "download",
          fingerprint: "fingerprint",
          manifest: {
            entries: [{ relativePath: "file.rar", length: 100, sha256: "file-hash" }],
            fingerprint: "fingerprint",
          },
        },
      ],
    },
  ];
  const openRun = vi.fn();
  render(<PlanRevisionHistory history={history} onRun={openRun} />);
  expect(screen.getByText(history[0].reason!)).toBeVisible();
  expect(screen.getByText("download + move")).toBeVisible();
  expect(document.body).not.toHaveTextContent("private-password-canary");
  fireEvent.click(screen.getByRole("button", { name: "acquisition.plan.history.previousRun" }));
  expect(openRun).toHaveBeenCalledExactlyOnceWith(100);
});

it("translates the known pre-start mirror selection reason while retaining arbitrary human explanations", () => {
  const translate = vi.fn((key: string) =>
    key === "acquisition.plan.history.reason.sourceSelection.preStartFallback"
      ? "Used an available mirror before execution."
      : "unexpected translation",
  );
  expect(
    planRevisionReason("sourceSelection.preStartFallback", translate as unknown as TFunction),
  ).toBe("Used an available mirror before execution.");
  const humanReason =
    "I intentionally kept acquisition.plan.history.reason.sourceSelection.preStartFallback as evidence.";
  expect(planRevisionReason(humanReason, translate as unknown as TFunction)).toBe(humanReason);
  expect(
    planRevisionReason("sourceSelection.futureReason", translate as unknown as TFunction),
  ).toBe("sourceSelection.futureReason");
  expect(translate).toHaveBeenCalledTimes(1);
});

it("provides both languages for mirror selection and access verification requirements", () => {
  const reason = "acquisition.plan.history.reason.sourceSelection.preStartFallback";
  const requirement = "acquire.requirement.SOURCE_ACCESS_VERIFICATION_REQUIRED";
  [cnAcquisition, enAcquisition].forEach((messages) => expect(messages[reason]).toBeTruthy());
  [cnAcquire, enAcquire].forEach((messages) => expect(messages[requirement]).toBeTruthy());
});

it("translates a user-confirmed mirror revision without replacing human-entered evidence", () => {
  const translate = vi.fn((key: string) => key);
  expect(
    planRevisionReason("sourceSelection.userRevision", translate as unknown as TFunction),
  ).toBe("acquisition.plan.history.reason.sourceSelection.userRevision");
  const evidence = "Confirmed this mirror in the original post.";
  expect(planRevisionReason(evidence, translate as unknown as TFunction)).toBe(evidence);
  for (const messages of [cnAcquisition, enAcquisition])
    expect(messages["acquisition.plan.history.reason.sourceSelection.userRevision"]).toBeTruthy();
});

it("translates the default as-is delivery note and preserves a task's custom scope note", () => {
  const translate = vi.fn((key: string) => key);
  const key = "acquisition.plan.history.reason.delivery.keepPreparedFiles";
  expect(planRevisionReason("delivery.keepPreparedFiles", translate as unknown as TFunction)).toBe(key);
  expect(planRevisionReason("Accepted 9 of 10 packages.", translate as unknown as TFunction)).toBe("Accepted 9 of 10 packages.");
  for (const messages of [cnAcquisition, enAcquisition]) expect(messages[key]).toBeTruthy();
});
