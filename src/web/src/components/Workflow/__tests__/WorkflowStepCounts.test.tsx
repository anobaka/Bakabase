import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import WorkflowStepCounts from "../WorkflowStepCounts";
import {
  downloadWorksetExecutionLabels,
  fileProcessingExecutionLabels,
} from "../Activities/executionLabels";
import cn from "@/locales/cn/pages/workflow.json";
import en from "@/locales/en/pages/workflow.json";

vi.mock("../Activities", () => ({
  getWorkflowActivityUI: (kind: string) => ({
    executionLabels:
      kind === "download"
        ? downloadWorksetExecutionLabels
        : kind === "files"
          ? fileProcessingExecutionLabels
          : undefined,
  }),
}));
vi.mock("react-i18next", () => ({
  useTranslation: () => ({ t: (key: string) => (cn as Record<string, string>)[key] ?? key }),
}));
vi.mock("@/components/bakaui", () => ({
  Tooltip: ({ children, content }: any) => <span title={content}>{children}</span>,
}));

describe("workflow work item counts", () => {
  it("uses node-owned labels without presenting a workset as a single file", () => {
    render(<WorkflowStepCounts kind="download" inputCount={1} outputCount={1} />);
    expect(screen.getByText("输入任务组: 1")).toBeVisible();
    expect(screen.getByText("输出任务组: 1")).toBeVisible();
    expect(screen.getByTitle(cn["workflow.counts.worksetExplanation"])).toBeVisible();
    expect(document.body.textContent).not.toContain("→");
  });
  it("lets local file processors describe their result and falls back safely for unknown nodes", () => {
    const view = render(
      <WorkflowStepCounts kind="files" inputCount={1} outputCount={0} failedCount={1} />,
    );
    expect(screen.getByText("处理结果项: 0")).toBeVisible();
    expect(screen.getByText("失败项: 1")).toBeVisible();
    view.rerender(<WorkflowStepCounts kind="future" inputCount={2} outputCount={4} />);
    expect(screen.getByText("输入项: 2")).toBeVisible();
    expect(screen.getByText("输出项: 4")).toBeVisible();
    expect(screen.queryByText(/失败项/)).toBeNull();
  });
  it("ships Chinese and English for every count and location label", () => {
    for (const key of Object.keys(cn).filter((key) => /^workflow\.(counts|location)\./.test(key))) {
      expect((en as Record<string, string>)[key], key).toBeTruthy();
    }
  });
});
