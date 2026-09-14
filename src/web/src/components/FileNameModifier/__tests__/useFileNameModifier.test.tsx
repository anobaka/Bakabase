import type { OperationWithId } from "../useFileNameModifier";

import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { beforeEach, afterEach, describe, expect, it, vi } from "vitest";

import { useFileNameModifier } from "../useFileNameModifier";

import { FileNameModifierOperationType as OperationType } from "@/sdk/constants";

const api = vi.hoisted(() => ({ preview: vi.fn(), modify: vi.fn() }));

vi.mock("@/sdk/BApi", () => ({
  default: {
    fileNameModifier: { previewFileNameModification: api.preview, modifyFileNames: api.modify },
  },
}));

const operation = (overrides: Partial<OperationWithId> = {}): OperationWithId => ({
  id: "first",
  target: 2,
  operation: OperationType.Insert,
  position: 1,
  positionIndex: 0,
  text: "new-",
  deleteCount: 0,
  deleteStartPosition: 0,
  caseType: 1,
  alphabetStartChar: "A",
  alphabetCount: 1,
  replaceEntire: false,
  regex: false,
  ...overrides,
});
let root: Root;
let container: HTMLDivElement;
let state: ReturnType<typeof useFileNameModifier>;
const initial = ["/library/a.jpg", "/library/b.jpg"];

function Harness() {
  state = useFileNameModifier(initial);

  return null;
}
const tick = async (ms = 300) => {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });
};
const change = async (operations: OperationWithId[]) => {
  await act(async () => state.setOperations(operations));
};

beforeEach(async () => {
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  vi.useFakeTimers();
  api.preview.mockReset();
  api.modify.mockReset();
  api.preview.mockResolvedValue({ code: 0, data: ["/library/new-a.jpg", "/library/new-b.jpg"] });
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
  await act(async () => root.render(<Harness />));
});
afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe("rename preview and execution", () => {
  it("invalidates the displayed preview immediately, before debounce elapses", async () => {
    await change([operation()]);
    await tick();
    expect(state.canExecute).toBe(true);
    await change([operation({ text: "next-" })]);
    expect(state.canExecute).toBe(false);
    expect(state.isPreviewLoading).toBe(true);
    await act(async () => {
      await state.execute();
    });
    expect(api.modify).not.toHaveBeenCalled();
    await tick(299);
    expect(api.preview).toHaveBeenCalledTimes(1);
  });

  it("ignores older responses even when they resolve after a newer preview", async () => {
    let oldResult!: (value: unknown) => void;

    api.preview.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          oldResult = resolve;
        }),
    );
    await change([operation()]);
    await tick();
    await change([operation({ text: "next-" })]);
    api.preview.mockResolvedValueOnce({
      code: 0,
      data: ["/library/next-a.jpg", "/library/next-b.jpg"],
    });
    await tick();
    expect(state.previewResults[0].modifiedPath).toBe("/library/next-a.jpg");
    await act(async () =>
      oldResult({ code: 0, data: ["/library/old-a.jpg", "/library/old-b.jpg"] }),
    );
    expect(state.previewResults[0].modifiedPath).toBe("/library/next-a.jpg");
  });

  it("does not silently skip an invalid rule in a mixed rule chain", async () => {
    await change([operation(), operation({ id: "invalid", text: "" })]);
    await tick();
    expect(state.hasInvalidOperations).toBe(true);
    expect(api.preview).not.toHaveBeenCalled();
    expect(state.canExecute).toBe(false);
    await act(async () => {
      await state.execute();
    });
    expect(api.modify).not.toHaveBeenCalled();
  });

  it("blocks execution after a failed or incomplete preview response", async () => {
    api.preview.mockResolvedValueOnce({
      code: 400,
      message: "Invalid regular expression",
      data: ["/library/new-a.jpg", "/library/new-b.jpg"],
    });
    await change([operation()]);
    await tick();
    expect(state.error).toBe("Invalid regular expression");
    expect(state.canExecute).toBe(false);
    api.preview.mockResolvedValueOnce({ code: 0, data: ["/library/new-a.jpg"] });
    await act(async () => state.refreshPreview());
    await tick();
    expect(state.canExecute).toBe(false);
    expect(state.error).toBe("FileNameModifier.PreviewFailed");
  });

  it("submits the full previewed input once and retains failed source paths", async () => {
    const ordered = [
      operation(),
      operation({ id: "second", operation: OperationType.ChangeCase, caseType: 2 }),
    ];

    await change(ordered);
    await tick();
    api.modify.mockResolvedValue({
      code: 0,
      data: [
        { oldPath: initial[0], newPath: "/library/new-a.jpg", success: true },
        {
          oldPath: initial[1],
          newPath: "/library/new-b.jpg",
          success: false,
          error: "Target exists",
        },
      ],
    });
    await act(async () => {
      const first = state.execute();
      const second = state.execute();

      await Promise.all([first, second]);
    });
    expect(api.modify).toHaveBeenCalledTimes(1);
    expect(api.modify.mock.calls[0][0]).toEqual(api.preview.mock.calls[0][0]);
    expect(api.modify.mock.calls[0][0].operations.map((item: OperationWithId) => item.id)).toEqual([
      undefined,
      undefined,
    ]);
    expect(state.filePaths).toEqual(["/library/new-a.jpg", initial[1]]);
    expect(state.lastFilePaths).toEqual(initial);
  });

  it("keeps mutation failures visible and does not invent successful paths", async () => {
    await change([operation()]);
    await tick();
    api.modify.mockResolvedValue({ code: 400, message: "Cannot rename here" });
    await act(async () => {
      await state.execute();
    });
    expect(state.error).toBe("Cannot rename here");
    expect(state.filePaths).toEqual(initial);
    expect(state.lastFilePaths).toBeNull();
  });

  it("does not restore a stale preview after the input list is cleared", async () => {
    let resolvePreview!: (value: unknown) => void;

    api.preview.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          resolvePreview = resolve;
        }),
    );
    await change([operation()]);
    await tick();
    await act(async () => state.setFilePaths([]));
    await act(async () =>
      resolvePreview({ code: 0, data: ["/library/new-a.jpg", "/library/new-b.jpg"] }),
    );
    expect(state.previewResults).toEqual([]);
    expect(state.canExecute).toBe(false);
  });
});
