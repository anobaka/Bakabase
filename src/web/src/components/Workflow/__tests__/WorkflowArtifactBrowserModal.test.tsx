import { Children, useState } from "react";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";

import WorkflowArtifactBrowserModal, {
  groupFileExecutions,
  type FileExecution,
} from "../WorkflowArtifactBrowserModal";

const mocks = vi.hoisted(() => ({ files: vi.fn(), explorer: vi.fn(), taskDirectory: vi.fn() }));
vi.mock("../TaskDirectoryExplorer", () => ({ default: (props: any) => {
  mocks.taskDirectory(props); return <div>Current task directory</div>;
} }));

vi.mock("@/components/FileArtifacts/DeliveryLayoutPreview", () => ({
  default: ({ runId, artifactId }: any) => (
    <div data-testid="delivery-preview" data-run-id={runId}>
      {artifactId}
    </div>
  ),
}));

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, args?: Record<string, unknown>) =>
      args?.number ? `${key} ${args.number}` : key,
  }),
}));
vi.mock("@/sdk/BApi", () => ({ default: { workflow: { getWorkflowArtifactFiles: mocks.files } } }));
vi.mock("../displayNames", () => ({ activityDisplayName: (_t: unknown, kind: string) => kind }));
vi.mock("@/components/FilePathValue", () => ({ default: ({ path }: any) => <span>{path}</span> }));
vi.mock("@/components/FileExplorer/FileExplorer", () => ({
  default: (props: any) => {
    mocks.explorer(props);

    return <div>Browsing {props.rootPath}</div>;
  },
}));
vi.mock("@/components/FileSystemSelector/components/Panel", () => ({
  default: (props: any) => (
    <div data-testid="managed-directory" data-mode={props.mode} data-path={props.startPath} />
  ),
}));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ visible, title, children, footer }: any) =>
    visible ? (
      <div aria-label={title} role="dialog">
        {children}
        {footer}
      </div>
    ) : null,
  Button: ({ children, onPress, "aria-pressed": pressed }: any) => (
    <button aria-pressed={pressed} onClick={onPress}>
      {children}
    </button>
  ),
  Chip: ({ children }: any) => <span>{children}</span>,
  Spinner: () => <span>Loading</span>,
  Pagination: ({ page, onChange }: any) => (
    <button onClick={() => onChange(page + 1)}>Next page</button>
  ),
  Select: ({ "aria-label": label, selectedKeys, dataSource, onSelectionChange }: any) => (
    <select
      aria-label={label}
      value={[...selectedKeys][0]}
      onChange={(event) => onSelectionChange(new Set([event.target.value]))}
    >
      {dataSource.map((item: any) => (
        <option key={item.value} value={item.value}>
          {item.label}
        </option>
      ))}
    </select>
  ),
  Tabs: ({ children, "aria-label": label, selectedKey, onSelectionChange }: any) => (
    <div aria-label={label} role="tablist">
      {Children.map(children, (child: any) => (
        <button
          aria-selected={child.key === selectedKey}
          disabled={child.props.isDisabled}
          role="tab"
          onClick={() => onSelectionChange(child.key)}
        >
          {child.props.title}
        </button>
      ))}
    </div>
  ),
  Tab: () => null,
  Accordion: ({ children }: any) => <div>{children}</div>,
  AccordionItem: ({ title, children }: any) => {
    const [open, setOpen] = useState(false);

    return (
      <div>
        <button onClick={() => setOpen(!open)}>{title}</button>
        {open && children}
      </div>
    );
  },
}));
const artifact = (id: string) => ({
  kind: "artifact",
  key: id,
  artifactId: id,
  label: id,
  directory: `/old/${id}`,
  fileCount: 1,
});
const node = (id: number, inputId?: string): FileExecution => ({
  id,
  nodeId: `node-${id}`,
  stepIndex: id - 1,
  kind: `test.step${id}`,
  attempt: 1,
  status: "succeeded",
  startedAt: "2026-10-30T01:00:00Z",
  inputCount: 1,
  outputCount: 1,
  failedCount: 0,
  resumeKind: "initial",
  outputsTruncated: false,
  inputsRecorded: inputId != null,
  inputs: inputId ? [artifact(inputId)] : undefined,
  outputs: [artifact(`out-${id}`)],
});

beforeEach(() => {
  vi.clearAllMocks();
  mocks.files.mockImplementation(async (_run: number, id: string, { pageIndex }: any) => ({
    code: 0,
    data: {
      artifactId: id,
      state: "available",
      directory: `/current/${id}`,
      fileCount: 1,
      totalBytes: 10,
      pageIndex,
      pageSize: 100,
      entries: [{ relativePath: `${id}.zip`, length: 10 }],
    },
  }));
});

it("keeps a retired artifact's manifest visible without offering directory browsing", async () => {
  mocks.files.mockResolvedValue({
    code: 0,
    data: {
      artifactId: "out-1",
      state: "retired",
      directory: "/historical/removed",
      fileCount: 1,
      totalBytes: 10,
      pageIndex: 1,
      pageSize: 100,
      entries: [{ relativePath: "old-archive.zip", length: 10 }],
    },
  });
  render(<WorkflowArtifactBrowserModal executions={[node(1)]} runId={42} onClose={() => {}} />);
  expect(await screen.findByText("old-archive.zip")).toBeVisible();
  expect(screen.getByText("workflow.metadata.artifactState.retired")).toBeVisible();
  expect(screen.getByText("workflow.metadata.artifactRetiredNotice")).toBeVisible();
  expect(screen.getByRole("tab", { name: "workflow.files.directory" })).toBeDisabled();
  expect(mocks.explorer).not.toHaveBeenCalled();
});

it("uses the selected attempt's recorded input, never the preceding output", async () => {
  render(
    <WorkflowArtifactBrowserModal
      executions={[node(1), node(2, "consumed")]}
      initialDirection="input"
      initialExecutionId={2}
      runId={42}
      onClose={() => {}}
    />,
  );
  expect(await screen.findByText("consumed.zip")).toBeVisible();
  expect(mocks.files).toHaveBeenCalledExactlyOnceWith(
    42,
    "consumed",
    { pageIndex: 1, pageSize: 100 },
    { showErrorToast: false },
  );
  expect(screen.queryByText("out-1.zip")).toBeNull();
  expect(mocks.explorer).not.toHaveBeenCalled();
});

it("opens actual delivery preview inside the same file modal using the selected artifact and run", async () => {
  render(
    <WorkflowArtifactBrowserModal
      executions={[node(2, "consumed")]}
      runId={42}
      initialDirection="input"
      onClose={() => {}}
    />,
  );
  expect(await screen.findByText("consumed.zip")).toBeVisible();
  fireEvent.click(screen.getByRole("tab", { name: "artifact.deliveryLayout.preview" }));
  expect(screen.getByTestId("delivery-preview")).toHaveTextContent("consumed");
  expect(screen.getByTestId("delivery-preview")).toHaveAttribute("data-run-id", "42");
  expect(screen.getAllByRole("dialog")).toHaveLength(1);
  expect(mocks.explorer).not.toHaveBeenCalled();
});

it("distinguishes old unrecorded inputs from an explicitly recorded empty input", () => {
  const view = render(
    <WorkflowArtifactBrowserModal
      executions={[node(1)]}
      initialDirection="input"
      runId={42}
      onClose={() => {}}
    />,
  );

  expect(screen.getByText("workflow.files.inputsNotRecorded")).toBeVisible();
  expect(mocks.files).not.toHaveBeenCalled();
  view.rerender(
    <WorkflowArtifactBrowserModal
      executions={[{ ...node(1), inputsRecorded: true, inputs: [] }]}
      initialDirection="input"
      runId={42}
      onClose={() => {}}
    />,
  );
  expect(screen.getByText("workflow.files.noInputFiles")).toBeVisible();
  expect(screen.queryByText("workflow.files.inputsNotRecorded")).toBeNull();
});

it("defaults to the exact manifest and only browses the currently resolved available directory read-only", async () => {
  render(<WorkflowArtifactBrowserModal executions={[node(1)]} runId={42} onClose={() => {}} />);
  expect(await screen.findByText("out-1.zip")).toBeVisible();
  expect(screen.queryByText("/old/out-1")).toBeNull();
  expect(mocks.explorer).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("tab", { name: "workflow.files.directory" }));
  expect(await screen.findByText("Browsing /current/out-1")).toBeVisible();
  expect(mocks.explorer).toHaveBeenLastCalledWith(
    expect.objectContaining({
      rootPath: "/current/out-1",
      keyboard: false,
      watchChanges: false,
      selectable: "disabled",
      capabilities: ["enter-directory"],
    }),
  );
});

it("uses the shared filesystem manager when the caller allows task file operations", async () => {
  render(
    <WorkflowArtifactBrowserModal
      allowFileOperations
      location={{ path: "/task/686" }}
      onClose={() => {}}
    />,
  );

  const manager = await screen.findByTestId("managed-directory");

  expect(manager).toHaveAttribute("data-mode", "manage");
  expect(manager).toHaveAttribute("data-path", "/task/686");
});

it("checks the current task and run before offering phase-directory operations", async () => {
  render(<WorkflowArtifactBrowserModal location={{ path: "/work/phase" }} taskDirectory={{ taskId: 865, runId: 22 }} onClose={() => {}} />);
  await screen.findByText("Current task directory");
  expect(mocks.taskDirectory).toHaveBeenCalledWith({ path: "/work/phase", context: { taskId: 865, runId: 22 } });
  expect(mocks.files).not.toHaveBeenCalled();
});

it("does not enable editing a recorded artifact merely because task context was supplied", async () => {
  render(<WorkflowArtifactBrowserModal executions={[node(1)]} runId={42} taskDirectory={{ taskId: 865, runId: 22 }} onClose={() => {}} />);
  await screen.findByText("out-1.zip");
  fireEvent.click(screen.getByRole("tab", { name: "workflow.files.directory" }));
  await screen.findByText("Browsing /current/out-1");
  expect(mocks.taskDirectory).not.toHaveBeenCalled();
  expect(mocks.explorer.mock.lastCall?.[0]).toMatchObject({ keyboard: false, capabilities: ["enter-directory"] });
});

it("keeps the manifest inspectable after cleanup without permitting directory browsing", async () => {
  mocks.files.mockResolvedValue({
    code: 0,
    data: {
      artifactId: "out-1",
      state: "retired",
      directory: "/retired",
      fileCount: 1,
      totalBytes: 10,
      pageIndex: 1,
      pageSize: 100,
      entries: [{ relativePath: "kept-history.zip", length: 10 }],
    },
  });
  render(<WorkflowArtifactBrowserModal executions={[node(1)]} runId={42} onClose={() => {}} />);
  expect(await screen.findByText("kept-history.zip")).toBeVisible();
  expect(screen.getByRole("tab", { name: "workflow.files.directory" })).toBeDisabled();
  expect(mocks.explorer).not.toHaveBeenCalled();
});

it("ignores a stale artifact response when selecting another dynamic step", async () => {
  let resolve!: (value: any) => void;

  mocks.files.mockImplementationOnce(
    () =>
      new Promise((done) => {
        resolve = done;
      }),
  );
  render(
    <WorkflowArtifactBrowserModal
      executions={[node(1), node(2)]}
      initialExecutionId={1}
      runId={42}
      onClose={() => {}}
    />,
  );
  fireEvent.click(screen.getByRole("button", { name: "2. test.step2" }));
  expect(await screen.findByText("out-2.zip")).toBeVisible();
  await act(async () =>
    resolve({
      code: 0,
      data: {
        artifactId: "out-1",
        pageIndex: 1,
        state: "available",
        directory: "/stale",
        fileCount: 1,
        totalBytes: 10,
        entries: [{ relativePath: "stale.zip", length: 10 }],
      },
    }),
  );
  expect(screen.queryByText("stale.zip")).toBeNull();
  expect(screen.getByText("out-2.zip")).toBeVisible();
});

it("groups by explicit node identity and keeps all attempts, including dynamic children", () => {
  const first = { ...node(1), nodeId: "parent/child-a", stepIndex: 2, attempt: 2 };
  const second = { ...node(2), nodeId: "parent/child-a", stepIndex: 2, attempt: 1 };
  const other = { ...node(3), nodeId: "parent/child-b", stepIndex: 2 };
  const grouped = groupFileExecutions([first, second, other]);

  expect(grouped).toHaveLength(2);
  expect(grouped[0].attempts.map((attempt) => attempt.id)).toEqual([2, 1]);
  expect(grouped[1].attempts.map((attempt) => attempt.id)).toEqual([3]);
});

it("keeps the displayed step when polling adds another dynamic execution", async () => {
  const view = render(
    <WorkflowArtifactBrowserModal executions={[node(1)]} runId={42} onClose={() => {}} />,
  );

  expect(await screen.findByText("out-1.zip")).toBeVisible();
  view.rerender(
    <WorkflowArtifactBrowserModal executions={[node(1), node(2)]} runId={42} onClose={() => {}} />,
  );
  expect(screen.getByRole("button", { name: "1. test.step1" })).toHaveAttribute(
    "aria-pressed",
    "true",
  );
  expect(screen.getByText("out-1.zip")).toBeVisible();
  expect(mocks.files).toHaveBeenCalledTimes(1);
});

it("opens a direct directory in the same large viewer without fetching a fabricated artifact", async () => {
  const onClose = vi.fn();

  render(
    <WorkflowArtifactBrowserModal
      location={{ path: "/nas/direct", label: "Delivered files" }}
      onClose={onClose}
    />,
  );
  expect(await screen.findByText("Browsing /nas/direct")).toBeVisible();
  expect(mocks.files).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "common.action.close" }));
  expect(onClose).toHaveBeenCalledOnce();
});
