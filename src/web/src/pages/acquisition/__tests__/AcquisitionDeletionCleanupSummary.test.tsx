import { render, screen } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import AcquisitionDeletionCleanupSummary from "../components/AcquisitionDeletionCleanupSummary";

vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/components/Workflow/displayNames", () => ({ activityDisplayName: (_t: unknown, kind: string) =>
  kind === "fs.publishArtifacts" ? "交付文件产物" : kind }));
vi.mock("@/components/bakaui", () => ({
  Accordion: ({ children }: any) => <div>{children}</div>,
  AccordionItem: ({ children, title, subtitle }: any) => <section>{title}{subtitle}{children}</section>,
  Button: ({ children, onPress }: any) => <button onClick={onPress}>{children}</button>,
  Chip: ({ children }: any) => <span>{children}</span>,
}));

it("localizes generated node labels in both summary and expanded records while retaining custom names and technical IDs", () => {
  render(<AcquisitionDeletionCleanupSummary reason={code => code ?? ""} cleanup={{
    fileCount: 0, bytes: 0, identifiedFileCount: 0, identifiedBytes: 0, sizeIncomplete: false,
    groups: [{ artifactId: "final", kind: "publishedFiles", directory: "/Acquired/A1",
      eligible: true, shared: false, fileCount: 0, bytes: 0, files: [], filesTruncated: false, sizeUnknown: false,
      nodes: [{ runId: 1, nodeId: "files.deliver", kind: "fs.publishArtifacts" },
        { runId: 1, nodeId: "用户自定义交付名称", kind: "fs.publishArtifacts" }],
    }],
  }} />);
  expect(screen.queryByText(/交付文件产物 · files\.deliver/)).toBeNull();
  expect(screen.getAllByTitle("files.deliver")).toHaveLength(2);
  expect(screen.getAllByText(/交付文件产物 · 用户自定义交付名称/)).toHaveLength(2);
  expect(screen.getByText("交付文件产物")).toBeVisible();
});
