import { Children, useState, type ReactNode } from "react";
import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import WorkflowNodeMetadata from "../WorkflowNodeMetadata";
import cn from "@/locales/cn/pages/cloudDownload.json";

const api = vi.hoisted(() => ({ files: vi.fn() }));
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string) =>
      key.startsWith("cloudDownload.problemCode.")
        ? ((cn as Record<string, string>)[key] ?? key)
        : key,
  }),
}));
vi.mock("@/sdk/BApi", () => ({ default: { workflow: { getWorkflowArtifactFiles: api.files } } }));
vi.mock("../displayNames", () => ({ activityDisplayName: (_t: unknown, kind: string) => kind }));
vi.mock("../WorkflowStepCounts", () => ({ default: () => <span>Work item counts</span> }));
vi.mock("../WorkflowFileLocation", () => ({
  default: ({ path, available }: any) => (
    <div>
      {path}
      {available && <button>Browse {path}</button>}
    </div>
  ),
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, as, href }: any) =>
    as === "a" ? <a href={href}>{children}</a> : <button onClick={onPress}>{children}</button>,
  Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
  Spinner: () => <span>Loading</span>,
  Pagination: ({ page, onChange }: any) => (
    <button onClick={() => onChange(page + 1)}>Next page {page}</button>
  ),
  Accordion: ({ children, selectedKeys, onSelectionChange }: any) => {
    const [open, setOpen] = useState<string[]>([]);
    const current = selectedKeys == null ? open : [...selectedKeys].map(String);
    return (
      <div>
        {Children.map(children, (child: any) => (
          <div>
            <button
              aria-expanded={current.includes(child.key)}
              onClick={() => {
                const next = current.includes(child.key)
                  ? current.filter((key: string) => key !== child.key)
                  : [...current, child.key];
                setOpen(next);
                onSelectionChange?.(new Set(next));
              }}
            >
              {child.props.title}
            </button>
            {current.includes(child.key) && child.props.children}
          </div>
        ))}
      </div>
    );
  },
  AccordionItem: ({ children }: any) => <div>{children}</div>,
}));
const base = {
  id: 1,
  nodeId: "node-1",
  stepIndex: 0,
  kind: "fs.processArtifacts",
  attempt: 1,
  status: "waiting",
  startedAt: "2026-10-21T08:00:00Z",
  completedAt: "2026-10-21T08:02:03Z",
  inputCount: 1,
  outputCount: 0,
  failedCount: 0,
  resumeKind: "initial",
  outputsTruncated: false,
  inputs: [],
  inputsRecorded: false,
  inputsTruncated: false,
  outputs: [],
};
const artifact = {
  kind: "artifact",
  key: "files",
  artifactId: "artifact-1",
  operationId: "operation-1",
  directory: "/safe/result",
  fileCount: 101,
  totalBytes: 1024,
  state: "committed",
};
const page = (number = 1) => ({
  code: 0,
  data: {
    artifactId: "artifact-1",
    directory: "/current/result",
    state: "available",
    fileCount: 101,
    totalBytes: 1024,
    pageIndex: number,
    pageSize: 100,
    entries: [{ relativePath: `page-${number}.txt`, length: 10, sha256: "safe-sha256" }],
  },
});
function expandAttempt() {
  fireEvent.click(screen.getByRole("button", { name: /fs.processArtifacts/ }));
  fireEvent.click(screen.getByRole("button", { name: /workflow.metadata.attempt(?:\s|$)/ }));
}
beforeEach(() => {
  api.files.mockReset().mockResolvedValue(page());
});

it("keeps separate same-kind planned nodes and unexecuted or legacy-summary steps in one ordered list", () => {
  render(<WorkflowNodeMetadata runId={42}
    planNodes={[
      { nodeId: "second", kind: "fs.processArtifacts", order: 1, notes: "Second part" },
      { nodeId: "first", kind: "fs.processArtifacts", order: 0, notes: "First part" },
      { nodeId: "future", kind: "fs.publishArtifacts", order: 2 },
    ]}
    executions={[{ ...base, nodeId: "first" }]}
    stepStats={[
      { stepIndex: 0, kind: "fs.processArtifacts", inputCount: 1, outputCount: 0, failedCount: 0 },
      { stepIndex: 3, kind: "legacy.step", inputCount: 4, outputCount: 4, failedCount: 0 },
    ]} />);
  const headers = screen.getAllByRole("button", { name: /fs.processArtifacts|fs.publishArtifacts|legacy.step/ });
  expect(headers).toHaveLength(4);
  expect(headers[0]).toHaveTextContent("1. fs.processArtifacts");
  expect(headers[1]).toHaveTextContent("2. fs.processArtifacts");
  expect(headers[2]).toHaveTextContent("3. fs.publishArtifacts");
  expect(headers[3]).toHaveTextContent("4. legacy.step");
  fireEvent.click(headers[2]);
  expect(screen.getByText("workflow.metadata.noExecutionHint")).toBeVisible();
  expect(api.files).not.toHaveBeenCalled();
});

it("localizes a stable cancelled-share warning without rendering its raw diagnostic payload", () => {
  render(
    <WorkflowNodeMetadata
      runId={42}
      executions={[
        {
          ...base,
          outputs: [
            {
              kind: "warning",
              key: "share-error",
              code: "BAIDU_SHARE_CANCELLED",
              message: "RAW_DIAGNOSTIC_SECRET",
              severity: "error",
            },
          ],
        },
      ]}
    />,
  );
  expandAttempt();
  expect(screen.getByText(cn["cloudDownload.problemCode.BAIDU_SHARE_CANCELLED"])).toBeVisible();
  expect(screen.queryByText(/提取码|RAW_DIAGNOSTIC_SECRET/)).toBeNull();
});

it("shows each frozen node attempt and recovery source, rendering only known safe block fields", async () => {
  const first = { ...base, outputs: [artifact] };
  const second = {
    ...base,
    id: 2,
    attempt: 2,
    status: "succeeded",
    resumeKind: "signal",
    outputs: [
      {
        kind: "remote",
        key: "remote",
        providerId: "baidu",
        methodId: "baidu.pcs",
        remotePath: "/selected/path",
        url: "https://safe.example/share",
      },
      { kind: "task", key: "task", taskKind: "download", taskId: "site:123" },
      { kind: "scalar", key: "scalar", value: "Approved summary", hiddenPassword: "HIDDEN_SECRET" },
      {
        kind: "warning",
        key: "warn",
        code: "SAFE_WARNING",
        message: "Safe notice",
        severity: "warning",
      },
      {
        kind: "warning",
        key: "unknown-warning",
        code: "UNKNOWN_WARNING",
        message: "RAW_WARNING_SECRET",
      },
      {
        kind: "future",
        key: "future",
        value: "UNKNOWN_RAW_SECRET",
        payload: { password: "RAW_SECRET" },
      },
    ],
  };
  render(
    <WorkflowNodeMetadata
      runId={42}
      executions={[first, second]}
      legacyOutput='[{"password":"LEGACY_SECRET"}]'
    />,
  );
  fireEvent.click(screen.getByRole("button", { name: /fs.processArtifacts/ }));
  expect(screen.getByText("workflow.metadata.resume.initial")).toBeVisible();
  expect(screen.getByText("workflow.metadata.resume.signal")).toBeVisible();
  for (const button of screen.getAllByRole("button", { name: /workflow.metadata.attempt(?:\s|$)/ }))
    fireEvent.click(button);
  expect(screen.getByText(/\/safe\/result/)).toBeVisible();
  expect(screen.getByText("Approved summary")).toBeVisible();
  expect(screen.getByText("/selected/path")).toBeVisible();
  expect(screen.getByRole("link", { name: "cloudDownload.viewTask" })).toHaveAttribute(
    "href",
    "#/downloader?id=site%3A123",
  );
  expect(screen.getByText("workflow.metadata.unknownHint")).toBeVisible();
  expect(document.body.textContent).not.toContain("SECRET");
  expect(api.files).toHaveBeenCalledTimes(1);
  expect(await screen.findByText("/current/result")).toBeVisible();
});

it("resolves the current artifact directory when expanded and paginates its run-bound manifest on demand", async () => {
  api.files.mockResolvedValueOnce(page()).mockResolvedValueOnce(page(2));
  render(<WorkflowNodeMetadata runId={42} executions={[{ ...base, outputs: [artifact] }]} />);
  expect(api.files).not.toHaveBeenCalled();
  expandAttempt();
  expect(await screen.findByRole("button", { name: "Browse /current/result" })).toBeVisible();
  expect(screen.queryByRole("button", { name: "Browse /safe/result" })).toBeNull();
  expect(screen.queryByText("page-1.txt")).toBeNull();
  fireEvent.click(screen.getByText("workflow.metadata.showFiles"));
  expect(await screen.findByText("page-1.txt")).toBeVisible();
  expect(api.files).toHaveBeenLastCalledWith(
    42,
    "artifact-1",
    { pageIndex: 1, pageSize: 100 },
    { showErrorToast: false },
  );
  fireEvent.click(screen.getByText("Next page 1"));
  expect(await screen.findByText("page-2.txt")).toBeVisible();
  expect(screen.queryByText("page-1.txt")).toBeNull();
  expect(api.files).toHaveBeenLastCalledWith(
    42,
    "artifact-1",
    { pageIndex: 2, pageSize: 100 },
    { showErrorToast: false },
  );
});

it("rejects an old run's late manifest and exposes a retryable unavailable state without scanning paths", async () => {
  let finish!: (response: unknown) => void;
  api.files
    .mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    )
    .mockResolvedValueOnce({ code: 404 })
    .mockResolvedValueOnce(page());
  const executions = [{ ...base, outputs: [artifact] }];
  const view = render(<WorkflowNodeMetadata runId={42} executions={executions} />);
  expandAttempt();
  fireEvent.click(screen.getByText("workflow.metadata.showFiles"));
  await waitFor(() => expect(api.files).toHaveBeenCalledTimes(1));
  view.rerender(<WorkflowNodeMetadata runId={43} executions={executions} />);
  fireEvent.click(screen.getByText("workflow.metadata.showFiles"));
  expect(await screen.findByRole("alert")).toHaveTextContent("workflow.metadata.filesUnavailable");
  await act(async () =>
    finish({
      ...page(),
      data: {
        ...page().data,
        entries: [{ relativePath: "STALE_OLD_FILE", length: 1, sha256: "x" }],
      },
    }),
  );
  expect(screen.queryByText("STALE_OLD_FILE")).toBeNull();
  fireEvent.click(screen.getByText("common.action.retry"));
  expect(await screen.findByText("page-1.txt")).toBeVisible();
  expect(api.files).toHaveBeenLastCalledWith(
    43,
    "artifact-1",
    { pageIndex: 1, pageSize: 100 },
    { showErrorToast: false },
  );
});

it("keeps recognized legacy artifact summaries but does not render arbitrary legacy JSON", () => {
  render(
    <WorkflowNodeMetadata
      runId={1}
      legacyOutput={JSON.stringify([
        {
          targets: [
            {
              id: "legacy",
              title: "Delivered result",
              publishedDirectory: "/library/result",
              publishedFileCount: 1,
            },
          ],
          cookies: "LEGACY_COOKIE_SECRET",
        },
        { raw: "ARBITRARY_SECRET" },
      ])}
    />,
  );
  expect(screen.getByText("Delivered result")).toBeVisible();
  expect(screen.getByText(/\/library\/result/)).toBeVisible();
  expect(document.body.textContent).not.toContain("SECRET");
  expect(screen.queryByRole("button", { name: "workflow.metadata.showFiles" })).toBeNull();
});

it("expands every node, attempt and file manifest, then collapses nested levels as well", async () => {
  const executions = [
    { ...base, outputs: [artifact] },
    {
      ...base,
      id: 2,
      attempt: 2,
      outputs: [{ kind: "scalar", key: "second", value: "Second attempt" }],
    },
    {
      ...base,
      id: 3,
      nodeId: "node-2",
      stepIndex: 1,
      kind: "fs.publishArtifacts",
      outputs: [{ kind: "scalar", key: "published", value: "Published output" }],
    },
  ];
  render(<WorkflowNodeMetadata runId={42} executions={executions} />);
  expect(api.files).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "workflow.details.expandAll" }));
  expect(await screen.findByText("page-1.txt")).toBeVisible();
  expect(screen.getByText("Second attempt")).toBeVisible();
  expect(screen.getByText("Published output")).toBeVisible();
  expect(api.files).toHaveBeenCalledTimes(1);
  fireEvent.click(screen.getByRole("button", { name: "workflow.details.collapseAll" }));
  expect(screen.queryByText("Second attempt")).toBeNull();
  expect(screen.queryByText("page-1.txt")).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: /fs.processArtifacts/ }));
  expect(screen.queryByText("Second attempt")).toBeNull();
  fireEvent.click(screen.getAllByRole("button", { name: /workflow.metadata.attempt(?:\s|$)/ })[0]);
  expect(screen.getByRole("button", { name: "workflow.metadata.showFiles" })).toBeVisible();
  expect(api.files).toHaveBeenCalledTimes(2);
  expect(await screen.findByText("/current/result")).toBeVisible();
});

it("does not offer folder actions for a missing artifact or a legacy snapshot-only path", async () => {
  api.files.mockResolvedValue({ ...page(), data: { ...page().data, state: "missing" } });
  render(<WorkflowNodeMetadata runId={42} executions={[{ ...base, outputs: [artifact] }]} />);
  expandAttempt();
  expect(await screen.findByText("/current/result")).toBeVisible();
  expect(screen.queryByRole("button", { name: /^Browse/ })).toBeNull();
});

it("shows a directory-resolution failure before opening the file list and never reuses an old open action", async () => {
  api.files.mockResolvedValueOnce({ code: 404 }).mockResolvedValueOnce(page());
  render(<WorkflowNodeMetadata runId={42} executions={[{ ...base, outputs: [artifact] }]} />);
  expandAttempt();
  expect(await screen.findByRole("alert")).toHaveTextContent("workflow.metadata.filesUnavailable");
  expect(screen.queryByRole("button", { name: /^Browse/ })).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "common.action.retry" }));
  expect(await screen.findByRole("button", { name: "Browse /current/result" })).toBeVisible();
  expect(screen.queryByText("page-1.txt")).toBeNull();
});

it("preserves manual choices on polling and honors repeated parent expansion requests", () => {
  const executions = [
    { ...base, outputs: [{ kind: "scalar", key: "value", value: "Output details" }] },
  ];
  const expanded = { expanded: true };
  const view = render(
    <WorkflowNodeMetadata runId={42} executions={executions} expansion={expanded} />,
  );
  expect(screen.getByText("Output details")).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: /workflow.metadata.attempt(?:\s|$)/ }));
  view.rerender(
    <WorkflowNodeMetadata
      runId={42}
      executions={structuredClone(executions)}
      expansion={expanded}
    />,
  );
  expect(screen.queryByText("Output details")).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "workflow.details.expandAll" }));
  expect(screen.getByText("Output details")).toBeVisible();
  view.rerender(
    <WorkflowNodeMetadata runId={42} executions={executions} expansion={{ expanded: false }} />,
  );
  expect(screen.queryByText("Output details")).toBeNull();
  view.rerender(
    <WorkflowNodeMetadata runId={42} executions={executions} expansion={{ expanded: true }} />,
  );
  expect(screen.getByText("Output details")).toBeVisible();
});

it("includes legacy artifact, original file and processed file details in expansion controls", () => {
  render(
    <WorkflowNodeMetadata
      runId={42}
      legacyOutput={JSON.stringify([
        {
          targets: [
            {
              id: "legacy",
              downloadArtifactId: "saved-artifact",
              files: ["/downloads/archive.rar"],
              processedFiles: ["/downloads/final/file.dat"],
            },
          ],
        },
      ])}
    />,
  );
  expect(screen.queryByText("saved-artifact")).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "workflow.details.expandAll" }));
  expect(screen.getByText("saved-artifact")).toBeVisible();
  expect(screen.getByText("/downloads/archive.rar")).toBeVisible();
  expect(screen.getByText("/downloads/final/file.dat")).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: "workflow.details.collapseAll" }));
  expect(screen.queryByText("saved-artifact")).toBeNull();
  expect(screen.queryByText("/downloads/archive.rar")).toBeNull();
  expect(screen.queryByText("/downloads/final/file.dat")).toBeNull();
});
