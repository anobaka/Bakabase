import { act, cleanup, renderHook, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { useMediaLibraryOrder } from "../useMediaLibraryOrder";
import {
  createMediaLibraryOrderSource,
  useConfiguredMediaLibraryChoices,
} from "../mediaLibraryChoiceOrder";
import { mediaLibraryChoiceValue } from "../mediaLibraryFilter";

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
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

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

describe("configured order in selection-only library editors", () => {
  const owner = { serverId: "owner", libraryEpoch: "epoch" };
  const libraries = [{ id: 1 }, { id: 2 }];
  const value = (library: { id: number }) => String(library.id);

  it("keeps native IDs and ignores another owner's or restored data directory's same-numbered libraries", () => {
    useUiOptionsStore.getState().update({
      mediaLibraryOrder: {
        keys: [
          mediaLibraryChoiceValue({ ...owner, serverId: "another" }, 1),
          mediaLibraryChoiceValue(owner, 2),
          mediaLibraryChoiceValue(owner, 1),
        ],
      },
    });
    const hook = renderHook(
      ({ currentOwner }) =>
        useConfiguredMediaLibraryChoices(libraries, value, { owner: currentOwner }),
      {
        initialProps: { currentOwner: owner },
      },
    );

    expect(hook.result.current.choices.map(value)).toEqual(["2", "1"]);
    const original = hook.result.current.source!;

    expect(original.getSnapshot()).toEqual(["2", "1"]);
    hook.rerender({ currentOwner: { ...owner, libraryEpoch: "restored" } });
    expect(hook.result.current.choices.map(value)).toEqual(["1", "2"]);
    expect(original.getSnapshot()).toEqual(["2", "1"]);
    expect(mocks.patch).not.toHaveBeenCalled();
  });

  it("uses qualified unified choices directly without qualifying them again", () => {
    const choices = libraries.map((library) => ({
      value: mediaLibraryChoiceValue(owner, library.id),
    }));

    useUiOptionsStore
      .getState()
      .update({ mediaLibraryOrder: { keys: choices.map((choice) => choice.value).reverse() } });
    const hook = renderHook(() =>
      useConfiguredMediaLibraryChoices(choices, (choice) => choice.value, { qualified: true }),
    );

    expect(hook.result.current.choices).toEqual([...choices].reverse());
    expect(hook.result.current.source!.getSnapshot()).toEqual(
      choices.map((choice) => choice.value).reverse(),
    );
    expect(mocks.patch).not.toHaveBeenCalled();
  });

  it("updates an already captured editor source when a local selector resolves its identity", async () => {
    let finish!: (response: unknown) => void;

    vi.stubGlobal(
      "fetch",
      vi.fn(
        () =>
          new Promise((resolve) => {
            finish = resolve;
          }),
      ),
    );
    useUiOptionsStore.getState().update({
      mediaLibraryOrder: {
        keys: [mediaLibraryChoiceValue(owner, 2), mediaLibraryChoiceValue(owner, 1)],
      },
    });
    const hook = renderHook(() =>
      useConfiguredMediaLibraryChoices(libraries, value, { local: true }),
    );
    const captured = hook.result.current.source!;
    const changed = vi.fn();
    const unsubscribe = captured.subscribe(changed);

    expect(captured.getSnapshot()).toEqual(["1", "2"]);
    await act(async () =>
      finish({ ok: true, json: async () => ({ sources: [{ ...owner, isLocal: true }] }) }),
    );
    await waitFor(() => expect(hook.result.current.choices.map(value)).toEqual(["2", "1"]));
    expect(hook.result.current.source).toBe(captured);
    expect(captured.getSnapshot()).toEqual(["2", "1"]);
    expect(changed).toHaveBeenCalled();
    expect(mocks.patch).not.toHaveBeenCalled();
    unsubscribe();
  });
});
