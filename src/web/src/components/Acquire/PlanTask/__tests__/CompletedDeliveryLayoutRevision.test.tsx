import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import Form, { canReviseDeliveryLayout } from "../CompletedDeliveryLayoutRevision";
import type { PlanTaskDetail } from "../models";
import { AcquisitionStatus } from "@/sdk/constants";

const api = vi.hoisted(() => ({ preview: vi.fn(), replan: vi.fn(), run: vi.fn() }));
vi.mock("@/components/FileSystemSelector/components/Button", () => ({ default: ({ fileSystemSelectorProps }: any) => <button onClick={() => fileSystemSelectorProps.onSelected({path: "/work/new"})}>Choose /work/new</button> }));
vi.mock("../api", () => ({ planTaskApi: api }));
vi.mock("@/sdk/BApi", () => ({
  default: { workflow: { previewArtifactDeliveryLayout: api.preview } },
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, isDisabled, isLoading }: any) => (
    <button disabled={isDisabled || isLoading} onClick={onPress}>
      {children}
    </button>
  ),
  Modal: ({title,children,footer}: any) => <div role="dialog" aria-label={title}>{children}{footer}</div>,
  Checkbox: ({ children, isSelected, onValueChange, isDisabled }: any) => (
    <label>
      <input
        type="checkbox"
        checked={isSelected}
        disabled={isDisabled}
        onChange={(e) => onValueChange(e.target.checked)}
      />
      {children}
    </label>
  ),
  Select: ({ label, selectedKeys, dataSource, onSelectionChange }: any) => (
    <select
      aria-label={label}
      value={selectedKeys[0] ?? ""}
      onChange={(e) => onSelectionChange(new Set([e.target.value]))}
    >
      <option value="" />
      {dataSource.map((row: any) => (
        <option key={row.value} value={row.value}>
          {row.label}
        </option>
      ))}
    </select>
  ),
  Textarea: ({ label, value, onValueChange }: any) => (
    <textarea aria-label={label} value={value} onChange={(e) => onValueChange(e.target.value)} />
  ),
  Chip: ({ children }: any) => <span>{children}</span>,
  Spinner: () => <span>Loading</span>,
}));
const artifact = (partId: string, id: string, kind = "publishedFiles") => ({
  partId,
  kind,
  canRepublish: kind === "publishedFiles",
  artifactId: id,
  fingerprint: `${id}-manifest`,
  rootDirectory: `/${id}`,
  fileCount: 1,
});
const detail = {
  task: { id: 42, planRevision: 3, workflowRunId: 100, status: AcquisitionStatus.Completed },
  plan: {
    parts: [
      { id: "first", title: "First" },
      { id: "second", title: "Second" },
    ],
  },
  replanArtifacts: [
    artifact("first", "a"),
    artifact("first", "b"),
    { ...artifact("first", "borrowed-publication"), canRepublish: false },
    { ...artifact("first", "legacy-unclassified"), canRepublish: undefined },
    artifact("first", "raw", "download"),
    artifact("second", "c"),
    artifact("second", "processed", "processedFiles"),
    artifact("other", "unrelated"),
  ],
} as PlanTaskDetail;
const preview = (id: string, policy: any, overrides = {}) => ({
  code: 0,
  data: {
    inputArtifactId: id,
    inputFingerprint: `${id}-manifest`,
    projectionFingerprint: `${id}-projection`,
    policy,
    changed: id !== "c",
    fileCount: 1,
    changedFileCount: id === "c" ? 0 : 1,
    directoryCount: 0,
    fileMappings: [{ sourceRelativePath: `${id}/old`, targetRelativePath: `${id}/next` }],
    directoryMappings: [],
    removedDirectories: [],
    truncated: false,
    state: "available",
    ...overrides,
  },
});
const renderForm = () =>
  render(<Form detail={detail} onCancel={vi.fn()} onSaved={vi.fn()} onBack={vi.fn()} />);
const save = () => screen.getByRole("button", { name: "artifact.deliveryLayout.saveRevision" });
const part = (name: string) => within(screen.getByRole("region", { name }));
const pick = (id = "a") =>
  fireEvent.change(part("First").getByRole("combobox"), { target: { value: id } });
const review = async (name: string, id: string) => {
  fireEvent.click(
    part(name).getByRole("button", { name: "artifact.deliveryLayout.previewAction" }),
  );
  await part(name).findByText(`${id}/next`);
};
beforeEach(() => {
  vi.resetAllMocks();
  api.preview.mockImplementation(async (_run, id, policy) => preview(id, policy));
  api.replan.mockResolvedValue(detail);
});

it("requires a published artifact with a manifest in every part and a recorded source run", () => {
  expect(canReviseDeliveryLayout(detail)).toBe(true);
  expect(
    canReviseDeliveryLayout({
      ...detail,
      replanArtifacts: detail.replanArtifacts?.filter((a) => a.artifactId !== "c"),
    }),
  ).toBe(false);
  expect(
    canReviseDeliveryLayout({ ...detail, task: { ...detail.task, workflowRunId: undefined } }),
  ).toBe(false);
});

it("uses only that part's published files, binds every preview fingerprint and saves once without running", async () => {
  const view = renderForm();
  expect(part("First").getByRole("combobox")).toHaveValue("");
  expect(part("Second").getByRole("combobox")).toHaveValue("c");
  expect(screen.queryByRole("option", { name: "/raw" })).toBeNull();
  expect(screen.queryByRole("option", { name: "/processed" })).toBeNull();
  expect(screen.queryByRole("option", { name: "/unrelated" })).toBeNull();
  expect(screen.queryByRole("option", { name: "/borrowed-publication" })).toBeNull();
  expect(screen.queryByRole("option", { name: "/legacy-unclassified" })).toBeNull();
  expect(api.preview).not.toHaveBeenCalled();
  expect(save()).toBeDisabled();
  pick();
  await review("First", "a");
  expect(save()).toBeDisabled();
  await review("Second", "c");
  expect(save()).toBeEnabled();
  view.rerender(
    <Form
      detail={{ ...detail, task: { ...detail.task, planRevision: 9, workflowRunId: 999 } }}
      onBack={vi.fn()}
      onSaved={vi.fn()}
      onCancel={vi.fn()}
    />,
  );
  fireEvent.click(save());
  await waitFor(() =>
    expect(api.replan).toHaveBeenCalledExactlyOnceWith(42, {
      expectedRevision: 3,
      expectedRunId: 100,
      reason: "artifact.deliveryLayout.revisionReason",
      useCurrentStorageDefaults: false,
      deliveryContentLayout: {
        version: 1,
        removeGeneratedDirectories: true,
        collapseSingleRootDirectory: false,
      },
      items: [
        ["first", "a"],
        ["second", "c"],
      ].map(([partId, id]) => ({
        partId,
        plan: { requirement: "notRequired", steps: [], evidence: [] },
        continueFromArtifactId: id,
        artifactEvidenceIds: [id],
        expectedDeliveryProjectionFingerprint: `${id}-projection`,
      })),
    }),
  );
  expect(api.run).not.toHaveBeenCalled();
});

it("uses safe defaults for a new revision of a custom legacy plan and invalidates a changed selection", async () => {
  render(<Form detail={{ ...detail, deliverySettings: {
    rootDirectory: "", directoryTemplate: "{Title} [A{TaskId}]", partDirectoryTemplate: "{PartTitle} [{PartId}]",
    contentLayout: { version: 1, removeGeneratedDirectories: false, collapseSingleRootDirectory: true },
  } }} onSaved={vi.fn()} onCancel={vi.fn()} />);
  expect(screen.queryByRole("checkbox", { name: "artifact.deliveryLayout.removeGenerated" })).not.toBeInTheDocument();
  expect(screen.queryByRole("checkbox", { name: "artifact.deliveryLayout.collapseSingleRoot" })).not.toBeInTheDocument();
  pick();
  await review("First", "a");
  await review("Second", "c");
  expect(save()).toBeEnabled();
  pick("b");
  expect(save()).toBeDisabled();
  await review("First", "b");
  expect(save()).toBeEnabled();
  expect(api.preview).toHaveBeenLastCalledWith(100, "b", {
    version: 1, removeGeneratedDirectories: true, collapseSingleRootDirectory: false,
  }, { showErrorToast: false });
  expect(api.replan).not.toHaveBeenCalled();
});

it.each([{ changed: false }, { state: "retired" }])(
  "does not revise for unchanged mappings or a non-available artifact: %o",
  async (overrides) => {
    api.preview.mockImplementation(async (_run, id, policy) => preview(id, policy, overrides));
    renderForm();
    pick();
    await review("First", "a");
    await review("Second", "c");
    expect(save()).toBeDisabled();
    expect(api.replan).not.toHaveBeenCalled();
  },
);

it.each([{ inputFingerprint: "other-manifest" }, { projectionFingerprint: "" }])(
  "rejects missing/stale evidence: %o",
  async (overrides) => {
    api.preview.mockImplementation(async (_run, id, policy) => preview(id, policy, overrides));
    renderForm();
    pick();
    fireEvent.click(
      part("First").getByRole("button", { name: "artifact.deliveryLayout.previewAction" }),
    );
    expect(await part("First").findByRole("alert")).toBeVisible();
    expect(save()).toBeDisabled();
    expect(api.replan).not.toHaveBeenCalled();
  },
);

it("does not retry a rejected replan automatically or run after a conflict", async () => {
  api.replan.mockRejectedValue(new Error("PLAN_VERSION_CONFLICT"));
  renderForm();
  pick();
  await review("First", "a");
  await review("Second", "c");
  fireEvent.click(save());
  expect(await screen.findByRole("alert")).toHaveTextContent("PLAN_VERSION_CONFLICT");
  expect(save()).toBeDisabled();
  expect(api.replan).toHaveBeenCalledTimes(1);
  expect(api.run).not.toHaveBeenCalled();
});

it("can deliver the reviewed output again with an unchanged layout to a task-specific directory", async () => {
  const accept = vi.fn();
  api.preview.mockImplementation(async (_run, id, policy) => preview(id, policy, {changed:false}));
  render(<Form detail={detail} retry onCancel={vi.fn()} onSaved={accept} />);
  pick();
  await review("First","a");
  await review("Second","c");
  openPlacement();
  fireEvent.click(screen.getByRole("checkbox",{name:"acquisition.timeline.overrideDeliveryRoot"}));
  const submit = screen.getByRole("button",{name:"acquisition.timeline.startAttempt"});
  expect(screen.getByRole("button",{name:"common.action.apply"})).toBeDisabled();
  fireEvent.click(screen.getByRole("button",{name:"Choose /work/new"}));
  applyPlacement();
  fireEvent.click(submit);
  await waitFor(()=>expect(accept).toHaveBeenCalledOnce());
  expect(api.replan).toHaveBeenCalledWith(42,expect.objectContaining({expectedRunId:100, expectedRevision:3, retryDeliveryOnly:true, deliveryRootDirectory:"/work/new"}));
});

function openPlacement() {
  fireEvent.click(screen.getByRole("button", {name: "acquisition.placement.open"}));
}
function applyPlacement() {
  fireEvent.click(screen.getByRole("button", {name: "common.action.apply"}));
}
