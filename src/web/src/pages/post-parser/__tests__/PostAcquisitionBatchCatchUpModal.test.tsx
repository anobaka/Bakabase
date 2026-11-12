import type { ComponentProps, ReactNode } from "react";
import type { CatchUpItem, CatchUpPreview, CatchUpResult } from "../batchCatchUp";

import { HeroUIProvider } from "@heroui/react";
import { act, cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import PostAcquisitionBatchCatchUpModal from "../components/PostAcquisitionBatchCatchUpModal";
import FilteredTaskActions from "../components/FilteredTaskActions";
import ActualModal from "@/components/bakaui/components/Modal";
import cn from "@/locales/cn/pages/postParser.json";
import en from "@/locales/en/pages/postParser.json";
import acquireCn from "@/locales/cn/components/acquire.json";

const api = vi.hoisted(() => ({
  filtered: vi.fn(),
  prepare: vi.fn(),
  confirm: vi.fn(),
  changed: vi.fn(),
  portal: vi.fn(),
  closed: vi.fn(),
  reparse: vi.fn(),
  purchase: vi.fn(),
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    acquisition: {
      prepareFilteredPostAcquisitionCatchUp: api.filtered,
      preparePostAcquisitionCatchUp: api.prepare,
      confirmPostAcquisitionCatchUp: api.confirm,
    },
    postParser: { reParsePostParserTask: api.reparse, purchasePostParserTaskContent: api.purchase },
  },
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: api.portal }),
}));
vi.mock("@/components/ExternalLink", () => ({
  default: ({ href, children }: { href: string; children: ReactNode }) => (
    <a href={href}>{children}</a>
  ),
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
const label = (key: string, values?: Record<string, unknown>) =>
  translate(`postParser.batchCatchUp.${key}`, values);
const item = (id: number, updates: Partial<CatchUpItem> = {}): CatchUpItem => ({
  taskId: id,
  title: `Saved result ${id}`,
  link: `https://example.test/posts/${id}`,
  status: "eligible",
  selection: { taskId: id, revision: 3, workflowRunId: id + 500, resultsHash: "a".repeat(64) },
  acquisitionTaskIds: [],
  ...updates,
});
const preview = (items: CatchUpItem[]): CatchUpPreview => ({
  requestedCount: items.length,
  eligibleCount: items.filter((item) => item.status === "eligible").length,
  existingCount: items.filter((item) => item.status === "existing").length,
  rejectedCount: items.filter((item) => item.status === "rejected").length,
  items,
});
const filtered = (items: CatchUpItem[]) => ({
  code: 0,
  data: {
    matchedCount: items.length,
    maxTasks: 1000,
    limitExceeded: false,
    preview: preview(items),
  },
});
const result = (items: CatchUpItem[]): CatchUpResult => ({
  requestedCount: items.length,
  acceptedCount: items.filter((item) => item.status === "accepted").length,
  existingCount: items.filter((item) => item.status === "existing").length,
  rejectedCount: items.filter((item) => item.status === "rejected").length,
  items,
});
const baseItems = () => [
  item(1),
  item(2),
  item(3, { status: "existing", acquisitionTaskIds: [800] }),
  item(4, { status: "rejected", code: "HANDOFF_SOURCE_NOT_READY", selection: undefined }),
];
const scope = { keyword: "  saved  ", status: "success" as const, reasonCode: "SOURCE_INVALID" };
type Props = ComponentProps<typeof PostAcquisitionBatchCatchUpModal>;
const show = (updates: Partial<Props> = {}) => {
  let props: Props = { scope, onChanged: api.changed, onClosed: api.closed, ...updates };
  const view = render(
    <HeroUIProvider disableAnimation>
      <PostAcquisitionBatchCatchUpModal {...props} />
    </HeroUIProvider>,
  );
  return {
    ...view,
    update: (updates: Partial<Props>) => {
      props = { ...props, ...updates };
      view.rerender(
        <HeroUIProvider disableAnimation>
          <PostAcquisitionBatchCatchUpModal {...props} />
        </HeroUIProvider>,
      );
    },
  };
};
const confirm = (count: number) =>
  screen.getByRole("button", { name: label("confirm", { count }) });
const refresh = () => screen.getByRole("button", { name: label("refresh") });
const checkbox = (id: number) =>
  screen.getByRole("checkbox", { name: label("selectTask", { id }) });
const ready = async (count = 2) => waitFor(() => expect(confirm(count)).toBeEnabled());
function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => {
    resolve = done;
  });
  return { promise, resolve };
}
beforeEach(() => {
  for (const fn of Object.values(api)) fn.mockReset();
  api.filtered.mockResolvedValue(filtered(baseItems()));
  api.prepare.mockResolvedValue({ code: 0, data: preview(baseItems()) });
  api.confirm.mockImplementation(async ({ tasks }) => ({
    code: 0,
    data: result(
      tasks.map((selection: { taskId: number }) =>
        item(selection.taskId, {
          status: "accepted",
          selection: undefined,
          handoffId: `queued-${selection.taskId}`,
        }),
      ),
    ),
  }));
});
afterEach(cleanup);

describe("filtered saved-result acquisition catch-up", () => {
  it("reviews every matching page without mutation, skips existing/rejected records, and submits only checked frozen results", async () => {
    show();
    await ready();
    expect(api.filtered).toHaveBeenCalledExactlyOnceWith(
      { keyword: "saved", status: "success", reasonCode: "SOURCE_INVALID" },
      expect.objectContaining({ sensitiveResponse: true }),
    );
    expect(checkbox(1)).toBeChecked();
    expect(checkbox(3)).toBeDisabled();
    expect(checkbox(4)).toBeDisabled();
    expect(screen.getByRole("link", { name: "#1 Saved result 1" })).toHaveAttribute(
      "href",
      "https://example.test/posts/1",
    );
    expect(
      screen.getByText(translate("postParser.catchUp.reason.HANDOFF_SOURCE_NOT_READY")),
    ).toBeVisible();
    expect(api.confirm).not.toHaveBeenCalled();
    await userEvent.setup().click(checkbox(2));
    await userEvent.setup().click(confirm(1));
    expect(api.confirm).toHaveBeenCalledExactlyOnceWith(
      { tasks: [item(1).selection] },
      expect.any(Object),
    );
    expect(await screen.findByRole("status")).toHaveTextContent("已入队 1 项");
    expect(screen.getByRole("status")).toHaveTextContent("入队不代表下载或处理已完成");
    expect(api.reparse).not.toHaveBeenCalled();
    expect(api.purchase).not.toHaveBeenCalled();
  });

  it("preserves selection across result pages and uses a cross-page select-all", async () => {
    api.filtered.mockResolvedValue(filtered(Array.from({ length: 25 }, (_, i) => item(i + 1))));
    show();
    await ready(25);
    const all = screen.getByRole("checkbox", { name: label("selectAll", { count: 25 }) });
    await userEvent.setup().click(all);
    expect(confirm(0)).toBeDisabled();
    await userEvent.setup().click(all);
    expect(confirm(25)).toBeEnabled();
    await userEvent.setup().click(checkbox(1));
    await userEvent.setup().click(screen.getByRole("button", { name: "pagination item 2" }));
    expect(checkbox(21)).toBeChecked();
    await userEvent.setup().click(checkbox(21));
    await userEvent.setup().click(confirm(23));
    expect(
      api.confirm.mock.calls[0][0].tasks.map((task: { taskId: number }) => task.taskId),
    ).toEqual(Array.from({ length: 25 }, (_, i) => i + 1).filter((id) => ![1, 21].includes(id)));
  });

  it("refreshes only the first snapshot's IDs, even if the surrounding page filter changes", async () => {
    const view = show();
    await ready();
    view.update({ scope: { keyword: "unrelated", status: "all" } });
    await userEvent.setup().click(refresh());
    await ready();
    expect(api.filtered).toHaveBeenCalledOnce();
    expect(api.prepare).toHaveBeenCalledExactlyOnceWith(
      { taskIds: [1, 2, 3, 4] },
      expect.any(Object),
    );
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it("preserves manual exclusions across refresh and leaves newly eligible results unselected", async () => {
    show();
    await ready();
    await userEvent.setup().click(checkbox(2));
    api.prepare.mockResolvedValueOnce({
      code: 0,
      data: preview([item(1), item(2), item(3), item(4)]),
    });
    await userEvent.setup().click(refresh());
    await ready(1);
    expect(checkbox(1)).toBeChecked();
    expect(checkbox(2)).not.toBeChecked();
    expect(checkbox(3)).not.toBeChecked();
    expect(checkbox(4)).not.toBeChecked();
    await userEvent.setup().click(confirm(1));
    expect(api.confirm.mock.calls[0][0].tasks).toEqual([item(1).selection]);
  });

  it("requires explicit reselection if refresh finds a different saved-result hash", async () => {
    show();
    await ready();
    api.prepare.mockResolvedValueOnce({
      code: 0,
      data: preview(
        baseItems().map((value) =>
          value.taskId === 1
            ? { ...value, selection: { ...value.selection!, resultsHash: "b".repeat(64) } }
            : value,
        ),
      ),
    });
    await userEvent.setup().click(refresh());
    await ready(1);
    expect(checkbox(1)).not.toBeChecked();
    expect(checkbox(2)).toBeChecked();
  });

  it("does not silently truncate an oversized filter or submit anything", async () => {
    api.filtered.mockResolvedValue({
      code: 0,
      data: { matchedCount: 1001, maxTasks: 1000, limitExceeded: true },
    });
    show();
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "当前匹配 1001 项，单次最多检查 1000 项",
    );
    expect(confirm(0)).toBeDisabled();
    expect(screen.queryByRole("list", { name: label("list") })).toBeNull();
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it("keeps a zero-match snapshot empty on refresh instead of adding new matches", async () => {
    api.filtered.mockResolvedValue(filtered([]));
    show();
    await screen.findByText(label("empty"));
    api.filtered.mockResolvedValue(filtered(baseItems()));
    await userEvent.setup().click(refresh());
    await screen.findByText(label("empty"));
    expect(api.filtered).toHaveBeenCalledOnce();
    expect(api.prepare).not.toHaveBeenCalled();
    expect(confirm(0)).toBeDisabled();
  });

  it.each(["duplicate", "missingHash", "wrongCounts"])(
    "fails closed for a malformed %s preview",
    async (mode) => {
      const response = filtered(baseItems());
      if (mode === "duplicate") response.data.preview.items[1] = item(1);
      if (mode === "missingHash") response.data.preview.items[0].selection!.resultsHash = "";
      if (mode === "wrongCounts") response.data.preview.eligibleCount = 3;
      api.filtered.mockResolvedValue(response);
      show();
      await screen.findByRole("alert");
      expect(confirm(0)).toBeDisabled();
      expect(api.confirm).not.toHaveBeenCalled();
    },
  );

  it("rejects an incomplete or expanded refresh snapshot", async () => {
    show();
    await ready();
    api.prepare.mockResolvedValue({ code: 0, data: preview([...baseItems(), item(99)]) });
    await userEvent.setup().click(refresh());
    await screen.findByRole("alert");
    expect(confirm(0)).toBeDisabled();
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it("locks confirmation, dismissal and same-frame repeats until the submission resolves", async () => {
    const pending = deferred<{ code: number; data: CatchUpResult }>();
    api.confirm.mockReturnValue(pending.promise);
    show();
    await ready();
    const button = confirm(2);
    act(() => {
      button.click();
      button.click();
    });
    expect(api.confirm).toHaveBeenCalledOnce();
    expect(refresh()).toBeDisabled();
    expect(
      screen.getByRole("button", { name: translate("postParser.catchUp.close") }),
    ).toBeDisabled();
    await userEvent.setup().keyboard("{Escape}");
    expect(screen.getByRole("dialog")).toBeVisible();
    await act(async () =>
      pending.resolve({
        code: 0,
        data: result([item(1, { status: "accepted" }), item(2, { status: "accepted" })]),
      }),
    );
    await screen.findByRole("status");
    expect(api.confirm).toHaveBeenCalledOnce();
  });

  it("never resubmits uncertain selections and resolves them only with a read-only refresh", async () => {
    api.confirm.mockRejectedValueOnce(new Error("transport disconnected"));
    show();
    await ready();
    await userEvent.setup().click(confirm(2));
    expect(await screen.findByRole("alert")).toHaveTextContent(
      translate("postParser.catchUp.submissionUnknown"),
    );
    expect(confirm(0)).toBeDisabled();
    await userEvent.setup().click(refresh());
    await waitFor(() => expect(api.prepare).toHaveBeenCalledOnce());
    expect(checkbox(1)).toBeDisabled();
    expect(confirm(0)).toBeDisabled();
    expect(api.confirm).toHaveBeenCalledOnce();
    api.prepare.mockResolvedValueOnce({
      code: 0,
      data: preview(
        baseItems().map((value) => ({
          ...value,
          status: "existing",
          acquisitionTaskIds: [value.taskId + 1000],
        })),
      ),
    });
    await userEvent.setup().click(refresh());
    await waitFor(() => expect(screen.queryByRole("alert")).toBeNull());
    expect(api.confirm).toHaveBeenCalledOnce();
  });

  it("does not report partial or mismatched confirmation responses as successful", async () => {
    api.confirm.mockResolvedValue({ code: 0, data: result([item(1, { status: "accepted" })]) });
    show();
    await ready();
    await userEvent.setup().click(confirm(2));
    expect(await screen.findByRole("alert")).toHaveTextContent(
      translate("postParser.catchUp.submissionUnknown"),
    );
    expect(screen.queryByRole("status")).toBeNull();
    expect(confirm(0)).toBeDisabled();
    expect(api.confirm).toHaveBeenCalledOnce();
  });

  it("invalidates the old approval before an in-flight refresh and ignores late results after close", async () => {
    const view = show();
    await ready();
    const pending = deferred<{ code: number; data: CatchUpPreview }>();
    api.prepare.mockReturnValue(pending.promise);
    const button = confirm(2);
    act(() => {
      refresh().click();
      button.click();
    });
    expect(api.confirm).not.toHaveBeenCalled();
    view.unmount();
    await act(async () => pending.resolve({ code: 0, data: preview(baseItems()) }));
    expect(api.changed).not.toHaveBeenCalled();
  });

  it("keeps status filters local to the frozen review without fetching or losing checked results", async () => {
    show();
    await ready();
    await userEvent.setup().click(
      within(screen.getByRole("group", { name: label("statuses") })).getByRole("button", {
        name: "暂不能接入 1",
      }),
    );
    expect(checkbox(4)).toBeVisible();
    expect(screen.queryByRole("checkbox", { name: label("selectTask", { id: 1 }) })).toBeNull();
    expect(confirm(2)).toBeEnabled();
    expect(api.filtered).toHaveBeenCalledOnce();
    expect(api.prepare).not.toHaveBeenCalled();
  });

  it("offers batch catch-up from filtered actions and localizes every label in both languages", async () => {
    const onCatchUp = vi.fn();
    render(
      <HeroUIProvider disableAnimation>
        <FilteredTaskActions
          count={900}
          filtered
          disabled={false}
          onCatchUp={onCatchUp}
          onDelete={vi.fn()}
          onExportInstructions={vi.fn()}
          onExportExcel={vi.fn()}
        />
      </HeroUIProvider>,
    );
    await userEvent.setup().click(
      screen.getByRole("button", {
        name: translate("postParser.actions.filtered", { count: 900 }),
      }),
    );
    await userEvent.setup().click(screen.getByRole("menuitem", { name: label("action") }));
    expect(onCatchUp).toHaveBeenCalledOnce();
    for (const key of Object.keys(cn).filter((key) => key.startsWith("postParser.batchCatchUp."))) {
      expect((en as Record<string, string>)[key], key).toBeTruthy();
    }
  });
});
