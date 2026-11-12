import type { ComponentProps } from "react";
import type {
  ContentReviewPreview,
  ContentReviewResult,
  ContentReviewTarget,
  ContentReviewTask,
} from "../components/ContentPurchaseReviewModal";

import { HeroUIProvider } from "@heroui/react";
import { act, cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ContentPurchaseReviewModal from "../components/ContentPurchaseReviewModal";
import ActualModal from "@/components/bakaui/components/Modal";
import cn from "@/locales/cn/pages/postParser.json";

const handlers = vi.hoisted(() => ({
  refresh: vi.fn(),
  submit: vi.fn(),
  close: vi.fn(),
  inspect: vi.fn(),
}));
vi.mock("@/components/bakaui", async () => ({
  ...(await import("@heroui/react")),
  Button: (await import("@/components/bakaui/components/Button")).Button,
  Modal: (props: ComponentProps<typeof ActualModal>) => <ActualModal {...props} />,
}));
vi.mock("@/components/ExternalLink", () => ({
  default: ({ href, children, ...props }: ComponentProps<"a">) => (
    <a href={href} {...props}>
      {children}
    </a>
  ),
}));
const translate = (key: string, values: Record<string, unknown> = {}) =>
  ((cn as Record<string, string>)[key] ?? key).replace(/\{\{(\w+)\}\}/g, (_, name) =>
    String(values[name] ?? ""),
  );
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: translate }) }));
const label = (key: string, values: Record<string, unknown> = {}) =>
  translate(`postParser.contentPurchaseReview.${key}`, values);

const target = (updates: Partial<ContentReviewTarget> = {}): ContentReviewTarget => ({
  targetId: "quote:first-floor",
  floor: "1",
  blockIds: ["block-a", "block-b"],
  blocks: [
    { blockId: "block-a", priceText: "1", isBought: false, contextText: "download password" },
    { blockId: "block-b", priceText: "1", isBought: false, contextText: "archive password" },
  ],
  price: 1,
  priceText: "1.000",
  currencyCode: "SP",
  currencyName: "SP币",
  canBuy: true,
  canMarkNotRequired: true,
  defaultAction: "buy",
  ...updates,
});
const task = (updates: Partial<ContentReviewTask> = {}): ContentReviewTask => ({
  taskId: 41,
  title: "A resource post",
  link: "https://example.test/post/41",
  revision: 0,
  workflowRunId: 86,
  decisionVersion: 4,
  contentFingerprint: "a".repeat(64),
  quoteHash: "quote-hash-fixed",
  canSubmit: true,
  alreadyUnlocked: false,
  targets: [target()],
  ...updates,
});
const preview = (tasks: ContentReviewTask[] = [task()]): ContentReviewPreview => ({
  scope: { status: "attention", reasonCode: "purchaseRequired" },
  matchedCount: tasks.length,
  autoBuyThreshold: 10,
  minimumRemainingCoins: 1000,
  tasks,
});
const outcome = (updates: Partial<ContentReviewResult> = {}): ContentReviewResult => ({
  requestId: "fixed-request-id",
  requestedCount: 1,
  queuedCount: 1,
  unchangedCount: 0,
  skippedCount: 0,
  failedCount: 0,
  items: [{ taskId: 41, status: "queued" }],
  ...updates,
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
type Props = ComponentProps<typeof ContentPurchaseReviewModal>;
const props = (updates: Partial<Props> = {}): Props => ({
  snapshot: preview(),
  snapshotKey: "snapshot:1",
  onRefresh: handlers.refresh,
  onSubmit: handlers.submit,
  onClose: handlers.close,
  onInspectTask: handlers.inspect,
  ...updates,
});
const show = (initial: Partial<Props> = {}) => {
  let current = props(initial);
  const view = render(
    <HeroUIProvider disableAnimation>
      <ContentPurchaseReviewModal {...current} />
    </HeroUIProvider>,
  );
  return {
    ...view,
    update: (updates: Partial<Props>) => {
      current = { ...current, ...updates };
      view.rerender(
        <HeroUIProvider disableAnimation>
          <ContentPurchaseReviewModal {...current} />
        </HeroUIProvider>,
      );
    },
  };
};
const submitButton = () =>
  screen.getByRole("button", {
    name: (name) => name.startsWith(label("submit", { count: "__COUNT__" }).split("__COUNT__")[0]),
  });
const selection = (record = task()) =>
  screen.getByRole("checkbox", {
    name: label("selectTask", { id: record.taskId, title: record.title }),
  });
const totals = () => screen.getByLabelText(label("selectedTotals"));
const bulkButton = (action: "buy" | "notRequired" | "defer") =>
  within(screen.getByLabelText(label("bulk.label"))).getByRole("button", {
    name: label(`bulk.${action}`),
  });
const draftAction = (
  record: ContentReviewTask,
  floor: string,
  action: "buy" | "notRequired" | "defer",
) =>
  within(
    within(screen.getByRole("region", { name: record.title! })).getByRole("radiogroup", {
      name: label("actionLabel", { target: label("targetFloor", { floor }) }),
    }),
  ).getByRole("radio", { name: label(`action.${action}`) });
const select = async (record = task()) => {
  const user = userEvent.setup();
  const checkbox = await screen.findByRole("checkbox", {
    name: label("selectTask", { id: record.taskId, title: record.title }),
  });
  if (!(checkbox as HTMLInputElement).checked) await user.click(checkbox);
  return user;
};
beforeEach(() => {
  handlers.refresh.mockReset().mockResolvedValue(undefined);
  handlers.submit.mockReset().mockResolvedValue(outcome());
  handlers.close.mockReset();
  handlers.inspect.mockReset();
});
afterEach(cleanup);

describe("unified content purchase review", () => {
  it("preselects eligible draft tasks without submitting, shows the post link and actual quote, and never sums page-block prices", async () => {
    show();
    expect(await screen.findByRole("dialog")).toBeVisible();
    expect(selection()).toBeChecked();
    expect(submitButton()).toBeEnabled();
    expect(screen.getByRole("link", { name: "#41 A resource post" })).toHaveAttribute(
      "href",
      "https://example.test/post/41",
    );
    expect(screen.getByText(label("targetFloor", { floor: "1" }))).toBeVisible();
    expect(
      within(screen.getByRole("region", { name: "A resource post" })).getByText("1.000 SP币"),
    ).toBeVisible();
    expect(totals()).toHaveTextContent("1.000 SP币");
    expect(totals()).not.toHaveTextContent("2 SP币");
    await userEvent.setup().click(selection());
    expect(totals()).toHaveTextContent(label("noPurchaseSelected"));
    expect(submitButton()).toBeDisabled();
    expect(handlers.submit).not.toHaveBeenCalled();
  });

  it("focuses cancellation by default and Enter closes without submitting", async () => {
    show();
    const close = await screen.findByRole("button", { name: label("close") });
    await waitFor(() => expect(close).toHaveFocus());
    await userEvent.setup().keyboard("{Enter}");
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(handlers.close).toHaveBeenCalledOnce();
    expect(handlers.submit).not.toHaveBeenCalled();
  });

  it("submits only selected tasks, with every target action and the exact frozen CAS identity", async () => {
    const first = task({
      targets: [
        target(),
        target({ targetId: "quote:later-floor", floor: "20", defaultAction: "defer" }),
      ],
    });
    const second = task({ taskId: 42, title: "Unselected post" });
    show({ snapshot: preview([first, second]) });
    await select(first);
    await userEvent.setup().click(selection(second));
    await userEvent.setup().click(submitButton());
    expect(handlers.submit).toHaveBeenCalledOnce();
    expect(handlers.submit).toHaveBeenCalledWith({
      tasks: [
        {
          taskId: 41,
          revision: 0,
          workflowRunId: 86,
          decisionVersion: 4,
          contentFingerprint: "a".repeat(64),
          quoteHash: "quote-hash-fixed",
          targets: [
            { targetId: "quote:first-floor", action: "buy" },
            { targetId: "quote:later-floor", action: "defer" },
          ],
        },
      ],
    });
    expect(submitButton()).toBeDisabled();
    expect(await screen.findByText(label("result.queued", { count: 1 }))).toBeVisible();
    expect(screen.getByText(label("result.pendingNotice"))).toBeVisible();
    expect(screen.getByRole("dialog")).toBeVisible();
  });

  it("preselects an over-limit notRequired draft without authorizing work, even with unknown currency", async () => {
    show({
      snapshot: preview([
        task({
          targets: [
            target({
              canBuy: false,
              buyBlockReason: "overPurchaseLimit",
              defaultAction: "notRequired",
              currencyCode: null,
              currencyName: null,
            }),
          ],
        }),
      ]),
    });
    const notRequired = await screen.findByRole("radio", { name: label("action.notRequired") });
    expect(notRequired).toBeChecked();
    expect(selection()).toBeChecked();
    expect(submitButton()).toBeEnabled();
    expect(handlers.submit).not.toHaveBeenCalled();
    expect(screen.getByRole("radio", { name: label("action.buy") })).toBeDisabled();
    expect(screen.getByText(label("status.overPurchaseLimit"))).toBeVisible();
    expect(
      screen.queryByText(translate("postParser.purchaseBatch.skip.overPurchaseLimit")),
    ).toBeNull();
    await select();
    expect(submitButton()).toBeEnabled();
    expect(totals()).toHaveTextContent(label("noPurchaseSelected"));
    await userEvent.setup().click(submitButton());
    expect(handlers.submit.mock.calls[0][0].tasks[0].targets).toEqual([
      { targetId: "quote:first-floor", action: "notRequired" },
    ]);
  });

  it.each([
    { price: null, priceText: null },
    { price: 1, priceText: "unknown" },
    { price: 1, priceText: "-1" },
    { currencyCode: null, currencyName: null },
  ])("defers an invalid or unknown purchase quote without allowing purchase: %j", async (quote) => {
    show({
      snapshot: preview([task({ targets: [target({ ...quote, canMarkNotRequired: false })] })]),
    });
    expect(await screen.findByRole("radio", { name: label("action.buy") })).toBeDisabled();
    expect(screen.getByRole("radio", { name: label("action.notRequired") })).toBeDisabled();
    expect(screen.getByRole("radio", { name: label("action.defer") })).toBeChecked();
    await select();
    expect(submitButton()).toBeDisabled();
    expect(handlers.submit).not.toHaveBeenCalled();
  });

  it("sums only selected buy quotes with exact decimal arithmetic and keeps currencies separate", async () => {
    const first = task({
      targets: [target({ priceText: "9007199254740993.000000001", price: 9007199254740992 })],
    });
    const second = task({
      taskId: 42,
      title: "Second SP task",
      targets: [target({ priceText: "0.000000009" })],
    });
    const third = task({
      taskId: 43,
      title: "Different currency",
      targets: [target({ currencyCode: "TOK", currencyName: "站点积分", priceText: "2.500" })],
    });
    show({ snapshot: preview([first, second, third]) });
    await select(first);
    await select(second);
    await select(third);
    expect(totals()).toHaveTextContent("9007199254740993.000000010 SP币");
    expect(totals()).toHaveTextContent("2.500 站点积分");
    expect(totals()).not.toHaveTextContent("9007199254740995");
    await userEvent.setup().click(selection(second));
    expect(totals()).toHaveTextContent("9007199254740993.000000001 SP币");
    const firstSection = screen.getByRole("region", { name: first.title! });
    await userEvent
      .setup()
      .click(within(firstSection).getByRole("radio", { name: label("action.notRequired") }));
    expect(totals()).not.toHaveTextContent("SP币");
    expect(totals()).toHaveTextContent("2.500 站点积分");
  });

  it("allows an already unlocked post to continue with no paid targets and skips already bought blocks", async () => {
    show({ snapshot: preview([task({ alreadyUnlocked: true, targets: [] })]) });
    expect(await screen.findByText(label("alreadyUnlocked"))).toBeVisible();
    await select();
    expect(submitButton()).toBeEnabled();
    await userEvent.setup().click(submitButton());
    expect(handlers.submit.mock.calls[0][0].tasks[0].targets).toEqual([]);
    cleanup();
    handlers.submit.mockClear();
    show({
      snapshot: preview([
        task({ targets: [target({ blocks: [{ blockId: "bought", isBought: true }] })] }),
      ]),
    });
    expect(await screen.findByText(label("alreadyUnlocked"))).toBeVisible();
    expect(screen.queryByRole("radio", { name: label("action.buy") })).toBeNull();
    await select();
    expect(submitButton()).toBeDisabled();
    expect(handlers.submit).not.toHaveBeenCalled();
  });

  it("does not select blocked tasks or tasks lacking a usable identity", async () => {
    const first = task({ canSubmit: false, blockReason: "operationBusy" });
    const second = task({ taskId: 42, title: "Identity missing", workflowRunId: undefined });
    show({ snapshot: preview([first, second]) });
    expect(
      await screen.findByRole("checkbox", {
        name: label("selectTask", { id: first.taskId, title: first.title }),
      }),
    ).toBeDisabled();
    expect(selection(second)).toBeDisabled();
    expect(submitButton()).toBeDisabled();
  });

  it("blocks duplicate submissions and dismissal until the pending operation settles", async () => {
    const pending = deferred<ContentReviewResult>();
    handlers.submit.mockReturnValue(pending.promise);
    show();
    await select();
    const button = submitButton();
    act(() => {
      button.click();
      button.click();
    });
    expect(handlers.submit).toHaveBeenCalledOnce();
    expect(button).toBeDisabled();
    expect(screen.getByRole("button", { name: label("close") })).toBeDisabled();
    expect(screen.getByRole("button", { name: label("refresh") })).toBeDisabled();
    expect(selection()).toBeDisabled();
    await userEvent.setup().keyboard("{Escape}");
    expect(screen.getByRole("dialog")).toBeVisible();
    await act(async () => pending.resolve(outcome()));
    expect(button).toBeDisabled();
    expect(handlers.close).not.toHaveBeenCalled();
  });

  it("requires a successful new preview before another explicit submit after any failure", async () => {
    handlers.submit.mockRejectedValueOnce(new Error("Network outcome is uncertain"));
    const view = show();
    await select();
    await userEvent.setup().click(submitButton());
    expect(await screen.findByRole("alert")).toHaveTextContent("Network outcome is uncertain");
    expect(submitButton()).toBeDisabled();
    act(() => submitButton().click());
    expect(handlers.submit).toHaveBeenCalledOnce();
    await userEvent.setup().click(screen.getByRole("button", { name: label("refresh") }));
    expect(handlers.refresh).toHaveBeenCalledOnce();
    expect(submitButton()).toBeDisabled();
    view.update({ snapshot: preview(), snapshotKey: "snapshot:2" });
    await waitFor(() => expect(selection()).toBeEnabled());
    expect(selection()).toBeChecked();
    expect(screen.queryByRole("alert")).toBeNull();
    await select();
    expect(submitButton()).toBeEnabled();
  });

  it("refreshes once, freezes old actions on refresh failure and requires a new key even for identical content", async () => {
    const pending = deferred<void>();
    handlers.refresh.mockReturnValue(pending.promise);
    const view = show();
    await select();
    const refresh = screen.getByRole("button", { name: label("refresh") });
    act(() => {
      refresh.click();
      refresh.click();
      submitButton().click();
    });
    expect(handlers.refresh).toHaveBeenCalledOnce();
    expect(handlers.submit).not.toHaveBeenCalled();
    await act(async () => pending.reject(new Error("Cannot refresh quote")));
    expect(await screen.findByRole("alert")).toHaveTextContent("Cannot refresh quote");
    expect(submitButton()).toBeDisabled();
    expect(selection()).toBeDisabled();
    view.update({ snapshot: preview(), snapshotKey: "snapshot:2" });
    await waitFor(() => expect(selection()).toBeEnabled());
    expect(selection()).toBeChecked();
  });

  it("ignores a late result for an older snapshot and preserves the new snapshot's independent draft defaults", async () => {
    const pending = deferred<ContentReviewResult>();
    handlers.submit.mockReturnValue(pending.promise);
    const view = show();
    await select();
    await userEvent.setup().click(submitButton());
    view.update({
      snapshot: preview([
        task({ title: "Fresh reviewed post", revision: 9, quoteHash: "new-quote" }),
      ]),
      snapshotKey: "snapshot:2",
    });
    await act(async () => pending.resolve(outcome({ queuedCount: 99 })));
    expect(screen.queryByRole("region", { name: label("result.title") })).toBeNull();
    expect(
      screen.getByRole("checkbox", {
        name: label("selectTask", { id: 41, title: "Fresh reviewed post" }),
      }),
    ).toBeChecked();
    expect(submitButton()).toBeEnabled();
    expect(handlers.submit).toHaveBeenCalledOnce();
  });

  it("does not surface an old async failure after a new preview or unmount", async () => {
    const pending = deferred<ContentReviewResult>();
    handlers.submit.mockReturnValue(pending.promise);
    const view = show();
    await select();
    await userEvent.setup().click(submitButton());
    view.update({ snapshotKey: "snapshot:2", snapshot: preview() });
    await act(async () => pending.reject(new Error("Old failure")));
    expect(screen.queryByRole("alert")).toBeNull();
    const second = deferred<void>();
    handlers.refresh.mockReturnValue(second.promise);
    await userEvent.setup().click(screen.getByRole("button", { name: label("refresh") }));
    view.unmount();
    await act(async () => second.reject(new Error("Unmounted failure")));
    expect(handlers.close).not.toHaveBeenCalled();
  });

  it("disables old snapshots during external loading or error and exposes read-only inspection through the controller", async () => {
    const view = show();
    await select();
    await userEvent.setup().click(screen.getByRole("button", { name: label("inspectTask") }));
    expect(handlers.inspect).toHaveBeenCalledWith(task());
    view.update({ loading: true });
    expect(selection()).toBeDisabled();
    expect(submitButton()).toBeDisabled();
    expect(screen.getByRole("status")).toHaveTextContent(label("loading"));
    view.update({ loading: false, error: "Preview is no longer valid" });
    expect(screen.getByRole("alert")).toHaveTextContent("Preview is no longer valid");
    expect(selection()).toBeDisabled();
    expect(submitButton()).toBeDisabled();
    expect(handlers.submit).not.toHaveBeenCalled();
  });

  it("reports partial queue outcomes without presenting them as successful purchases", async () => {
    handlers.submit.mockResolvedValue(
      outcome({
        queuedCount: 1,
        failedCount: 1,
        skippedCount: 2,
        unchangedCount: 3,
        items: [
          { taskId: 41, status: "queued" },
          { taskId: 42, status: "failed", reason: "quoteChanged" },
        ],
      }),
    );
    show();
    await select();
    await userEvent.setup().click(submitButton());
    const result = await screen.findByRole("region", { name: label("result.title") });
    expect(result).toHaveTextContent(label("result.queued", { count: 1 }));
    expect(result).toHaveTextContent(label("result.failed", { count: 1 }));
    expect(result).toHaveTextContent(label("result.pendingNotice"));
    expect(result).not.toHaveTextContent("购买成功");
    expect(submitButton()).toBeDisabled();
  });

  it("allows a server-approved free quote without guessing a currency", async () => {
    show({
      snapshot: preview([
        task({
          targets: [
            target({ price: 0, priceText: "0.000", currencyCode: null, currencyName: null }),
          ],
        }),
      ]),
    });
    expect(await screen.findByRole("radio", { name: label("action.buy") })).toBeEnabled();
    expect(selection()).toBeChecked();
    expect(submitButton()).toBeEnabled();
    expect(totals()).toHaveTextContent(label("free"));
    expect(totals()).not.toHaveTextContent("SP币");
    await userEvent.setup().click(submitButton());
    expect(handlers.submit.mock.calls[0][0].tasks[0].targets[0].action).toBe("buy");
  });

  it("counts purchase targets rather than page blocks and supports selecting or clearing eligible tasks", async () => {
    const first = task({
      targets: [
        target(),
        target({ targetId: "skip", defaultAction: "notRequired" }),
        target({ targetId: "later", defaultAction: "defer" }),
      ],
    });
    const blocked = task({ taskId: 42, title: "Blocked post", canSubmit: false });
    show({ snapshot: preview([first, blocked]) });
    expect(
      await screen.findByText(label("selectionActions", { buy: 1, notRequired: 1, defer: 1 })),
    ).toBeVisible();
    expect(selection(blocked)).not.toBeChecked();
    await userEvent.setup().click(screen.getByRole("button", { name: label("clearSelection") }));
    expect(selection(first)).not.toBeChecked();
    expect(submitButton()).toBeDisabled();
    await userEvent.setup().click(screen.getByRole("button", { name: label("selectAll") }));
    expect(selection(first)).toBeChecked();
    expect(selection(blocked)).not.toBeChecked();
    expect(totals()).toHaveTextContent("1.000 SP币");
    expect(handlers.submit).not.toHaveBeenCalled();
  });

  it("bulk buy changes only supported targets in selected posts, counts targets and keeps exact quoted totals", async () => {
    const selected = task({
      targets: [
        target({
          targetId: "precise",
          floor: "1",
          priceText: "9007199254740993.000000001",
          defaultAction: "notRequired",
        }),
        target({
          targetId: "not-buyable",
          floor: "2",
          canBuy: false,
          defaultAction: "notRequired",
        }),
        target({
          targetId: "unknown-currency",
          floor: "3",
          currencyCode: null,
          currencyName: null,
          defaultAction: "notRequired",
        }),
        target({
          targetId: "free",
          floor: "4",
          price: 0,
          priceText: "0.000",
          currencyCode: null,
          currencyName: null,
          defaultAction: "notRequired",
        }),
        target({
          targetId: "unknown-price",
          floor: "5",
          priceText: "unverified",
          defaultAction: "notRequired",
        }),
      ],
    });
    const unselected = task({
      taskId: 42,
      title: "Keep this draft unchanged",
      targets: [target({ floor: "10", defaultAction: "notRequired" })],
    });
    const blocked = task({
      taskId: 43,
      title: "Blocked task",
      canSubmit: false,
      targets: [target({ floor: "20", defaultAction: "notRequired" })],
    });
    show({ snapshot: preview([selected, unselected, blocked]) });
    await userEvent.setup().click(selection(unselected));
    await userEvent.setup().click(bulkButton("buy"));
    expect(await screen.findByText(label("bulk.result", { applied: 2, skipped: 3 }))).toBeVisible();
    expect(draftAction(selected, "1", "buy")).toBeChecked();
    expect(draftAction(selected, "4", "buy")).toBeChecked();
    for (const floor of ["2", "3", "5"])
      expect(draftAction(selected, floor, "notRequired")).toBeChecked();
    expect(draftAction(unselected, "10", "notRequired")).toBeChecked();
    expect(draftAction(blocked, "20", "notRequired")).toBeChecked();
    expect(selection(selected)).toBeChecked();
    expect(selection(unselected)).not.toBeChecked();
    expect(selection(blocked)).not.toBeChecked();
    expect(totals()).toHaveTextContent("9007199254740993.000000001 SP币");
    expect(totals()).toHaveTextContent(label("free"));
    expect(
      screen.getByText(label("selectionActions", { buy: 2, notRequired: 3, defer: 0 })),
    ).toBeVisible();
    expect(handlers.submit).not.toHaveBeenCalled();
    expect(handlers.refresh).not.toHaveBeenCalled();
  });

  it("bulk notRequired respects each target's permission and accepts unknown price or currency without buying", async () => {
    const selected = task({
      targets: [
        target({ targetId: "may-skip", floor: "1" }),
        target({
          targetId: "must-not-skip",
          floor: "2",
          canMarkNotRequired: false,
          priceText: "2.00",
        }),
        target({
          targetId: "unknown-but-confirmable",
          floor: "3",
          price: null,
          priceText: null,
          currencyCode: null,
          currencyName: null,
          defaultAction: "defer",
        }),
      ],
    });
    show({ snapshot: preview([selected]) });
    await userEvent.setup().click(bulkButton("notRequired"));
    expect(await screen.findByText(label("bulk.result", { applied: 2, skipped: 1 }))).toBeVisible();
    expect(draftAction(selected, "1", "notRequired")).toBeChecked();
    expect(draftAction(selected, "2", "buy")).toBeChecked();
    expect(draftAction(selected, "3", "notRequired")).toBeChecked();
    expect(totals()).toHaveTextContent("2.00 SP币");
    expect(handlers.submit).not.toHaveBeenCalled();
    await userEvent.setup().click(submitButton());
    expect(handlers.submit.mock.calls[0][0].tasks[0].targets).toEqual([
      { targetId: "may-skip", action: "notRequired" },
      { targetId: "must-not-skip", action: "buy" },
      { targetId: "unknown-but-confirmable", action: "notRequired" },
    ]);
  });

  it("reports zero eligible bulk purchases without altering drafts and excludes already bought targets from counts", async () => {
    const selected = task({
      targets: [
        target({
          targetId: "not-buyable",
          floor: "1",
          canBuy: false,
          defaultAction: "notRequired",
        }),
        target({ targetId: "unknown", floor: "2", canBuy: false, defaultAction: "defer" }),
        target({
          targetId: "already-bought",
          floor: "3",
          blocks: [{ blockId: "paid", isBought: true }],
        }),
      ],
    });
    show({ snapshot: preview([selected]) });
    expect(bulkButton("buy")).toBeEnabled();
    await userEvent.setup().click(bulkButton("buy"));
    expect(await screen.findByText(label("bulk.result", { applied: 0, skipped: 2 }))).toBeVisible();
    expect(draftAction(selected, "1", "notRequired")).toBeChecked();
    expect(draftAction(selected, "2", "defer")).toBeChecked();
    expect(selection(selected)).toBeChecked();
    expect(totals()).toHaveTextContent(label("noPurchaseSelected"));
    expect(handlers.submit).not.toHaveBeenCalled();
  });

  it("bulk defer keeps task checkboxes unchanged, never touches unselected posts and does not queue work", async () => {
    const selected = task({
      targets: [target(), target({ targetId: "skip", floor: "2", defaultAction: "notRequired" })],
    });
    const unselected = task({
      taskId: 42,
      title: "Unselected purchase",
      targets: [target({ floor: "10" })],
    });
    show({ snapshot: preview([selected, unselected]) });
    await userEvent.setup().click(selection(unselected));
    await userEvent.setup().click(bulkButton("defer"));
    expect(await screen.findByText(label("bulk.result", { applied: 2, skipped: 0 }))).toBeVisible();
    expect(draftAction(selected, "1", "defer")).toBeChecked();
    expect(draftAction(selected, "2", "defer")).toBeChecked();
    expect(draftAction(unselected, "10", "buy")).toBeChecked();
    expect(selection(selected)).toBeChecked();
    expect(selection(unselected)).not.toBeChecked();
    expect(totals()).toHaveTextContent(label("noPurchaseSelected"));
    expect(submitButton()).toBeDisabled();
    expect(handlers.submit).not.toHaveBeenCalled();
  });

  it("disables all bulk actions when no post is selected or the external preview is loading or invalid", async () => {
    const view = show();
    await userEvent.setup().click(screen.getByRole("button", { name: label("clearSelection") }));
    for (const action of ["buy", "notRequired", "defer"] as const)
      expect(bulkButton(action)).toBeDisabled();
    await select();
    for (const action of ["buy", "notRequired", "defer"] as const)
      expect(bulkButton(action)).toBeEnabled();
    await act(async () => view.update({ loading: true }));
    for (const action of ["buy", "notRequired", "defer"] as const)
      expect(bulkButton(action)).toBeDisabled();
    await act(async () => view.update({ loading: false, error: "Preview invalidated" }));
    for (const action of ["buy", "notRequired", "defer"] as const)
      expect(bulkButton(action)).toBeDisabled();
    expect(handlers.submit).not.toHaveBeenCalled();
    expect(handlers.refresh).not.toHaveBeenCalled();
  });

  it("freezes bulk edits during same-frame refresh or submit and after a consumed snapshot until a new preview arrives", async () => {
    const refresh = deferred<void>();
    handlers.refresh.mockReturnValue(refresh.promise);
    const view = show();
    await userEvent.setup().click(bulkButton("notRequired"));
    expect(await screen.findByText(label("bulk.result", { applied: 1, skipped: 0 }))).toBeVisible();
    const buy = bulkButton("buy");
    act(() => {
      screen.getByRole("button", { name: label("refresh") }).click();
      buy.click();
    });
    expect(draftAction(task(), "1", "notRequired")).toBeChecked();
    for (const action of ["buy", "notRequired", "defer"] as const)
      expect(bulkButton(action)).toBeDisabled();
    await act(async () => refresh.reject(new Error("Cannot verify current quotes")));
    for (const action of ["buy", "notRequired", "defer"] as const)
      expect(bulkButton(action)).toBeDisabled();
    view.update({ snapshot: preview(), snapshotKey: "snapshot:2" });
    await waitFor(() => expect(bulkButton("buy")).toBeEnabled());
    expect(screen.queryByText(label("bulk.result", { applied: 1, skipped: 0 }))).toBeNull();
    expect(draftAction(task(), "1", "buy")).toBeChecked();
    const pending = deferred<ContentReviewResult>();
    handlers.submit.mockReturnValue(pending.promise);
    const defer = bulkButton("defer");
    act(() => {
      submitButton().click();
      defer.click();
    });
    expect(draftAction(task(), "1", "buy")).toBeChecked();
    for (const action of ["buy", "notRequired", "defer"] as const)
      expect(bulkButton(action)).toBeDisabled();
    await act(async () => pending.resolve(outcome()));
    for (const action of ["buy", "notRequired", "defer"] as const)
      expect(bulkButton(action)).toBeDisabled();
    expect(handlers.submit).toHaveBeenCalledOnce();
    expect(handlers.refresh).toHaveBeenCalledOnce();
  });

  it("preserves the latest draft for unsupported targets across same-frame bulk buy then notRequired", async () => {
    const selected = task({
      targets: [
        target({
          targetId: "buy-only",
          floor: "1",
          canMarkNotRequired: false,
          defaultAction: "defer",
          priceText: "4.125",
        }),
        target({
          targetId: "not-required-only",
          floor: "2",
          canBuy: false,
          defaultAction: "notRequired",
        }),
      ],
    });
    show({ snapshot: preview([selected]) });
    expect(selection(selected)).toBeChecked();
    expect(draftAction(selected, "1", "defer")).toBeChecked();
    const buy = bulkButton("buy");
    const notRequired = bulkButton("notRequired");
    act(() => {
      buy.click();
      notRequired.click();
    });
    expect(draftAction(selected, "1", "buy")).toBeChecked();
    expect(draftAction(selected, "2", "notRequired")).toBeChecked();
    expect(totals()).toHaveTextContent("4.125 SP币");
    expect(screen.getByText(label("bulk.result", { applied: 1, skipped: 1 }))).toBeVisible();
    expect(handlers.submit).not.toHaveBeenCalled();
    await userEvent.setup().click(submitButton());
    expect(handlers.submit.mock.calls[0][0].tasks[0].targets).toEqual([
      { targetId: "buy-only", action: "buy" },
      { targetId: "not-required-only", action: "notRequired" },
    ]);
  });

  it("uses the cleared selection for same-frame bulk editing and submission instead of reusing old selected posts", async () => {
    const selected = task({ targets: [target({ defaultAction: "notRequired" })] });
    show({ snapshot: preview([selected]) });
    expect(selection(selected)).toBeChecked();
    const clear = screen.getByRole("button", { name: label("clearSelection") });
    const buy = bulkButton("buy");
    const submit = submitButton();
    act(() => {
      clear.click();
      buy.click();
      submit.click();
    });
    expect(selection(selected)).not.toBeChecked();
    expect(draftAction(selected, "1", "notRequired")).toBeChecked();
    expect(bulkButton("buy")).toBeDisabled();
    expect(submitButton()).toBeDisabled();
    expect(totals()).toHaveTextContent(label("noPurchaseSelected"));
    expect(screen.queryByText(label("bulk.result", { applied: 1, skipped: 0 }))).toBeNull();
    expect(handlers.submit).not.toHaveBeenCalled();
    expect(handlers.refresh).not.toHaveBeenCalled();
  });
});
