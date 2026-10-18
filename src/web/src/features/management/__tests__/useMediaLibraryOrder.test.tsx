import { act, cleanup, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { createMediaLibraryOrderSource, useMediaLibraryOrder } from "../useMediaLibraryOrder";

import { useUiOptionsStore } from "@/stores/options";

const mocks = vi.hoisted(() => ({ patch: vi.fn() }));

vi.mock("@/sdk/BApi", () => ({
  default: { options: new Proxy({}, { get: () => mocks.patch }) },
}));

beforeEach(() => {
  vi.clearAllMocks();
  mocks.patch.mockResolvedValue({ code: 0 });
  useUiOptionsStore.setState({
    data: { mediaLibraryOrder: { keys: ["A", "B", "C", "D"] } },
    initialized: true,
  });
});
afterEach(cleanup);

describe("confirmed shared media-library order", () => {
  it("serializes concurrent disjoint edits against the latest confirmed preference", async () => {
    let finish!: (value: { code: number }) => void;

    mocks.patch.mockReturnValueOnce(
      new Promise((resolve) => {
        finish = resolve;
      }),
    );
    const first = renderHook(() => useMediaLibraryOrder(["A", "B"]));
    const second = renderHook(() => useMediaLibraryOrder(["C", "D"]));
    let one!: Promise<boolean>, two!: Promise<boolean>;

    await act(async () => {
      one = first.result.current.move("B", "A", ["A", "B"]);
      two = second.result.current.move("D", "C", ["C", "D"]);
      await Promise.resolve();
    });
    expect(mocks.patch).toHaveBeenCalledTimes(1);
    expect(useUiOptionsStore.getState().data.mediaLibraryOrder?.keys).toEqual(["A", "B", "C", "D"]);
    await act(async () => {
      finish({ code: 0 });
      await Promise.all([one, two]);
    });
    expect(mocks.patch).toHaveBeenLastCalledWith({
      mediaLibraryOrder: { keys: ["B", "A", "D", "C"] },
    });
    expect(first.result.current.keys).toEqual(["B", "A", "D", "C"]);
    expect(second.result.current.keys).toEqual(first.result.current.keys);
  });

  it("keeps the confirmed order on failure and allows the next queued change", async () => {
    mocks.patch.mockRejectedValueOnce(new Error("offline"));
    const order = renderHook(() => useMediaLibraryOrder(["A", "B"]));

    await act(async () =>
      expect(await order.result.current.move("B", "A", ["A", "B"])).toBe(false),
    );
    expect(order.result.current.error).toBe(true);
    expect(order.result.current.keys).toEqual(["A", "B", "C", "D"]);
    await act(async () => expect(await order.result.current.move("B", "A", ["A", "B"])).toBe(true));
    expect(order.result.current.error).toBe(false);
    expect(order.result.current.keys).toEqual(["B", "A", "C", "D"]);
  });

  it("exposes a stable portal snapshot that follows other views and preserves its original selection IDs", () => {
    const source = createMediaLibraryOrderSource([
      { value: "1", key: "A" },
      { value: "2", key: "B" },
    ]);
    const changed = vi.fn();
    const unsubscribe = source.subscribe(changed);
    const initial = source.getSnapshot();

    expect(initial).toEqual(["1", "2"]);
    expect(source.getSnapshot()).toBe(initial);
    useUiOptionsStore.getState().update({ mediaLibraryOrder: { keys: ["B", "A"] } });
    expect(changed).toHaveBeenCalledOnce();
    expect(source.getSnapshot()).toEqual(["2", "1"]);
    unsubscribe();
  });
});
