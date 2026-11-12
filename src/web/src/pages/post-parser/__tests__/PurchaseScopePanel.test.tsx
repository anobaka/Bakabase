import type { ComponentProps } from "react";
import type { PurchasePreview } from "../components/PurchaseScopePanel";

import { HeroUIProvider } from "@heroui/react";
import { act, cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import PurchaseScopePanel from "../components/PurchaseScopePanel";
import cn from "@/locales/cn/pages/postParser.json";

const api = vi.hoisted(() => ({ preview: vi.fn(), confirm: vi.fn(), review: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({
  default: {
    postParser: {
      previewPostParserTaskPurchases: api.preview,
      confirmPostParserTaskPurchases: api.confirm,
    },
  },
}));
vi.mock("@/components/bakaui", async () => ({
  ...(await import("@heroui/react")),
  Button: (await import("@/components/bakaui/components/Button")).Button,
}));
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, values: Record<string, unknown> = {}) =>
      ((cn as Record<string, string>)[key] ?? key).replace(/\{\{(\w+)\}\}/g, (_, name) =>
        String(values[name] ?? ""),
      ),
  }),
}));

type PanelProps = ComponentProps<typeof PurchaseScopePanel>;
type PreviewResponse = { code: number; data?: PurchasePreview };
const scope: PanelProps["scope"] = {
  keyword: "exact selected scope",
  status: "attention",
  reasonCode: "purchaseRequired",
};
const preview = (updates: Partial<PurchasePreview> = {}): PurchasePreview => ({
  scope,
  matchedCount: 52,
  actionableCount: 2,
  knownTotalCost: 6.125,
  knownTotalCostText: String(updates.knownTotalCost ?? 6.125),
  currencyCode: "SP",
  currencyName: "SP币",
  currencyTotals: [],
  autoBuyThreshold: 10,
  minimumRemainingCoins: 1000,
  skipReasonCounts: { priceUnknown: 49, insufficientBalance: 1 },
  tasks: [
    {
      taskId: 3,
      revision: 7,
      workflowRunId: 13,
      lockUrls: ["https://example.test/post3/lock1", "https://example.test/post3/lock2"],
      maxTotalCost: 2.125,
      ...{ maxTotalCostText: "2.125", currencyCode: "SP", currencyName: "SP币" },
      quoteHash: "fixed-quote-three",
    },
    {
      taskId: 4,
      revision: 8,
      workflowRunId: 14,
      lockUrls: ["https://example.test/post4/lock1"],
      maxTotalCost: 4,
      ...{ maxTotalCostText: "4", currencyCode: "SP", currencyName: "SP币" },
      quoteHash: "fixed-quote-four",
    },
  ],
  skipped: [],
  ...updates,
});
const response = (data = preview()): PreviewResponse => ({ code: 0, data });
const deferred = () => {
  let resolve!: (value: PreviewResponse) => void;
  let reject!: (error: Error) => void;
  const promise = new Promise<PreviewResponse>((done, fail) => {
    resolve = done;
    reject = fail;
  });
  return { promise, resolve, reject };
};
const panel = (props: Partial<PanelProps> = {}) => (
  <HeroUIProvider disableAnimation>
    <PurchaseScopePanel scope={scope} refreshVersion="initial" onReview={api.review} {...props} />
  </HeroUIProvider>
);
const show = (props: Partial<PanelProps> = {}) => render(panel(props));
const reviewButton = () =>
  screen.getByRole("button", {
    name: (name) => name.startsWith(cn["postParser.purchaseBatch.action"]),
  });
const summaryValue = (kind: "matched" | "actionable" | "items" | "cost") => {
  const label = screen.getByText(
    (cn as Record<string, string>)[`postParser.purchaseScope.${kind}`] ??
      `postParser.purchaseScope.${kind}`,
    { selector: "dt" },
  );
  return label.nextElementSibling;
};
const refreshButton = () =>
  screen.getByRole("button", {
    name:
      (cn as Record<string, string>)["postParser.purchaseScope.refresh"] ??
      "postParser.purchaseScope.refresh",
  });

beforeEach(() => {
  api.preview.mockReset().mockResolvedValue(response());
  api.confirm.mockReset();
  api.review.mockReset();
});
afterEach(cleanup);

describe("purchase summary for the selected attention reason", () => {
  it("does not preview all reasons or a scope outside the attention group", () => {
    const view = show({ scope: { status: "attention" } });
    expect(view.container.querySelector("section")).toBeNull();
    expect(api.preview).not.toHaveBeenCalled();
    view.rerender(panel({ scope: { status: "all", reasonCode: "purchaseRequired" } }));
    expect(view.container.querySelector("section")).toBeNull();
    expect(api.preview).not.toHaveBeenCalled();
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it("keeps counts and price unknown until a bounded preview has loaded", async () => {
    const pending = deferred();
    api.preview.mockReturnValue(pending.promise);
    const view = show();
    expect(reviewButton()).toBeDisabled();
    expect(api.preview).toHaveBeenCalledExactlyOnceWith(
      scope,
      expect.objectContaining({ sensitiveResponse: true, signal: expect.any(AbortSignal) }),
    );
    expect(view.container.querySelector("dl")).toBeNull();
    expect(screen.queryByText("0 SP币")).toBeNull();
    expect(api.review).not.toHaveBeenCalled();
    await act(async () => pending.resolve(response()));
    expect(reviewButton()).toBeEnabled();
  });

  it("waits for an accepted page query and isolates old quotes throughout keyword changes", async () => {
    const first = deferred();
    const obsolete = deferred();
    const nextScope = { ...scope, keyword: "next accepted keyword" };
    const accepted = preview({ scope: nextScope, knownTotalCost: 9.375 });
    api.preview
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(obsolete.promise)
      .mockResolvedValueOnce(response(accepted));
    const view = show({ queryReady: false });
    expect(api.preview).not.toHaveBeenCalled();
    expect(reviewButton()).toBeDisabled();
    expect(view.container.querySelector("dl")).toBeNull();

    view.rerender(panel({ queryReady: true }));
    expect(api.preview).toHaveBeenCalledExactlyOnceWith(
      scope,
      expect.objectContaining({ sensitiveResponse: true, signal: expect.any(AbortSignal) }),
    );
    await act(async () => first.resolve(response()));
    expect(reviewButton()).toBeEnabled();
    view.rerender(panel({ queryReady: false }));
    expect(api.preview).toHaveBeenCalledOnce();
    expect(reviewButton()).toBeDisabled();
    await userEvent.setup().click(reviewButton());
    expect(api.review).not.toHaveBeenCalled();

    view.rerender(panel({ queryReady: true }));
    expect(api.preview).toHaveBeenCalledTimes(2);
    const obsoleteSignal = api.preview.mock.calls[1][1].signal as AbortSignal;
    view.rerender(panel({ scope: { ...scope, keyword: "next" }, queryReady: false }));
    view.rerender(panel({ scope: nextScope, queryReady: false }));
    expect(obsoleteSignal.aborted).toBe(true);
    expect(api.preview).toHaveBeenCalledTimes(2);
    await act(async () => obsolete.resolve(response(preview({ knownTotalCost: 999 }))));
    expect(reviewButton()).toBeDisabled();
    expect(view.container.querySelector("dl")).toBeNull();
    expect(screen.queryByText("999 SP币")).toBeNull();

    view.rerender(panel({ scope: nextScope, queryReady: true }));
    expect(api.preview).toHaveBeenLastCalledWith(
      nextScope,
      expect.objectContaining({ sensitiveResponse: true, signal: expect.any(AbortSignal) }),
    );
    await waitFor(() => expect(reviewButton()).toBeEnabled());
    expect(api.preview).toHaveBeenCalledTimes(3);
    await userEvent.setup().click(reviewButton());
    expect(api.review).toHaveBeenCalledExactlyOnceWith(accepted);
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it("summarizes task counts separately from payable unlock items and preserves price precision", async () => {
    show();
    await waitFor(() => expect(reviewButton()).toBeEnabled());
    expect(summaryValue("matched")).toHaveTextContent(/^52$/);
    expect(summaryValue("actionable")).toHaveTextContent(/^2$/);
    expect(summaryValue("items")).toHaveTextContent(/^3$/);
    expect(summaryValue("cost")).toHaveTextContent(/^6\.125 SP币$/);
    expect(screen.getByText(/内容价格未知，需要人工核实：49/)).toBeVisible();
    expect(screen.getByText(/购买后的余额不满足保留额度：1/)).toBeVisible();
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it("presents each currency's exact quoted total without combining currencies into one amount", async () => {
    const accepted = preview({
      knownTotalCost: 999,
      knownTotalCostText: "999",
      currencyCode: undefined,
      currencyName: undefined,
      currencyTotals: [
        {
          currencyCode: "SP",
          currencyName: "SP币",
          total: 1.25,
          totalText: "1.25000000000000000001",
        },
        {
          currencyCode: "GP",
          currencyName: "GP",
          total: 2.5,
          totalText: "2.50000000000000000002",
        },
      ],
    });
    api.preview.mockResolvedValue(response(accepted));
    show();
    await waitFor(() => expect(reviewButton()).toBeEnabled());
    expect(summaryValue("cost")).toHaveTextContent(
      /^1\.25000000000000000001 SP币 · 2\.50000000000000000002 GP$/,
    );
    expect(reviewButton()).toHaveTextContent("1.25000000000000000001 SP币");
    expect(reviewButton()).toHaveTextContent("2.50000000000000000002 GP");
    expect(screen.queryByText(/3\.75|999/)).toBeNull();
    await userEvent.setup().click(reviewButton());
    expect(api.review).toHaveBeenCalledExactlyOnceWith(accepted);
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it("disables review when the quote currency is unknown rather than assuming SP", async () => {
    api.preview.mockResolvedValue(
      response(preview({ currencyCode: undefined, currencyName: undefined, currencyTotals: [] })),
    );
    show();
    expect(await screen.findByRole("alert")).toHaveTextContent(
      cn["postParser.purchaseBatch.currencyUnknown"],
    );
    expect(summaryValue("cost")).toHaveTextContent(/^6\.125 来源币种未知$/);
    expect(reviewButton()).toBeDisabled();
    expect(screen.queryByText(/SP币|金币/)).toBeNull();
    await userEvent.setup().click(reviewButton());
    expect(api.review).not.toHaveBeenCalled();
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it("reviews the original accepted manifest without invoking purchase confirmation", async () => {
    const accepted = preview();
    api.preview.mockResolvedValue(response(accepted));
    show();
    await waitFor(() => expect(reviewButton()).toBeEnabled());
    const user = userEvent.setup();
    await user.click(reviewButton());
    expect(api.review).toHaveBeenCalledExactlyOnceWith(accepted);
    expect(api.review.mock.calls[0][0]).toBe(accepted);
    expect(api.preview).toHaveBeenCalledOnce();
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it("disables an accepted manifest while its external context is unavailable", async () => {
    const view = show();
    await waitFor(() => expect(reviewButton()).toBeEnabled());
    view.rerender(panel({ disabled: true }));
    expect(reviewButton()).toBeDisabled();
    await userEvent.setup().click(reviewButton());
    expect(api.review).not.toHaveBeenCalled();
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it("does not allow reviewing a previous manifest during a refresh", async () => {
    const refreshed = deferred();
    const old = preview();
    const replacement = preview({ knownTotalCost: 8.875 });
    api.preview.mockResolvedValueOnce(response(old)).mockReturnValueOnce(refreshed.promise);
    const view = show();
    await waitFor(() => expect(reviewButton()).toBeEnabled());
    view.rerender(panel({ refreshVersion: "changed" }));
    expect(reviewButton()).toBeDisabled();
    await userEvent.setup().click(reviewButton());
    expect(api.review).not.toHaveBeenCalled();
    expect(api.preview).toHaveBeenCalledTimes(2);
    await act(async () => refreshed.resolve(response(replacement)));
    expect(reviewButton()).toBeEnabled();
    await userEvent.setup().click(reviewButton());
    expect(api.review).toHaveBeenCalledExactlyOnceWith(replacement);
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it("uses the same exact scope when manually refreshing and holds review until acceptance", async () => {
    const refreshed = deferred();
    api.preview.mockResolvedValueOnce(response()).mockReturnValueOnce(refreshed.promise);
    show();
    await waitFor(() => expect(reviewButton()).toBeEnabled());
    await userEvent.setup().click(refreshButton());
    expect(reviewButton()).toBeDisabled();
    expect(refreshButton()).toBeDisabled();
    expect(api.preview).toHaveBeenLastCalledWith(
      scope,
      expect.objectContaining({ sensitiveResponse: true, signal: expect.any(AbortSignal) }),
    );
    expect(screen.getByRole("status")).toBeVisible();
    await act(async () => refreshed.resolve(response()));
    expect(reviewButton()).toBeEnabled();
    expect(refreshButton()).toBeEnabled();
  });

  it("isolates a late success from the previous filter scope", async () => {
    const old = deferred();
    const newScope = { ...scope, keyword: "new filter" };
    const replacement = preview({ scope: newScope, knownTotalCost: 9.375 });
    api.preview.mockReturnValueOnce(old.promise).mockResolvedValueOnce(response(replacement));
    const view = show();
    const oldSignal = api.preview.mock.calls[0][1].signal as AbortSignal;
    view.rerender(panel({ scope: newScope }));
    expect(oldSignal.aborted).toBe(true);
    await waitFor(() => expect(reviewButton()).toBeEnabled());
    await act(async () => old.resolve(response(preview({ knownTotalCost: 999 }))));
    await userEvent.setup().click(reviewButton());
    expect(api.review).toHaveBeenCalledExactlyOnceWith(replacement);
    expect(screen.queryByText("999 SP币")).toBeNull();
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it("ignores a previous scope failure after the current scope is accepted", async () => {
    const old = deferred();
    const newScope = { ...scope, reasonCode: "availabilityUnknown" };
    const replacement = preview({ scope: newScope });
    api.preview.mockReturnValueOnce(old.promise).mockResolvedValueOnce(response(replacement));
    const view = show();
    view.rerender(panel({ scope: newScope }));
    await waitFor(() => expect(reviewButton()).toBeEnabled());
    await act(async () => old.reject(new Error("obsolete failure")));
    expect(screen.queryByRole("alert")).toBeNull();
    expect(reviewButton()).toBeEnabled();
    await userEvent.setup().click(reviewButton());
    expect(api.review).toHaveBeenCalledExactlyOnceWith(replacement);
  });

  it("does not accept an old scope success while the new scope is still loading", async () => {
    const old = deferred();
    const current = deferred();
    const newScope = { ...scope, keyword: "next scope still loading" };
    api.preview.mockReturnValueOnce(old.promise).mockReturnValueOnce(current.promise);
    const view = show();
    view.rerender(panel({ scope: newScope }));
    await act(async () => old.resolve(response()));
    expect(reviewButton()).toBeDisabled();
    expect(view.container.querySelector("dl")).toBeNull();
    expect(screen.getByRole("status")).toBeVisible();
    const accepted = preview({ scope: newScope });
    await act(async () => current.resolve(response(accepted)));
    await userEvent.setup().click(reviewButton());
    expect(api.review).toHaveBeenCalledExactlyOnceWith(accepted);
  });

  it.each(["rejected request", "unsuccessful response"] as const)(
    "shows a recoverable error after an %s without turning it into an empty quote",
    async (failure) => {
      const retry = deferred();
      if (failure === "rejected request")
        api.preview.mockRejectedValueOnce(new Error("request failed"));
      else api.preview.mockResolvedValueOnce({ code: 1 });
      api.preview.mockReturnValueOnce(retry.promise);
      const view = show();
      await screen.findByRole("alert");
      expect(reviewButton()).toBeDisabled();
      expect(view.container.querySelector("dl")).toBeNull();
      await userEvent.setup().click(refreshButton());
      expect(screen.queryByRole("alert")).toBeNull();
      expect(reviewButton()).toBeDisabled();
      await act(async () => retry.resolve(response()));
      expect(reviewButton()).toBeEnabled();
      expect(api.preview).toHaveBeenCalledTimes(2);
      expect(api.review).not.toHaveBeenCalled();
      expect(api.confirm).not.toHaveBeenCalled();
    },
  );

  it("retains the accepted summary but cannot review it after its refresh failed", async () => {
    api.preview
      .mockResolvedValueOnce(response())
      .mockRejectedValueOnce(new Error("refresh unavailable"));
    const view = show();
    await waitFor(() => expect(reviewButton()).toBeEnabled());
    view.rerender(panel({ refreshVersion: "refresh failed" }));
    await screen.findByRole("alert");
    expect(summaryValue("cost")).toHaveTextContent(/^6\.125 SP币$/);
    expect(reviewButton()).toBeDisabled();
    await userEvent.setup().click(reviewButton());
    expect(api.review).not.toHaveBeenCalled();
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it("shows a completed empty quote and its skip reasons without allowing review", async () => {
    api.preview.mockResolvedValue(
      response(
        preview({
          actionableCount: 0,
          knownTotalCost: 0,
          tasks: [],
          skipReasonCounts: { priceUnknown: 52 },
        }),
      ),
    );
    show();
    await waitFor(() => expect(summaryValue("matched")).toHaveTextContent(/^52$/));
    expect(summaryValue("actionable")).toHaveTextContent(/^0$/);
    expect(summaryValue("items")).toHaveTextContent(/^0$/);
    expect(summaryValue("cost")).toHaveTextContent(/^0 SP币$/);
    expect(reviewButton()).toBeDisabled();
    expect(screen.getByRole("status")).toHaveTextContent(cn["postParser.purchaseBatch.none"]);
    expect(api.review).not.toHaveBeenCalled();
  });

  it("keeps a zero-cost quote reviewable when it still has eligible unlock items", async () => {
    const accepted = preview({
      knownTotalCost: 0,
      tasks: preview().tasks.map((task) => ({ ...task, maxTotalCost: 0, maxTotalCostText: "0" })),
    });
    api.preview.mockResolvedValue(response(accepted));
    show();
    await waitFor(() => expect(reviewButton()).toBeEnabled());
    expect(summaryValue("cost")).toHaveTextContent(/^0 SP币$/);
    await userEvent.setup().click(reviewButton());
    expect(api.review).toHaveBeenCalledExactlyOnceWith(accepted);
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it.each<[string, () => PurchasePreview]>([
    ["another reason", () => preview({ scope: { ...scope, reasonCode: "priceUnknown" } })],
    ["a mismatched task count", () => preview({ actionableCount: 1 })],
    ["duplicate tasks", () => preview({ tasks: [preview().tasks[0], preview().tasks[0]] })],
    [
      "a missing unlock URL",
      () => preview({ tasks: preview().tasks.map((task) => ({ ...task, lockUrls: [" "] })) }),
    ],
    ["a non-finite price", () => preview({ knownTotalCost: Number.POSITIVE_INFINITY })],
  ])("rejects a quote containing %s", async (_reason, invalid) => {
    api.preview.mockResolvedValue(response(invalid()));
    const view = show();
    await screen.findByRole("alert");
    expect(reviewButton()).toBeDisabled();
    expect(view.container.querySelector("dl")).toBeNull();
    expect(api.review).not.toHaveBeenCalled();
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it("can display submission outcomes independently from the current quote", async () => {
    show({ submission: { queuedCount: 2, skippedCount: 3, failedCount: 1 } });
    await waitFor(() => expect(reviewButton()).toBeEnabled());
    expect(screen.getByRole("status")).toHaveTextContent(
      "已入队 2 个任务，3 个因状态或报价变化跳过，1 个未能提交。",
    );
    expect(screen.getByRole("status")).toHaveTextContent("入队不代表已完成购买或解析");
    expect(api.review).not.toHaveBeenCalled();
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it.each(["success", "failure"] as const)(
    "aborts on unmount and ignores an outstanding %s",
    async (result) => {
      const pending = deferred();
      api.preview.mockReturnValue(pending.promise);
      const view = show();
      const signal = api.preview.mock.calls[0][1].signal as AbortSignal;
      view.unmount();
      expect(signal.aborted).toBe(true);
      await act(async () => {
        if (result === "success") pending.resolve(response());
        else pending.reject(new Error("unmounted failure"));
      });
      expect(api.preview).toHaveBeenCalledOnce();
      expect(api.review).not.toHaveBeenCalled();
      expect(api.confirm).not.toHaveBeenCalled();
    },
  );
});
