import type { ComponentProps, ReactNode } from "react";

import { HeroUIProvider } from "@heroui/react";
import { act, cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import PurchaseTasksConfirmationModal from "../components/PurchaseTasksConfirmationModal";
import ActualModal from "@/components/bakaui/components/Modal";
import cn from "@/locales/cn/pages/postParser.json";
import en from "@/locales/en/pages/postParser.json";

const api = vi.hoisted(() => ({ confirm: vi.fn(), close: vi.fn() }));
vi.mock("@/components/bakaui", async () => ({
  ...(await import("@heroui/react")),
  Button: (await import("@/components/bakaui/components/Button")).Button,
  Modal: (props: ComponentProps<typeof ActualModal>) => <ActualModal {...props} />,
}));
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, values: Record<string, unknown> = {}) =>
      ((cn as Record<string, string>)[key] ?? key).replace(/\{\{(\w+)\}\}/g, (_, name) =>
        String(values[name] ?? ""),
      ),
  }),
  Trans: ({ children }: { children: ReactNode }) => children,
}));
const show = (props: Partial<ComponentProps<typeof PurchaseTasksConfirmationModal>> = {}) => {
  render(
    <HeroUIProvider disableAnimation>
      <PurchaseTasksConfirmationModal
        scope={{ keyword: "test scope", status: "attention", reasonCode: "purchaseRequired" }}
        matchedCount={52}
        actionableCount={30}
        purchaseItemCount={45}
        knownTotalCost={43.125}
        currencyCode="SP"
        currencyName="SP币"
        skipReasonCounts={{ priceUnknown: 12, overPurchaseLimit: 10 }}
        autoBuyThreshold={10}
        minimumRemainingCoins={1000}
        onConfirm={api.confirm}
        onClose={api.close}
        {...props}
      />
    </HeroUIProvider>,
  );
  return userEvent.setup();
};
beforeEach(() => {
  api.confirm.mockReset().mockResolvedValue(undefined);
  api.close.mockReset();
});
afterEach(cleanup);

describe("priced purchase selection confirmation", () => {
  it("explains the exact scope, known cost, eligible count, limits and skipped reasons without rounding", async () => {
    show();
    const dialog = await screen.findByRole("dialog");
    expect(dialog).toHaveTextContent("test scope");
    expect(dialog).toHaveTextContent("需要购买解锁");
    expect(dialog).toHaveTextContent("52");
    expect(dialog).toHaveTextContent("30");
    expect(dialog).toHaveTextContent("43.125 SP币");
    expect(dialog).toHaveTextContent("内容价格未知，需要人工核实");
    expect(dialog).toHaveTextContent("内容价格超过当前购买限额");
    expect(dialog).toHaveTextContent("单项价格不超过 10 SP币");
    expect(dialog).toHaveTextContent("购买后余额至少 1000 SP币");
    expect(dialog).toHaveTextContent("已有文件和执行历史会保留");
    const confirm = screen.getByRole("button", {
      name: "购买并重新解析（合计 43.125 SP币，30 个任务）",
    });
    expect(confirm).toHaveClass("h-auto", "min-w-0", "max-w-full", "whitespace-normal");
    expect(confirm.parentElement).toHaveClass("flex-wrap");
    expect(api.confirm).not.toHaveBeenCalled();
  });
  it("focuses cancellation by default and Enter never purchases", async () => {
    const user = show();
    const cancel = await screen.findByRole("button", {
      name: cn["postParser.purchaseBatch.cancel"],
    });
    await waitFor(() => expect(cancel).toHaveFocus());
    await user.keyboard("{Enter}");
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(api.confirm).not.toHaveBeenCalled();
    expect(api.close).toHaveBeenCalledOnce();
  });
  it("allows Escape to cancel and has no actionable confirmation when all quotes were skipped", async () => {
    const user = show({
      actionableCount: 0,
      knownTotalCost: 0,
      skipReasonCounts: { priceUnknown: 52 },
    });
    await screen.findByRole("dialog");
    expect(
      screen.getByRole("button", { name: "购买并重新解析（合计 0 SP币，0 个任务）" }),
    ).toBeDisabled();
    expect(screen.getByRole("status")).toHaveTextContent(cn["postParser.purchaseBatch.none"]);
    await user.keyboard("{Escape}");
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(api.confirm).not.toHaveBeenCalled();
  });
  it("submits only once, locks dismissal while pending and preserves the dialog on failure", async () => {
    let fail!: (failure: Error) => void;
    api.confirm.mockImplementation(
      () =>
        new Promise<void>((_, reject) => {
          fail = reject;
        }),
    );
    const user = show();
    const confirm = await screen.findByRole("button", {
      name: "购买并重新解析（合计 43.125 SP币，30 个任务）",
    });
    act(() => {
      confirm.click();
      confirm.click();
    });
    expect(api.confirm).toHaveBeenCalledOnce();
    expect(confirm).toBeDisabled();
    expect(
      screen.getByRole("button", { name: cn["postParser.purchaseBatch.cancel"] }),
    ).toBeDisabled();
    await user.keyboard("{Escape}");
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    await act(async () => fail(new Error("Quote changed; preview again")));
    expect(screen.getByRole("alert")).toHaveTextContent("Quote changed; preview again");
    expect(confirm).toBeEnabled();
  });
  it.each([-1, Number.NaN, Number.POSITIVE_INFINITY])(
    "rejects an invalid known upper cost %s",
    async (knownTotalCost) => {
      show({ knownTotalCost });
      const dialog = await screen.findByRole("dialog");
      const confirm =
        dialog.querySelector<HTMLButtonElement>('button[data-color="warning"]') ??
        screen
          .getAllByRole("button")
          .find((button) => button.textContent?.startsWith("购买并重新解析（合计"));
      expect(confirm).toBeDisabled();
      expect(api.confirm).not.toHaveBeenCalled();
    },
  );
  it("uses exact quote text and source currency instead of rounding or assuming coins", async () => {
    show({
      knownTotalCost: 9007199254740992,
      knownTotalCostText: "9007199254740993.000000001",
      currencyCode: "TOK",
      currencyName: "站点积分",
    });
    expect(await screen.findByRole("dialog")).toHaveTextContent(
      "9007199254740993.000000001 站点积分",
    );
    expect(
      screen.getByRole("button", {
        name: "购买并重新解析（合计 9007199254740993.000000001 站点积分，30 个任务）",
      }),
    ).toBeEnabled();
    expect(screen.getByRole("dialog")).not.toHaveTextContent("SP币");
  });
  it("keeps different source currency totals separate and does not approve an unknown currency", async () => {
    const user = show({
      knownTotalCost: 0,
      currencyCode: null,
      currencyName: null,
      currencyTotals: [
        { currencyCode: "A", currencyName: "甲站积分", total: 2, totalText: "2.000" },
        { currencyCode: "B", currencyName: "乙站点数", total: 3, totalText: "3" },
      ],
    });
    const dialog = await screen.findByRole("dialog");
    expect(dialog).toHaveTextContent("2.000 甲站积分 · 3 乙站点数");
    expect(dialog).not.toHaveTextContent("5 甲站积分");
    expect(dialog).toHaveTextContent(cn["postParser.purchaseBatch.multipleCurrencyLimits"]);
    await user.keyboard("{Escape}");
    cleanup();
    show({ currencyName: null, currencyCode: null });
    expect(
      await screen.findByRole("button", { name: /购买并重新解析（合计 43.125 来源币种未知/ }),
    ).toBeDisabled();
    expect(api.confirm).not.toHaveBeenCalled();
  });
  it("provides non-key Chinese and English labels for every backend skip reason and attention category", () => {
    const codes = [
      "missingOrDeleted",
      "snapshotChanged",
      "scopeChanged",
      "notWaitingForPurchase",
      "sourceUnsupported",
      "noPurchaseQuote",
      "noEligibleItems",
      "priceUnknown",
      "overPurchaseLimit",
      "balanceUnknown",
      "insufficientBalance",
      "quoteChanged",
      "operationBusy",
      "queueFailed",
    ];
    for (const locale of [cn, en])
      for (const code of codes)
        expect(
          (locale as Record<string, string>)[`postParser.purchaseBatch.skip.${code}`],
        ).toBeTruthy();
    expect(cn["postParser.filters.reasonHint"]).toContain("重叠");
    expect(en["postParser.filters.reasonHint"]).toContain("overlap");
  });
});
