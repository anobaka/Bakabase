import { fireEvent, render, screen } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import type { BakabaseModulesWorkflowAbstractionsModelsViewWorkflowRunViewModel as Run } from "@/sdk/Api";
import { WorkflowRunStatus } from "@/sdk/constants";
import CurrentProcessingFiles, { currentProcessingArtifact, ProcessingDirectoryFiles } from "../CurrentProcessingFiles";

const files = vi.hoisted(() => vi.fn());
const directoryProps = vi.hoisted(() => vi.fn());
const taskDirectoryProps = vi.hoisted(() => vi.fn());
vi.mock("../TaskDirectoryExplorer", () => ({ default: (props: any) => {
  taskDirectoryProps(props); return <div>Task current files</div>;
} }));
vi.mock("@/sdk/BApi", () => ({ default: { workflow: { getWorkflowArtifactDirectory: files } } }));
// The shared tree's own tests cover rendering and expansion. Exercise this adapter's
// exact run/artifact/page requests through the public data-source contract.
vi.mock("@/components/FileExplorer/FileExplorer", () => ({
  default: ({ recordedDirectory: page, ...props }: { recordedDirectory?: import("@/core/models/FileExplorer/DirectoryPage").ExplorerDirectoryPage }) => {
    if (!page) { directoryProps(props); return <div>Current directory</div>; }
    return (
    <div>
      <button onClick={() => page.onNavigate("")}>workflow.processing.inputFiles.root</button>
      {page.entries.map(entry => entry.isDirectory ? (
        <button key={entry.relativePath} onClick={() => page.onNavigate(entry.relativePath)}>{entry.relativePath}</button>
      ) : <span key={entry.relativePath}>{entry.relativePath.split("/").at(-1)}</span>)}
    </div>
  ); },
}));
vi.mock("@/components/FileSystemEntryIcon", () => ({default: () => null}));
vi.mock("../WorkflowArtifactBrowserModal", () => ({
  WorkflowArtifactBrowserButton: () => <button>browse recorded files</button>,
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, "aria-label": label }: any) => <button aria-label={label} onClick={onPress}>{children}</button>,
  Spinner: () => <span>loading</span>,
  Pagination: ({ onChange }: any) => <button onClick={() => onChange(2)}>next page</button>,
}));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));

const artifact = (key: string, artifactId: string) => ({ kind: "artifact", key, artifactId });
function waiting(prompt: object = {}, patch: object = {}, executionPatch: object = {}): Run {
  const step = {
    id: 19, nodeId: "step-four", stepIndex: 4, kind: "acquisition.plan.process", inputCount: 1, status: "waiting",
    waitReason: "cloud.fileProcessing", inputsRecorded: true, inputsTruncated: false, outputsTruncated: false,
    inputs: [artifact("other:download", "unrelated"), artifact("target:download", "input")],
    outputs: [artifact("target:processed", "inner")], ...executionPatch,
  };
  return { id: 3510, status: WorkflowRunStatus.Waiting, currentStepIndex: 4,
    waitReason: step.waitReason, waitPromptJson: JSON.stringify({ targetId: "target", ...prompt }),
    planNodes: [{ nodeId: step.nodeId, order: 4 }], nodeExecutions: [step], ...patch } as Run;
}

it("reads the chosen current directory through the shared read-only explorer without querying the old manifest", async () => {
  files.mockClear();
  render(<ProcessingDirectoryFiles path="/work/task/current" />);
  expect(await screen.findByText("Current directory")).toBeVisible();
  expect(directoryProps).toHaveBeenLastCalledWith({ rootPath: "/work/task/current", maxContentHeight: 288, expandable: true,
    capabilities: ["enter-directory"], keyboard: false, watchChanges: false, selectable: "disabled" });
  expect(files).not.toHaveBeenCalled();
});

it("uses the shared managed explorer only with current acquisition identity", async () => {
  render(<ProcessingDirectoryFiles path="/work/task/current" taskContext={{ taskId: 10, runId: 20 }} />);
  await screen.findByText("Task current files");
  expect(taskDirectoryProps).toHaveBeenLastCalledWith({ path: "/work/task/current", context: { taskId: 10, runId: 20 }, maxContentHeight: 288 });
});

it("uses this waiting attempt and part, and a nested review's current result even when the input contains the previous layer", () => {
  expect(currentProcessingArtifact(waiting())).toBe("input");
  expect(currentProcessingArtifact(waiting({ deliveryReview: true, artifactId: "inner" }))).toBe("inner");
  expect(currentProcessingArtifact(waiting({ deliveryReview: true, artifactId: "unrelated" }))).toBeUndefined();
  const stale = waiting();
  stale.nodeExecutions.push({ ...stale.nodeExecutions[0], id: 21, status: "succeeded" });
  expect(currentProcessingArtifact(stale)).toBeUndefined();
});

it("does not guess ambiguous, truncated, or private intermediate inputs", () => {
  expect(currentProcessingArtifact(waiting({}, {}, { inputs: [artifact("target:processed", "other"),artifact("target:download","input")] }))).toBeUndefined();
  expect(currentProcessingArtifact(waiting({}, {}, { inputsTruncated: true }))).toBeUndefined();
  expect(currentProcessingArtifact(waiting({ knownStepId: "unpack", extractionPlanJson: JSON.stringify({ steps: [{ id: "unpack", input: "renamed" }] }) }))).toBeUndefined();
});

it("uses the exact local fs.processArtifacts input recorded on a single suspended item", () => {
  const local = { waitReason: "files.processing", kind:"fs.processArtifacts", outputs: [artifact("artifact.outputId","prior"),artifact("artifact.inputId","actual")] };
  expect(currentProcessingArtifact(waiting({}, {}, local))).toBe("actual");
  expect(currentProcessingArtifact(waiting({ archiveId: "private-archive" }, {}, local))).toBeUndefined();
  expect(currentProcessingArtifact(waiting({}, {}, { ...local, inputCount: 2 }))).toBeUndefined();
});

it("browses only direct children, paging within each directory and returning to root", async () => {
  files.mockImplementation((_run, id, { pageIndex, directory }) => Promise.resolve({ code: 0, data: {
    artifactId: id, pageIndex, relativeDirectory: directory || "", fileCount: 26, childCount: directory ? 26 : 1,
    totalBytes: 5000, state: pageIndex === 1 ? "available" : "storageunavailable",
    entries: directory ? [{relativePath: "folder/file-" + pageIndex + ".zip", length: 1024, isDirectory: false}]
      : [{relativePath: "folder", isDirectory: true}],
  }}));
  render(<CurrentProcessingFiles run={waiting()} />);
  fireEvent.click(await screen.findByRole("button", {name:"folder"}));
  expect(await screen.findByText("file-1.zip")).toBeVisible();
  expect(files).toHaveBeenCalledWith(3510,"input",{directory:"folder",pageIndex:1,pageSize:25,locateSingleFile:false},expect.objectContaining({sensitiveResponse:true}));
  fireEvent.click(screen.getByText("next page"));
  expect(await screen.findByText("file-2.zip")).toBeVisible();
  expect(screen.queryByText("file-1.zip")).toBeNull();
  expect(screen.getByText("browse recorded files")).toBeVisible();
  fireEvent.click(screen.getByRole("button", {name:"workflow.processing.inputFiles.root"}));
  expect(await screen.findByRole("button",{name:"folder"})).toBeVisible();
  expect(files).toHaveBeenLastCalledWith(3510,"input",{directory:"",pageIndex:1,pageSize:25,locateSingleFile:false},expect.anything());
});

it("keeps an unavailable or ambiguous file list from hiding the manual form or presenting another target's input", () => {
  render(<CurrentProcessingFiles run={waiting({ targetId: "missing" })} />);
  expect(screen.getByText("workflow.processing.inputFiles.missing")).toBeVisible();
  expect(screen.getByText("browse recorded files")).toBeVisible();
});


it("inspects the exact processed input of a waiting acquisition publication", () => {
  const publication = { kind: "fs.publishArtifacts", waitReason: "artifacts.publication",
    inputs: [artifact("target:processed", "processed"), artifact("other:processed", "unrelated")] };
  expect(currentProcessingArtifact(waiting({}, {}, publication))).toBe("processed");
  expect(currentProcessingArtifact(waiting({}, {}, { ...publication, kind: "fs.processArtifacts" }))).toBeUndefined();
  expect(currentProcessingArtifact(waiting({}, {}, { ...publication, inputs: [
    ...publication.inputs, artifact("target:download", "old-download"),
  ] }))).toBeUndefined();
});
