import type { ReactNode } from "react";
import type { components } from "@/sdk/BApi2";

import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { useDraftWorkflowValidation, useSavedWorkflowValidation } from "../useWorkflowValidation";

type Workflow =
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowDefinitionViewModel"];
const api = vi.hoisted(() => {
  const store = () => {
    let state = { data: {} as Record<string, unknown> };
    const listeners = new Set<(current: typeof state, previous: typeof state) => void>();

    return {
      getState: () => state,
      subscribe: (listener: (current: typeof state, previous: typeof state) => void) => {
        listeners.add(listener);

        return () => listeners.delete(listener);
      },
      update: (data: Record<string, unknown>) => {
        const previous = state;

        state = { data };
        listeners.forEach((listener) => listener(state, previous));
      },
    };
  };

  return { saved: vi.fn(), draft: vi.fn(), config: store(), ui: store(), baseUrl: "server-one" };
});

vi.mock("@/sdk/BApi", () => ({
  default: {
    get baseUrl() {
      return api.baseUrl;
    },
    workflow: { validateSavedWorkflow: api.saved, validateWorkflow: api.draft },
  },
}));
vi.mock("@/stores/options", () => ({
  optionsStores: { aiOptions: api.config, uiOptions: api.ui },
}));

const workflow = (id: number, extra: Partial<Workflow> = {}): Workflow => ({
  id,
  name: `Workflow ${id}`,
  triggerKind: "fs.manualScan",
  enabled: true,
  isBuiltin: false,
  createdAt: "2026-09-15T00:00:00Z",
  activities: [],
  ...extra,
});
const valid = { code: 0, data: { isValid: true, diagnostics: [] } };
const invalid = {
  code: 0,
  data: {
    isValid: false,
    diagnostics: [{ code: "missing", severity: "error", message: "Missing settings" }],
  },
};
const deferred = () => {
  let resolve!: (value: unknown) => void;
  const promise = new Promise((done) => {
    resolve = done;
  });

  return { promise, resolve };
};
let host: HTMLDivElement;
let root: Root;
let testRevision = 0;
let saved: ReturnType<typeof useSavedWorkflowValidation>;
let draft: ReturnType<typeof useDraftWorkflowValidation>;

function Saved({ definitions }: { definitions: Workflow[] }) {
  saved = useSavedWorkflowValidation(definitions);

  return <div>{definitions.map((item) => JSON.stringify(saved.getState(item.id))).join("\n")}</div>;
}
function Draft({ kind = "fs.manualScan" }: { kind?: string }) {
  draft = useDraftWorkflowValidation({ triggerKind: kind, activities: [] });

  return <div>{JSON.stringify(draft)}</div>;
}
async function render(node: ReactNode) {
  await act(async () => root.render(<>{node}</>));
}
async function advance(ms: number) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });
}
async function focus() {
  await act(async () => {
    window.dispatchEvent(new Event("focus"));
  });
}
beforeEach(() => {
  vi.useFakeTimers();
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  api.saved.mockReset().mockResolvedValue(valid);
  api.draft.mockReset().mockResolvedValue(valid);
  api.baseUrl = "server-one";
  api.config.update({ revision: ++testRevision });
  host = document.createElement("div");
  document.body.appendChild(host);
  root = createRoot(host);
});
afterEach(async () => {
  await act(async () => root.unmount());
  host.remove();
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe("saved workflow automatic validation", () => {
  it("does not reuse results for the same definition ID on a different backend", async () => {
    await render(<Saved definitions={[workflow(90)]} />);
    expect(saved.getState(90).result?.isValid).toBe(true);
    api.baseUrl = "server-two";
    api.saved.mockResolvedValueOnce(invalid);
    await render(<Saved definitions={[workflow(90)]} />);
    expect(api.saved).toHaveBeenCalledTimes(2);
    expect(saved.getState(90).result?.isValid).toBe(false);
  });
  it("starts at most three checks, deduplicates IDs, and advances queued checks as requests finish", async () => {
    const pending = Array.from({ length: 6 }, deferred);

    api.saved.mockImplementation((_id: number) => pending[api.saved.mock.calls.length - 1].promise);
    await render(<Saved definitions={[1, 2, 3, 4, 5, 6, 1].map((id) => workflow(id))} />);
    expect(api.saved.mock.calls.map(([id]) => id)).toEqual([1, 2, 3]);
    await focus();
    expect(api.saved).toHaveBeenCalledTimes(3);
    await act(async () => pending[0].resolve(valid));
    expect(api.saved.mock.calls.map(([id]) => id)).toEqual([1, 2, 3, 4]);
    await act(async () => pending[1].resolve(valid));
    await act(async () => pending[2].resolve(valid));
    expect(api.saved.mock.calls.map(([id]) => id)).toEqual([1, 2, 3, 4, 5, 6]);
    await act(async () => pending.slice(3).forEach((request) => request.resolve(valid)));
    expect(saved.getState(6).result?.isValid).toBe(true);
    expect(api.draft).not.toHaveBeenCalled();
  });

  it("deduplicates same-page checks, checks again after returning, and refreshes expired focus without polling", async () => {
    await render(<Saved definitions={[workflow(10)]} />);
    await render(<Saved definitions={[workflow(10)]} />);
    await focus();
    expect(api.saved).toHaveBeenCalledTimes(1);
    await render(null);
    await render(<Saved definitions={[workflow(10)]} />);
    expect(api.saved).toHaveBeenCalledTimes(2);
    expect(saved.getState(10).result?.isValid).toBe(true);
    await advance(60_001);
    expect(api.saved).toHaveBeenCalledTimes(2);
    await focus();
    await focus();
    expect(api.saved).toHaveBeenCalledTimes(3);
  });

  it("invalidates changed definitions, aborts stale checks, and ignores responses after leaving", async () => {
    const older = deferred();
    const newer = deferred();
    const leaving = deferred();

    api.saved
      .mockReturnValueOnce(older.promise)
      .mockReturnValueOnce(newer.promise)
      .mockReturnValueOnce(leaving.promise);
    await render(<Saved definitions={[workflow(20)]} />);
    const oldSignal = api.saved.mock.calls[0][1].signal;

    await render(<Saved definitions={[workflow(20, { triggerFilterJson: "changed" })]} />);
    expect(oldSignal.aborted).toBe(true);
    await act(async () => newer.resolve(valid));
    await act(async () => older.resolve(invalid));
    expect(saved.getState(20).result?.isValid).toBe(true);
    await render(<Saved definitions={[workflow(21)]} />);
    const leavingSignal = api.saved.mock.calls[2][1].signal;

    await render(null);
    expect(leavingSignal.aborted).toBe(true);
    await act(async () => leaving.resolve(invalid));
    expect(host.textContent).toBe("");
  });

  it("coalesces actual config changes but ignores unchanged snapshots and presentation settings", async () => {
    await render(<Saved definitions={[workflow(30)]} />);
    await act(async () => {
      api.config.update({ revision: testRevision });
      api.ui.update({ layout: "different" });
    });
    await advance(200);
    expect(api.saved).toHaveBeenCalledTimes(1);
    await act(async () => {
      api.config.update({ revision: testRevision, credential: "one" });
      api.config.update({ revision: testRevision, credential: "two" });
    });
    await advance(200);
    expect(api.saved).toHaveBeenCalledTimes(2);
  });

  it("marks API and network failures as unavailable and deduplicates a user's retry", async () => {
    api.saved.mockResolvedValueOnce({ code: 500, data: valid.data });
    await render(<Saved definitions={[workflow(40)]} />);
    expect(saved.getState(40)).toEqual({ failed: true });
    expect(api.saved).toHaveBeenLastCalledWith(40, {
      signal: expect.any(AbortSignal),
      showErrorToast: false,
    });
    const retry = deferred();

    api.saved.mockReturnValueOnce(retry.promise);
    await act(async () => {
      saved.retry(40);
      saved.retry(40);
    });
    expect(api.saved).toHaveBeenCalledTimes(2);
    await act(async () => retry.resolve(valid));
    expect(saved.getState(40).result?.isValid).toBe(true);
    api.saved.mockRejectedValueOnce(new Error("offline"));
    await act(async () => saved.retry(40));
    expect(saved.getState(40)).toEqual({ failed: true });
  });
});

describe("draft automatic validation", () => {
  it("debounces edits, cancels stale responses, and validates the latest unsaved draft", async () => {
    const older = deferred();

    api.draft.mockReturnValueOnce(older.promise);
    await render(<Draft />);
    await advance(250);
    await render(<Draft kind="postParser.manual" />);
    await advance(499);
    expect(api.draft).not.toHaveBeenCalled();
    await advance(1);
    expect(api.draft.mock.calls[0][0].triggerKind).toBe("postParser.manual");
    const oldSignal = api.draft.mock.calls[0][1].signal;

    await render(<Draft kind="fs.watch" />);
    expect(oldSignal.aborted).toBe(true);
    await advance(500);
    await act(async () => older.resolve(invalid));
    expect(draft.result?.isValid).toBe(true);
    expect(api.draft.mock.calls[1][0].triggerKind).toBe("fs.watch");
  });

  it("automatically rechecks config changes and retries failures without creating a run", async () => {
    api.draft.mockRejectedValueOnce(new Error("offline"));
    await render(<Draft />);
    await advance(500);
    expect(draft.failed).toBe(true);
    expect(api.draft).toHaveBeenLastCalledWith(
      { triggerKind: "fs.manualScan", activities: [] },
      { signal: expect.any(AbortSignal), showErrorToast: false },
    );
    await act(async () => draft.retry());
    await advance(500);
    expect(draft.result?.isValid).toBe(true);
    await act(async () =>
      api.config.update({ revision: testRevision, downloadDirectory: "/changed" }),
    );
    await advance(200);
    await advance(500);
    expect(api.draft).toHaveBeenCalledTimes(3);
    expect(api.saved).not.toHaveBeenCalled();
  });
});
