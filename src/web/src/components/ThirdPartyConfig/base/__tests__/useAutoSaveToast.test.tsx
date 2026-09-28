import { createRoot, type Root } from "react-dom/client";
import { act } from "react-dom/test-utils";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { useAutoSaveToast } from "../useAutoSaveToast";

const { success } = vi.hoisted(() => ({ success: vi.fn() }));

vi.mock("@/components/bakaui", () => ({ toast: { success } }));
vi.mock("react-i18next", () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}));

type Patch = { value: string };
type Response = { code?: number };

function deferred() {
  let resolve!: (response: Response) => void;
  let reject!: (reason: Error) => void;
  const promise = new Promise<Response>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });

  return { promise, resolve, reject };
}

let container: HTMLDivElement;
let root: Root;
let save: (patch: Patch) => void;

function Harness({ patchApi }: { patchApi: (patch: Patch) => Promise<Response> }) {
  save = useAutoSaveToast(patchApi);

  return null;
}

async function render(patchApi: (patch: Patch) => Promise<Response>) {
  await act(async () => root.render(<Harness patchApi={patchApi} />));
}

async function flushPromises() {
  await act(async () => {
    await Promise.resolve();
  });
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.useFakeTimers();
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

describe("useAutoSaveToast", () => {
  it("confirms a save only after the API returns code 0", async () => {
    const patchApi = vi.fn().mockResolvedValue({ code: 0 });

    await render(patchApi);
    save({ value: "enabled" });
    expect(patchApi).toHaveBeenCalledExactlyOnceWith({ value: "enabled" });
    await flushPromises();
    expect(success).not.toHaveBeenCalled();

    await act(async () => vi.runAllTimers());
    expect(success).toHaveBeenCalledExactlyOnceWith("thirdPartyConfig.success.saved");
  });

  it("never confirms business failures, missing codes, or rejected requests", async () => {
    const patchApi = vi
      .fn()
      .mockResolvedValueOnce({ code: 409 })
      .mockResolvedValueOnce({})
      .mockRejectedValueOnce(new Error("offline"));

    await render(patchApi);
    save({ value: "business failure" });
    await flushPromises();
    save({ value: "missing code" });
    await flushPromises();
    save({ value: "network failure" });
    await flushPromises();
    await act(async () => vi.runAllTimers());

    expect(success).not.toHaveBeenCalled();
  });

  it("combines rapid successful edits into one notification", async () => {
    const patchApi = vi.fn().mockResolvedValue({ code: 0 });

    await render(patchApi);
    save({ value: "first" });
    await flushPromises();
    await act(async () => vi.advanceTimersByTime(300));
    save({ value: "second" });
    await flushPromises();
    await act(async () => vi.advanceTimersByTime(300));
    expect(success).not.toHaveBeenCalled();
    await act(async () => vi.runAllTimers());

    expect(patchApi).toHaveBeenCalledTimes(2);
    expect(success).toHaveBeenCalledTimes(1);
  });

  it("ignores an older success if a newer save fails", async () => {
    const first = deferred();
    const second = deferred();
    const patchApi = vi.fn().mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise);

    await render(patchApi);
    save({ value: "first" });
    save({ value: "second" });
    second.resolve({ code: 409 });
    first.resolve({ code: 0 });
    await flushPromises();
    await act(async () => vi.runAllTimers());

    expect(success).not.toHaveBeenCalled();
  });

  it("keeps a pending confirmation after the panel unmounts", async () => {
    const patchApi = vi.fn().mockResolvedValue({ code: 0 });

    await render(patchApi);
    save({ value: "first" });
    await flushPromises();
    await act(async () => root.render(null));
    await act(async () => vi.runAllTimers());

    expect(success).toHaveBeenCalledExactlyOnceWith("thirdPartyConfig.success.saved");
  });

  it("confirms an in-flight save that succeeds after the panel unmounts", async () => {
    const pending = deferred();
    const patchApi = vi.fn().mockReturnValue(pending.promise);

    await render(patchApi);
    save({ value: "first" });
    await act(async () => root.render(null));
    pending.resolve({ code: 0 });
    await flushPromises();
    await act(async () => vi.runAllTimers());

    expect(success).toHaveBeenCalledExactlyOnceWith("thirdPartyConfig.success.saved");
  });

  it("coalesces rapid saves across remounts that use the same API method", async () => {
    const patchApi = vi.fn().mockResolvedValue({ code: 0 });

    await render(patchApi);
    save({ value: "first" });
    await flushPromises();
    await act(async () => vi.advanceTimersByTime(300));
    await act(async () => root.render(null));
    await render(patchApi);
    save({ value: "second" });
    await flushPromises();
    await act(async () => vi.advanceTimersByTime(300));
    expect(success).not.toHaveBeenCalled();
    await act(async () => vi.runAllTimers());

    expect(patchApi).toHaveBeenCalledTimes(2);
    expect(success).toHaveBeenCalledTimes(1);
  });
});
