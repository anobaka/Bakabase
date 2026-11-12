import type { ComponentProps } from "react";

import { HeroUIProvider } from "@heroui/react";
import { act, cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import PostAcquisitionCatchUpModal from "../components/PostAcquisitionCatchUpModal";
import ActualModal from "@/components/bakaui/components/Modal";
import cn from "@/locales/cn/pages/postParser.json";
import acquireCn from "@/locales/cn/components/acquire.json";

const api = vi.hoisted(() => ({
  prepare: vi.fn(),
  confirm: vi.fn(),
  handoff: vi.fn(),
  retryHandoff: vi.fn(),
  reparse: vi.fn(),
  purchase: vi.fn(),
  changed: vi.fn(),
  portal: vi.fn(),
  destroyed: vi.fn(),
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    acquisition: {
      preparePostAcquisitionCatchUp: api.prepare,
      confirmPostAcquisitionCatchUp: api.confirm,
      getAcquisitionHandoff: api.handoff,
      retryAcquisitionHandoff: api.retryHandoff,
    },
    postParser: { reParsePostParserTask: api.reparse, purchasePostParserTaskContent: api.purchase },
  },
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: api.portal }),
}));
vi.mock("@/components/Acquire/PlanTask/PlanTaskDetailModal", () => ({
  default: ({ taskId }: { taskId: number }) => <p>Acquisition detail #{taskId}</p>,
}));
vi.mock("@/components/bakaui", async () => ({
  ...(await import("@heroui/react")),
  Button: (await import("@/components/bakaui/components/Button")).Button,
  Modal: (props: ComponentProps<typeof ActualModal>) => <ActualModal {...props} />,
}));
const translations = { ...acquireCn, ...cn } as Record<string, string>;
const translate = (key: string, values: Record<string, unknown> = {}) =>
  (
    translations[key] ?? (typeof values.defaultValue === "string" ? values.defaultValue : key)
  ).replace(/\{\{(\w+)\}\}/g, (_, name) => String(values[name] ?? ""));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: translate }) }));
const label = (key: string) => translate(`postParser.catchUp.${key}`);
type Props = ComponentProps<typeof PostAcquisitionCatchUpModal>;
type Selection = {
  taskId: number;
  revision: number;
  workflowRunId?: number | null;
  resultsHash: string;
};
type Item = {
  taskId: number;
  title?: string;
  status: string;
  code?: string | null;
  selection?: Selection | null;
  handoffId?: string | null;
  acquisitionTaskIds: number[];
};
const task = (updates: Partial<Props["task"]> = {}): Props["task"] => ({
  id: 41,
  revision: 3,
  workflowRunId: 76,
  title: "Saved post before automation",
  ...updates,
});
const selection = (updates: Partial<Selection> = {}): Selection => ({
  taskId: 41,
  revision: 3,
  workflowRunId: 76,
  resultsHash: "a".repeat(64),
  ...updates,
});
const item = (updates: Partial<Item> = {}): Item => ({
  taskId: 41,
  title: "Saved post before automation",
  status: "eligible",
  selection: selection(),
  acquisitionTaskIds: [],
  ...updates,
});
const preview = (record = item()) => ({
  code: 0,
  data: {
    requestedCount: 1,
    eligibleCount: Number(record.status === "eligible"),
    existingCount: Number(record.status === "existing"),
    rejectedCount: Number(record.status === "rejected"),
    items: [record],
  },
});
const result = (
  record = item({ status: "accepted", selection: null, handoffId: "queued-handoff" }),
) => ({
  code: 0,
  data: {
    requestedCount: 1,
    acceptedCount: Number(record.status === "accepted"),
    existingCount: Number(record.status === "existing"),
    rejectedCount: Number(record.status === "rejected"),
    items: [record],
  },
});
function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (error: Error) => void;
  const promise = new Promise<T>((done, fail) => {
    resolve = done;
    reject = fail;
  });
  return { promise, resolve, reject };
}
const show = (updates: Partial<Props> = {}) => {
  let props: Props = {
    task: task(),
    onChanged: api.changed,
    onDestroyed: api.destroyed,
    ...updates,
  };
  const view = render(
    <HeroUIProvider disableAnimation>
      <PostAcquisitionCatchUpModal {...props} />
    </HeroUIProvider>,
  );
  return {
    ...view,
    update: (next: Partial<Props>) => {
      props = { ...props, ...next };
      view.rerender(
        <HeroUIProvider disableAnimation>
          <PostAcquisitionCatchUpModal {...props} />
        </HeroUIProvider>,
      );
    },
  };
};
const confirmButton = () => screen.getByRole("button", { name: label("confirm") });
const refreshButton = () => screen.getByRole("button", { name: label("refresh") });
const ready = async () => {
  await screen.findByText(label("ready"));
  await waitFor(() => expect(confirmButton()).toBeEnabled());
};
const noParsingOrPurchase = () => {
  expect(api.reparse).not.toHaveBeenCalled();
  expect(api.purchase).not.toHaveBeenCalled();
};
beforeEach(() => {
  api.prepare.mockReset().mockResolvedValue(preview());
  api.confirm.mockReset().mockResolvedValue(result());
  api.handoff.mockReset().mockResolvedValue({ code: 0, data: undefined });
  for (const handler of [
    api.retryHandoff,
    api.reparse,
    api.purchase,
    api.changed,
    api.portal,
    api.destroyed,
  ])
    handler.mockReset();
});
afterEach(cleanup);

describe("single saved-result acquisition catch-up", () => {
  it("prepares the exact clicked task without side effects and submits only its accepted immutable selection", async () => {
    const view = show();
    await ready();
    expect(api.prepare).toHaveBeenCalledExactlyOnceWith(
      { taskIds: [41] },
      expect.objectContaining({ sensitiveResponse: true, showErrorToast: false }),
    );
    expect(api.confirm).not.toHaveBeenCalled();
    noParsingOrPurchase();
    const accepted = structuredClone(selection());
    api.prepare.mockResolvedValue(
      preview(item({ selection: selection({ resultsHash: "b".repeat(64) }) })),
    );
    view.update({ task: task({ title: "A later row-title update" }) });
    await userEvent.setup().click(confirmButton());
    expect(api.confirm).toHaveBeenCalledExactlyOnceWith(
      { tasks: [accepted] },
      expect.objectContaining({ sensitiveResponse: true, showErrorToast: false }),
    );
    expect(api.prepare).toHaveBeenCalledOnce();
    expect(await screen.findByRole("status")).toHaveTextContent(label("accepted"));
    expect(screen.getByRole("status")).toHaveTextContent("尚不代表下载或文件处理完成");
    expect(screen.queryByRole("button", { name: label("confirm") })).toBeNull();
    expect(api.changed).toHaveBeenCalledOnce();
    noParsingOrPurchase();
  });

  it.each([undefined, null])(
    "normalizes an absent workflow run to null while retaining an exact revision: %s",
    async (run) => {
      api.prepare.mockResolvedValue(
        preview(item({ selection: selection({ workflowRunId: null }) })),
      );
      show({ task: task({ workflowRunId: run }) });
      await ready();
      await userEvent.setup().click(confirmButton());
      expect(api.confirm.mock.calls[0][0].tasks).toEqual([selection({ workflowRunId: null })]);
    },
  );

  it.each([
    { title: "task ID", record: item({ taskId: 42, selection: selection({ taskId: 42 }) }) },
    { title: "revision", record: item({ selection: selection({ revision: 4 }) }) },
    { title: "workflow run", record: item({ selection: selection({ workflowRunId: 77 }) }) },
    {
      title: "result hash",
      record: item({ selection: selection({ resultsHash: "not-a-result-hash" }) }),
    },
    { title: "missing selection", record: item({ selection: null }) },
  ])("does not authorize a prepared manifest with a mismatched $title", async ({ record }) => {
    api.prepare.mockResolvedValue(preview(record));
    show();
    await screen.findByRole("alert");
    expect(screen.queryByRole("button", { name: label("confirm") })).toBeNull();
    expect(api.confirm).not.toHaveBeenCalled();
    noParsingOrPurchase();
  });

  it("does not authorize a selection when the clicked row has no saved revision", async () => {
    show({ task: task({ revision: undefined }) });
    await screen.findByRole("alert");
    expect(screen.queryByRole("button", { name: label("confirm") })).toBeNull();
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it("submits once and locks confirmation, refresh, dismissal and Escape while the outcome is pending", async () => {
    const pending = deferred<ReturnType<typeof result>>();
    api.confirm.mockReturnValue(pending.promise);
    show();
    await ready();
    const confirm = confirmButton();
    act(() => {
      confirm.click();
      confirm.click();
    });
    expect(api.confirm).toHaveBeenCalledOnce();
    expect(confirm).toBeDisabled();
    expect(refreshButton()).toBeDisabled();
    expect(screen.getByRole("button", { name: label("close") })).toBeDisabled();
    await userEvent.setup().keyboard("{Escape}");
    expect(screen.getByRole("dialog")).toBeVisible();
    await act(async () => pending.resolve(result()));
    expect(await screen.findByRole("status")).toHaveTextContent(label("accepted"));
    expect(api.confirm).toHaveBeenCalledOnce();
  });

  it("invalidates a prior manifest synchronously when refreshing before a same-frame confirmation", async () => {
    show();
    await ready();
    const pending = deferred<ReturnType<typeof preview>>();
    api.prepare.mockReturnValueOnce(pending.promise);
    const confirm = confirmButton();
    act(() => {
      refreshButton().click();
      confirm.click();
    });
    expect(api.confirm).not.toHaveBeenCalled();
    expect(api.prepare).toHaveBeenCalledTimes(2);
    await act(async () => pending.resolve(preview()));
    await ready();
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it("never resubmits an uncertain selection, even after refresh, and requires explicit approval of a genuinely new hash", async () => {
    api.confirm.mockRejectedValueOnce(new Error("Transport disconnected after submission"));
    show();
    await ready();
    await userEvent.setup().click(confirmButton());
    expect(await screen.findByRole("alert")).toHaveTextContent(label("submissionUnknown"));
    expect(screen.queryByText(label("ready"))).toBeNull();
    expect(confirmButton()).toBeDisabled();
    act(() => confirmButton().click());
    expect(api.confirm).toHaveBeenCalledOnce();
    await userEvent.setup().click(refreshButton());
    await waitFor(() => expect(api.prepare).toHaveBeenCalledTimes(2));
    expect(confirmButton()).toBeDisabled();
    expect(screen.getByRole("alert")).toHaveTextContent(label("submissionUnknown"));
    expect(screen.queryByText(label("ready"))).toBeNull();
    api.prepare.mockResolvedValueOnce(
      preview(item({ selection: selection({ resultsHash: "b".repeat(64) }) })),
    );
    await userEvent.setup().click(refreshButton());
    await ready();
    expect(api.confirm).toHaveBeenCalledOnce();
    await userEvent.setup().click(confirmButton());
    expect(api.confirm).toHaveBeenCalledTimes(2);
    expect(api.confirm.mock.calls[1][0].tasks).toEqual([
      selection({ resultsHash: "b".repeat(64) }),
    ]);
    noParsingOrPurchase();
  });

  it("shows existing task records without approving their included selection or restarting them", async () => {
    api.prepare.mockResolvedValue(
      preview(item({ status: "existing", acquisitionTaskIds: [701, 701, 702] })),
    );
    show();
    expect(await screen.findByRole("status")).toHaveTextContent(label("existing"));
    expect(screen.queryByRole("button", { name: label("confirm") })).toBeNull();
    const first = translate("acquire.handoff.openTask", { id: 701 });
    expect(screen.getAllByRole("button", { name: first })).toHaveLength(1);
    expect(
      screen.getByRole("button", { name: translate("acquire.handoff.openTask", { id: 702 }) }),
    ).toBeVisible();
    await userEvent.setup().click(screen.getByRole("button", { name: first }));
    const [Detail, detailProps] = api.portal.mock.lastCall!;
    render(
      <HeroUIProvider disableAnimation>
        <Detail {...detailProps} />
      </HeroUIProvider>,
    );
    expect(await screen.findByText("Acquisition detail #701")).toBeVisible();
    expect(api.changed).toHaveBeenCalledOnce();
    expect(api.confirm).not.toHaveBeenCalled();
    expect(api.retryHandoff).not.toHaveBeenCalled();
    noParsingOrPurchase();
  });

  it("reads an existing handoff without a task ID and displays its record without automatically retrying it", async () => {
    api.prepare.mockResolvedValue(
      preview(item({ status: "existing", handoffId: "old-handoff", selection: null })),
    );
    api.handoff.mockResolvedValue({
      code: 0,
      data: { id: "old-handoff", status: "attention", code: "HANDOFF_NEEDS_CONFIGURATION" },
    });
    show();
    await screen.findByText(translate("acquire.handoff.issue.HANDOFF_NEEDS_CONFIGURATION"));
    expect(api.handoff).toHaveBeenCalledExactlyOnceWith(
      "old-handoff",
      expect.objectContaining({ sensitiveResponse: true, showErrorToast: false }),
    );
    expect(screen.queryByRole("button", { name: label("confirm") })).toBeNull();
    expect(screen.queryByRole("button", { name: translate("acquire.handoff.retry") })).toBeNull();
    expect(api.confirm).not.toHaveBeenCalled();
    expect(api.retryHandoff).not.toHaveBeenCalled();
  });

  it.each(["HANDOFF_SOURCE_NOT_READY", "UNRECOGNIZED_REASON"])(
    "explains a rejected saved result without acquiring or reparsing it: %s",
    async (code) => {
      api.prepare.mockResolvedValue(preview(item({ status: "rejected", code, selection: null })));
      show();
      const reason = code === "UNRECOGNIZED_REASON" ? "unknown" : code;
      expect(await screen.findByRole("alert")).toHaveTextContent(label(`reason.${reason}`));
      expect(screen.queryByRole("button", { name: label("confirm") })).toBeNull();
      expect(api.confirm).not.toHaveBeenCalled();
      noParsingOrPurchase();
    },
  );

  it("discards a delayed prepare after the row identity changes and requires closing rather than silently binding a new task", async () => {
    const pending = deferred<ReturnType<typeof preview>>();
    api.prepare.mockReturnValue(pending.promise);
    const view = show();
    view.update({ task: task({ id: 42, revision: 4, workflowRunId: 77 }) });
    expect(await screen.findByRole("alert")).toHaveTextContent(label("changed"));
    await act(async () => pending.resolve(preview()));
    expect(api.prepare).toHaveBeenCalledExactlyOnceWith({ taskIds: [41] }, expect.any(Object));
    expect(screen.queryByRole("button", { name: label("confirm") })).toBeNull();
    expect(refreshButton()).toBeDisabled();
    expect(api.changed).not.toHaveBeenCalled();
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it.each(["success", "error"])("ignores a late prepare %s after unmount", async (mode) => {
    const pending = deferred<ReturnType<typeof preview>>();
    api.prepare.mockReturnValue(pending.promise);
    const view = show();
    view.unmount();
    await act(async () => {
      if (mode === "success") pending.resolve(preview());
      else pending.reject(new Error("Late prepare failure"));
    });
    expect(api.changed).not.toHaveBeenCalled();
    expect(api.confirm).not.toHaveBeenCalled();
    expect(api.handoff).not.toHaveBeenCalled();
    noParsingOrPurchase();
  });
});
