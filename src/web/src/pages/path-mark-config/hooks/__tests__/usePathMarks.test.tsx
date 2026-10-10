import { act, cleanup, renderHook, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { usePathMarks } from "../usePathMarks";

import { IwFsType } from "@/sdk/constants";

const api = vi.hoisted(() => ({ getAll: vi.fn(), entry: vi.fn() }));

vi.mock("@/sdk/BApi", () => ({
  default: { pathMark: { getAllPathMarks: api.getAll }, file: { getIwFsEntry: api.entry } },
}));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/stores/pathMarks", () => ({
  usePathMarksStore: { getState: () => ({ setMarks: vi.fn() }) },
}));

const path = "E:/Downloads";
const mark = { id: 1, path, syncStatus: 2 };

beforeEach(() => {
  vi.clearAllMocks();
  api.getAll.mockResolvedValue({ code: 0, data: [mark] });
});
afterEach(cleanup);

describe("server path availability", () => {
  it("keeps a refused Docker path unknown with its actual reason and preserves the mark", async () => {
    const response = Object.assign(new Response(null, { status: 400 }), {
      error: { message: "Choose a folder inside a mounted storage location. E:/Downloads" },
    });

    api.entry.mockRejectedValue(response);
    const { result } = renderHook(() => usePathMarks());

    await waitFor(() => expect(result.current.pathErrorsMap.size).toBe(1));
    expect(result.current.pathErrorsMap.get(path)).toBe(
      "fileExplorer.storage.pathRejected E:/Downloads",
    );
    expect(result.current.pathExistsMap.has(path)).toBe(false);
    expect(result.current.getInvalidPathsCount()).toBe(0);
    expect(result.current.allMarks).toEqual([mark]);
    expect(api.entry.mock.calls[0][1]).toMatchObject({
      showErrorToast: false,
      signal: expect.any(AbortSignal),
    });
  });

  it("distinguishes genuinely missing paths from failed checks and can retry after a mount recovers", async () => {
    api.entry.mockResolvedValue({ code: 0, data: { type: IwFsType.Invalid } });
    const { result } = renderHook(() => usePathMarks());

    await waitFor(() => expect(result.current.getInvalidPathsCount()).toBe(1));
    expect(result.current.pathErrorsMap.size).toBe(0);
    api.entry.mockResolvedValue({ code: 0, data: { type: IwFsType.Directory } });
    act(() => result.current.retryPathChecks());
    await waitFor(() => expect(result.current.pathExistsMap.get(path)).toBe(true));
    expect(result.current.getInvalidPathsCount()).toBe(0);
  });

  it("does not turn an API-level failure into a missing directory and clears it after retry", async () => {
    api.entry.mockResolvedValue({ code: 400, message: "Mount is not available" });
    const { result } = renderHook(() => usePathMarks());

    await waitFor(() =>
      expect(result.current.pathErrorsMap.get(path)).toBe("Mount is not available"),
    );
    api.entry.mockResolvedValue({ code: 0, data: { type: IwFsType.Directory } });
    act(() => result.current.retryPathChecks());
    await waitFor(() => expect(result.current.pathExistsMap.get(path)).toBe(true));
    expect(result.current.pathErrorsMap.size).toBe(0);
  });

  it("does not issue unused existence probes for the tree decoration hook", async () => {
    const { result } = renderHook(() => usePathMarks({ checkExistence: false }));

    await waitFor(() => expect(result.current.allMarks).toHaveLength(1));
    expect(api.entry).not.toHaveBeenCalled();
  });

  it("ignores a stale path result after refreshing the list", async () => {
    let finish!: (value: unknown) => void;

    api.entry.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    const { result } = renderHook(() => usePathMarks());

    await waitFor(() => expect(api.entry).toHaveBeenCalledTimes(1));
    api.getAll.mockResolvedValue({ code: 0, data: [{ ...mark, path: "/media" }] });
    api.entry.mockResolvedValue({ code: 0, data: { type: IwFsType.Directory } });
    await act(async () => {
      await result.current.loadAllMarks();
    });
    await waitFor(() => expect(result.current.pathExistsMap.get("/media")).toBe(true));
    await act(async () => {
      finish({ code: 0, data: { type: IwFsType.Invalid } });
    });
    expect(result.current.pathExistsMap.has(path)).toBe(false);
    expect(result.current.pathErrorsMap.size).toBe(0);
  });

  it("updates a failed check's reason and clears the old error when its path is removed", async () => {
    api.entry.mockRejectedValue(new Error("First failure"));
    const { result } = renderHook(() => usePathMarks());

    await waitFor(() => expect(result.current.pathErrorsMap.get(path)).toBe("First failure"));
    api.entry.mockResolvedValue({ code: 400, message: "Mount changed" });
    act(() => result.current.retryPathChecks());
    await waitFor(() => expect(result.current.pathErrorsMap.get(path)).toBe("Mount changed"));

    api.getAll.mockResolvedValue({ code: 0, data: [] });
    await act(async () => {
      await result.current.loadAllMarks();
    });
    await waitFor(() => expect(result.current.pathErrorsMap.size).toBe(0));
    expect(result.current.pathExistsMap.size).toBe(0);
    expect(result.current.checkingPaths).toBe(false);
  });

  it("aborts a pending check and clears its busy state when no marked paths remain", async () => {
    let finish!: (value: unknown) => void;

    api.entry.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    const { result } = renderHook(() => usePathMarks());

    await waitFor(() => expect(result.current.checkingPaths).toBe(true));
    const signal = api.entry.mock.calls[0][1].signal as AbortSignal;

    api.getAll.mockResolvedValue({ code: 0, data: [] });
    await act(async () => {
      await result.current.loadAllMarks();
    });
    await waitFor(() => expect(result.current.checkingPaths).toBe(false));
    expect(signal.aborted).toBe(true);
    await act(async () => {
      finish({ code: 0, data: { type: IwFsType.Invalid } });
    });
    expect(result.current.pathExistsMap.size).toBe(0);
    expect(result.current.pathErrorsMap.size).toBe(0);
  });
});
