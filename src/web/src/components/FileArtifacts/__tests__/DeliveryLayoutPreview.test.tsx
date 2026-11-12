import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import Preview from "../DeliveryLayoutPreview";

const api = vi.hoisted(() => ({ preview: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({
  default: { workflow: { previewArtifactDeliveryLayout: api.preview } },
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, isDisabled }: any) => (
    <button disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Checkbox: ({ children, isSelected, onValueChange }: any) => (
    <label>
      <input
        type="checkbox"
        checked={isSelected}
        onChange={(e) => onValueChange(e.target.checked)}
      />
      {children}
    </label>
  ),
  Chip: ({ children }: any) => <span>{children}</span>,
  Spinner: (props: any) => <span role="status" aria-label={props["aria-label"]} />,
}));
const policy = { version: 1, removeGeneratedDirectories: true, collapseSingleRootDirectory: false };
const result = (id = "artifact-a", overrides = {}) => ({
  code: 0,
  data: {
    inputArtifactId: id,
    inputFingerprint: "manifest-a",
    policy,
    changed: true,
    fileCount: 230,
    changedFileCount: 230,
    directoryCount: 4,
    fileMappings: [
      { sourceRelativePath: "wrapper/author/file.jpg", targetRelativePath: "author/file.jpg" },
    ],
    directoryMappings: [
      { sourceRelativePath: "wrapper", targetRelativePath: "" },
      { sourceRelativePath: "wrapper/author/empty", targetRelativePath: "author/empty" },
    ],
    removedDirectories: ["wrapper"],
    truncated: true,
    state: "available",
    ...overrides,
  },
});
const generate = () =>
  fireEvent.click(screen.getByRole("button", { name: "artifact.deliveryLayout.previewAction" }));
beforeEach(() => api.preview.mockReset().mockResolvedValue(result()));

it("only calls the read-only preview on request and renders server-bound path/empty-directory mappings", async () => {
  render(<Preview runId={42} artifactId="artifact-a" />);
  expect(api.preview).not.toHaveBeenCalled();
  expect(screen.getByText("artifact.deliveryLayout.previewHint")).toBeVisible();
  generate();
  expect(await screen.findByText("author/file.jpg")).toBeVisible();
  expect(api.preview).toHaveBeenCalledExactlyOnceWith(42, "artifact-a", policy, {
    showErrorToast: false,
  });
  expect(screen.getByText("author/empty")).toBeVisible();
  expect(screen.getByText("artifact.deliveryLayout.previewRoot")).toBeVisible();
  expect(screen.getByText("artifact.deliveryLayout.previewTruncated")).toBeVisible();
});

it("discards a pending response immediately when the preview policy changes", async () => {
  let resolve!: (value: unknown) => void;
  api.preview.mockReturnValue(
    new Promise((value) => {
      resolve = value;
    }),
  );
  render(<Preview runId={42} artifactId="artifact-a" />);
  generate();
  fireEvent.click(
    screen.getByRole("checkbox", { name: "artifact.deliveryLayout.collapseSingleRoot" }),
  );
  await act(async () => resolve(result()));
  expect(screen.queryByText("author/file.jpg")).toBeNull();
  expect(screen.getByText("artifact.deliveryLayout.previewStale")).toBeVisible();
  expect(api.preview).toHaveBeenCalledTimes(1);
  api.preview.mockResolvedValue(
    result("artifact-a", { policy: { ...policy, collapseSingleRootDirectory: true } }),
  );
  generate();
  expect(await screen.findByText("author/file.jpg")).toBeVisible();
  expect(api.preview.mock.lastCall?.[2].collapseSingleRootDirectory).toBe(true);
});

it("does not display the previous artifact's response after switching inputs or runs", async () => {
  let resolve!: (value: unknown) => void;
  api.preview.mockReturnValue(
    new Promise((value) => {
      resolve = value;
    }),
  );
  const view = render(<Preview runId={42} artifactId="artifact-a" />);
  generate();
  view.rerender(<Preview runId={43} artifactId="artifact-b" />);
  await act(async () => resolve(result()));
  expect(screen.queryByText("author/file.jpg")).toBeNull();
  api.preview.mockResolvedValue(result("artifact-b"));
  generate();
  expect(await screen.findByText("author/file.jpg")).toBeVisible();
  expect(api.preview.mock.lastCall?.slice(0, 2)).toEqual([43, "artifact-b"]);
});

it.each([
  { code: 404, message: "private internal detail" },
  result("other-artifact"),
  result("artifact-a", { policy: { ...policy, collapseSingleRootDirectory: true } }),
])("fails closed for unavailable or incorrectly bound previews", async (response) => {
  api.preview.mockResolvedValue(response);
  render(<Preview runId={42} artifactId="artifact-a" />);
  generate();
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "artifact.deliveryLayout.previewFailed",
  );
  expect(screen.queryByText("private internal detail")).toBeNull();
  expect(screen.queryByText("author/file.jpg")).toBeNull();
  expect(api.preview).toHaveBeenCalledTimes(1);
});

it("handles a rejected request and allows an explicit retry without any mutation API", async () => {
  api.preview.mockRejectedValueOnce(new Error("network failed"));
  render(<Preview runId={42} artifactId="artifact-a" />);
  generate();
  expect(await screen.findByRole("alert")).toBeVisible();
  generate();
  await waitFor(() => expect(api.preview).toHaveBeenCalledTimes(2));
  expect(await screen.findByText("author/file.jpg")).toBeVisible();
});

it("shows an unchanged static manifest even when its physical artifact is retired", async () => {
  api.preview.mockResolvedValue(
    result("artifact-a", {
      changed: false,
      changedFileCount: 0,
      state: "retired",
      fileMappings: [],
      directoryMappings: [],
      removedDirectories: [],
    }),
  );
  render(<Preview runId={42} artifactId="artifact-a" />);
  generate();
  expect(await screen.findByText("artifact.deliveryLayout.previewUnchanged")).toBeVisible();
  expect(screen.getByText("workflow.metadata.artifactState.retired")).toBeVisible();
});
