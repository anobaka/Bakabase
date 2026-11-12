import type { ComponentProps } from "react";
import type { PostContentSnapshot } from "@/core/models/PostParserTask";

import { HeroUIProvider } from "@heroui/react";
import { cleanup, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import LockedContentDetails, {
  formatSourceAmount,
  sourceCurrencyLabel,
} from "../components/LockedContentDetails";
import cn from "@/locales/cn/pages/postParser.json";
import { PostParserSource } from "@/sdk/constants";

vi.mock("@/components/bakaui", async () => ({ ...(await import("@heroui/react")) }));
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, values: Record<string, unknown> = {}) =>
      ((cn as Record<string, string>)[key] ?? key).replace(/\{\{(\w+)\}\}/g, (_, name) =>
        String(values[name] ?? ""),
      ),
  }),
}));

const label = (key: string) =>
  (cn as Record<string, string>)[`postParser.lockedContent.${key}`] ??
  `postParser.lockedContent.${key}`;
const content = (updates: Partial<PostContentSnapshot> = {}): PostContentSnapshot => ({
  currencyCode: "SP",
  currencyName: "SP币",
  locks: [
    {
      blockId: "main:block-1",
      id: "legacy-id",
      url: "https://example.test/purchase/shared",
      floor: "1",
      priceText: "12.5000",
      price: 12.5,
      contextText: "此内容仅为支持作者的额外内容",
      isBought: false,
    },
  ],
  readiness: {
    basis: "analysis",
    fingerprint: "saved-content-fingerprint",
    policyVersion: "test-policy",
    decisions: [
      {
        blockId: "main:block-1",
        relevance: "optional",
        purpose: "donation",
        evidence: ["正文写明主资源链接已在免费区域提供"],
        reason: "这一块是额外支持，不用于获得主资源。",
      },
    ],
  },
  ...updates,
});
const show = (props: Partial<ComponentProps<typeof LockedContentDetails>> = {}) =>
  render(
    <HeroUIProvider disableAnimation>
      <LockedContentDetails content={content()} {...props} />
    </HeroUIProvider>,
  );
afterEach(cleanup);

describe("source amount and currency presentation helpers", () => {
  it("retains decimal strings rather than coercing them into JS number precision", () => {
    const exact = "90071992547409931234567890.12345678901234567890";
    expect(formatSourceAmount(exact, 9)).toBe(exact);
    expect(formatSourceAmount("00012.5000", 12.5)).toBe("00012.5000");
    expect(formatSourceAmount("", 12.125)).toBe("12.125");
    expect(formatSourceAmount(0)).toBe("0");
    expect(formatSourceAmount(Number.NaN, undefined, "价格未知")).toBe("价格未知");
    expect(formatSourceAmount(Number.POSITIVE_INFINITY)).toBe("—");
  });

  it("uses source names or codes and restricts legacy SP currency fallback to SoulPlus", () => {
    expect(sourceCurrencyLabel("来源积分", "SRC", PostParserSource.SoulPlus)).toBe("来源积分");
    expect(sourceCurrencyLabel(undefined, "GP", 0)).toBe("GP");
    expect(sourceCurrencyLabel(null, null, PostParserSource.SoulPlus)).toBe("SP币");
    expect(sourceCurrencyLabel(undefined, undefined, 0, "币种未知")).toBe("币种未知");
    expect(sourceCurrencyLabel(undefined, undefined, undefined, "币种未知")).toBe("币种未知");
  });
});

describe("read-only locked page block evidence", () => {
  it("shows the real source currency, original price, block identity, context and purpose evidence", () => {
    show();
    const region = screen.getByRole("region", { name: label("title") });
    expect(region).toHaveTextContent("12.5000 SP币");
    expect(region).not.toHaveTextContent("金币");
    expect(region).toHaveTextContent("main:block-1");
    expect(region).toHaveTextContent("此内容仅为支持作者的额外内容");
    expect(region).toHaveTextContent(label("relevance.optional"));
    expect(region).toHaveTextContent(label("purpose.donation"));
    expect(region).toHaveTextContent("正文写明主资源链接已在免费区域提供");
    expect(region).toHaveTextContent("这一块是额外支持，不用于获得主资源。");
    expect(region).toHaveTextContent(label("basis.analysis"));
    expect(screen.queryByRole("button")).toBeNull();
  });

  it.each([
    {
      reason: "No supported evidence resolves this restricted block.",
      translated: "尚无充分依据判断此受限内容块是否为获取所需。",
    },
    {
      reason: "The source-specific evidence does not establish that this block is optional.",
      translated: "来源专属证据尚不能证明此内容块可不解锁。",
    },
  ])("renders the known policy reason in Chinese: $reason", ({ reason, translated }) => {
    const readiness = content().readiness!;
    show({
      content: content({
        readiness: {
          ...readiness,
          decisions: [{ ...readiness.decisions[0], reason }],
        },
      }),
    });
    expect(screen.getByText(translated)).toBeVisible();
    expect(screen.queryByText(reason)).toBeNull();
  });

  it("preserves large original decimal prices in the rendered details", () => {
    const exact = "90071992547409931234567890.12345678901234567890";
    show({
      content: content({
        locks: [{ ...content().locks[0], priceText: exact, price: 9007199254740992 }],
      }),
    });
    expect(screen.getByText(`${exact} SP币`)).toBeVisible();
    expect(screen.queryByText("9007199254740992 SP币")).toBeNull();
  });

  it("uses legacy numeric prices and SP names only for an identified SoulPlus snapshot", () => {
    show({
      source: PostParserSource.SoulPlus,
      content: content({
        currencyCode: null,
        currencyName: null,
        locks: [{ ...content().locks[0], priceText: null, price: 7.125 }],
      }),
    });
    expect(screen.getByText("7.125 SP币")).toBeVisible();
  });

  it.each([0, undefined] as const)(
    "does not invent a currency for an unidentified source %s",
    (source) => {
      show({
        source,
        content: content({ currencyCode: null, currencyName: null }),
      });
      expect(screen.getByText(`12.5000 ${label("currencyUnknown")}`)).toBeVisible();
      expect(screen.queryByText(/SP币|金币/)).toBeNull();
    },
  );

  it("honors per-block currency metadata before a snapshot currency", () => {
    show({
      source: PostParserSource.SoulPlus,
      content: content({
        locks: [
          { ...content().locks[0], currencyName: "来源积分", currencyCode: "SRC" },
          { ...content().locks[0], blockId: "other", currencyName: null, currencyCode: "GP" },
        ],
      }),
    });
    expect(screen.getByText("12.5000 来源积分")).toBeVisible();
    expect(screen.getByText("12.5000 GP")).toBeVisible();
    expect(screen.queryByText("12.5000 SP币")).toBeNull();
  });

  it("distinguishes page block prices from the payable quote without declaring a valid server quote unresolved", () => {
    show({
      content: content({
        locks: [
          { ...content().locks[0], priceText: "10", price: 10 },
          { ...content().locks[0], blockId: "second:block", priceText: "25", price: 25 },
        ],
      }),
    });
    expect(screen.getByRole("alert")).toHaveTextContent(label("conflictingPrices"));
    expect(screen.getByRole("alert")).toHaveTextContent(
      "实际应付金额以服务器购买预检和确认时的报价为准",
    );
    expect(screen.getByRole("alert")).not.toHaveTextContent("购买报价待核实");
    expect(screen.getByText("10 SP币")).toBeVisible();
    expect(screen.getByText("25 SP币")).toBeVisible();
    expect(screen.queryByText(/35/)).toBeNull();
    expect(screen.getByText("main:block-1")).toBeVisible();
    expect(screen.getByText("second:block")).toBeVisible();
  });

  it("compares equivalent decimal text without falsely treating formatting as a price conflict", () => {
    show({
      content: content({
        locks: [
          { ...content().locks[0], priceText: "0001.00" },
          { ...content().locks[0], blockId: "second:block", priceText: "1.0" },
        ],
      }),
    });
    expect(screen.queryByRole("alert")).toBeNull();
    expect(screen.getByText("0001.00 SP币")).toBeVisible();
    expect(screen.getByText("1.0 SP币")).toBeVisible();
  });

  it("detects conflicting fractional prices beyond JS number precision", () => {
    show({
      content: content({
        locks: [
          { ...content().locks[0], priceText: "900719925474099312345.0000000000000001" },
          {
            ...content().locks[0],
            blockId: "second:block",
            priceText: "900719925474099312345.0000000000000002",
          },
        ],
      }),
    });
    expect(screen.getByRole("alert")).toHaveTextContent(label("conflictingPrices"));
    expect(screen.getByText("900719925474099312345.0000000000000001 SP币")).toBeVisible();
    expect(screen.getByText("900719925474099312345.0000000000000002 SP币")).toBeVisible();
  });

  it("attaches assessments by block ID rather than a shared purchase URL", () => {
    show({
      content: content({
        locks: [
          content().locks[0],
          { ...content().locks[0], blockId: "no-decision", contextText: "另一个内容块" },
          { ...content().locks[0], blockId: null, id: "main:block-1", contextText: "旧格式内容块" },
        ],
      }),
    });
    const assessed = screen.getByText("此内容仅为支持作者的额外内容").closest("li")!;
    expect(within(assessed).getByText(label("relevance.optional"))).toBeVisible();
    for (const context of ["另一个内容块", "旧格式内容块"]) {
      const unknown = screen.getByText(context).closest("li")!;
      expect(within(unknown).getByText(label("relevance.unknown"))).toBeVisible();
      expect(within(unknown).getByText(label("unassessed"))).toBeVisible();
      expect(within(unknown).queryByText(label("relevance.optional"))).toBeNull();
    }
  });

  it("keeps optional content distinct from already purchased or unlocked content", () => {
    show({
      content: content({
        locks: [
          content().locks[0],
          {
            ...content().locks[0],
            blockId: "already-bought",
            isBought: true,
            contextText: "已购买正文",
          },
        ],
      }),
    });
    const optional = screen.getByText("此内容仅为支持作者的额外内容").closest("li")!;
    expect(optional).toHaveTextContent(label("relevance.optional"));
    expect(optional).not.toHaveTextContent(/已解锁|已付费|已购买/);
    expect(screen.queryByText("已购买正文")).toBeNull();
    expect(screen.queryByRole("button")).toBeNull();
  });

  it("treats ambiguous duplicate decisions as unassessed instead of choosing a convenient purpose", () => {
    const readiness = content().readiness!;
    show({
      content: content({
        readiness: {
          ...readiness,
          decisions: [
            readiness.decisions[0],
            { ...readiness.decisions[0], relevance: "required", purpose: "downloadCredentials" },
          ],
        },
      }),
    });
    expect(screen.getByText(label("relevance.unknown"))).toBeVisible();
    expect(screen.getByText(label("unassessed"))).toBeVisible();
    expect(screen.queryByText("正文写明主资源链接已在免费区域提供")).toBeNull();
  });

  it("shows at most three compact entries and opens complete evidence using the shared accordion", async () => {
    const original = content();
    show({
      compact: true,
      content: content({
        locks: Array.from({ length: 4 }, (_, index) => ({
          ...original.locks[0],
          blockId: index === 0 ? "main:block-1" : `block-${index}`,
          contextText: `content-${index}`,
          priceText: index === 0 ? "12.5000" : "25",
        })),
      }),
    });
    expect(screen.getByText("content-0")).toBeVisible();
    expect(screen.getByText("content-2")).toBeVisible();
    expect(screen.queryByText("content-3")).toBeNull();
    expect(screen.queryByText("正文写明主资源链接已在免费区域提供")).toBeNull();
    expect(screen.queryByText("main:block-1")).toBeNull();
    expect(screen.queryByText(label("floor"))).toBeNull();
    expect(screen.getByRole("alert")).toHaveTextContent(label("conflictingPrices"));
    const expand = screen.getByRole("button", {
      name: label("details").replace("{{count}}", "4"),
    });
    await userEvent.setup().click(expand);
    expect(screen.getByText("content-3")).toBeVisible();
    expect(screen.getByText("正文写明主资源链接已在免费区域提供")).toBeVisible();
    expect(screen.getByText("main:block-1")).toBeVisible();
    expect(screen.getAllByRole("button")).toHaveLength(1);
  });

  it("reports missing price and context without inventing assessments", () => {
    show({
      content: {
        locks: [{ blockId: "unknown-block", price: null, priceText: null, isBought: false }],
      },
    });
    expect(screen.getByText(`${label("priceUnknown")} ${label("currencyUnknown")}`)).toBeVisible();
    expect(screen.getByText(label("contextUnknown"))).toBeVisible();
    expect(screen.getByText(label("relevance.unknown"))).toBeVisible();
    expect(screen.getByText(label("unassessed"))).toBeVisible();
  });

  it("renders source context as text rather than executing pasted HTML", () => {
    const literal = '<img src="x" onerror="alert(1)"> source evidence';
    const view = show({
      content: content({ locks: [{ ...content().locks[0], contextText: literal }] }),
    });
    expect(screen.getByText(literal)).toBeVisible();
    expect(view.container.querySelector("img")).toBeNull();
    expect(view.container.querySelector("script")).toBeNull();
  });

  it("renders an empty state when there are no unpaid blocks", () => {
    show({ content: content({ locks: [{ ...content().locks[0], isBought: true }] }) });
    expect(screen.getByText(label("none"))).toBeVisible();
    expect(screen.queryByText("12.5000 SP币")).toBeNull();
    expect(screen.queryByRole("button")).toBeNull();
  });
});
