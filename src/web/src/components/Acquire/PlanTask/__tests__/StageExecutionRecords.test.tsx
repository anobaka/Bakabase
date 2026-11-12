import { render, screen, within } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import type { ReactNode } from "react";
import StageExecutionRecords from "../StageExecutionRecords";
import type { Attempt } from "../acquisitionAttempt";
import type { AcquisitionStage } from "../AcquisitionExecutionTimeline";

vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ children, footer }: { children: ReactNode; footer: ReactNode }) => (
    <div role="dialog">
      {children}
      {footer}
    </div>
  ),
  Button: ({ onPress, children }: { onPress?: () => void; children: ReactNode }) => (
    <button onClick={onPress}>{children}</button>
  ),
  Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
}));
vi.mock("@/components/Workflow/WorkflowNodeMetadata", () => ({
  WorkflowOutputBlock: ({ block }: { block: Attempt["outputs"][number] }) => (
    <div>{block.label ?? block.code ?? block.key}</div>
  ),
}));
vi.mock("@/components/Workflow/displayNames", () => ({
  activityDisplayName: (_: unknown, kind: string) => kind,
}));
vi.mock("../AssessmentInputOutput", () => ({ default: () => null }));

const artifact = {
  kind: "artifact",
  artifactId: "saved",
  operationId: "op",
  directory: "/processed",
};
const sample: Attempt = {
  id: "505/122",
  runId: 505,
  stage: "processing",
  attempt: 4,
  status: "waiting",
  nodes: ["fs.assessDelivery"],
  outputsTruncated: false,
  legacy: false,
  outputs: [
    { kind: "remote", key: "main:remote", remotePath: "cloud-folder" },
    { kind: "task", taskKind: "download", taskId: "site:97", key: "main:downloadTask" },
    { kind: "warning", key: "main:processing", code: "RESULT_UNCERTAIN", severity: "warning" },
    { kind: "warning", key: "main:problem", code: "RESULT_UNCERTAIN", severity: "warning" },
    { kind: "warning", key: "extras:problem", code: "RESULT_UNCERTAIN", severity: "warning" },
    {
      ...artifact,
      key: "main:continuation",
      labelKey: "acquisition.plan.continueFiles",
      state: "reused",
    },
    { ...artifact, key: "main:processed", labelKey: "cloudDownload.deliveryReview.pendingFiles" },
    {
      ...artifact,
      key: "extras:processed",
      labelKey: "cloudDownload.deliveryReview.pendingFiles",
      directory: "/other",
    },
  ],
};
function records(stage: AcquisitionStage) {
  return (
    <StageExecutionRecords
      taskId={42}
      stage={stage}
      attempts={[sample]}
      initial={{ id: sample.id, io: false }}
      onClose={vi.fn()}
      onOpenRun={vi.fn()}
    />
  );
}
it("presents one wait per affected target and one file list per location, with the preserved roles", () => {
  const view = render(records("processing"));
  const warnings = screen.getByRole("region", { name: "acquisition.records.attention" });
  expect(within(warnings).getAllByText("RESULT_UNCERTAIN")).toHaveLength(2);
  const files = screen.getByRole("region", { name: "acquisition.records.files" });
  expect(
    within(files).getByText(
      "acquisition.records.inputFiles · cloudDownload.deliveryReview.pendingFiles",
    ),
  ).toBeVisible();
  expect(within(files).getByText("cloudDownload.deliveryReview.pendingFiles")).toBeVisible();
  expect(screen.queryByRole("region", { name: "acquisition.records.related" })).toBeNull();
  view.rerender(records("download"));
  const downloadSources = screen.getByRole("region", { name: "acquisition.records.related" });
  expect(within(downloadSources).getByText("main:remote")).toBeVisible();
  expect(within(downloadSources).getByText("main:downloadTask")).toBeVisible();
});
