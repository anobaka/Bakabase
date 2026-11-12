import { describe, expect, it, vi } from "vitest";
import type { TFunction } from "i18next";
import { attentionReasonLabel, purchaseSkipReasonLabel } from "../attentionReasons";
import { translateKnownMessage } from "../knownMessages";
import cn from "@/locales/cn/pages/postParser.json";

describe("server reason presentation", () => {
  const t = ((key: string, values: Record<string, string> = {}) =>
    ((cn as Record<string, string>)[key] ?? key).replace(
      /\{\{(\w+)\}\}/g,
      (_, name) => values[name] ?? "",
    )) as TFunction;
  it("translates projected categories and falls back for future server codes", () => {
    expect(attentionReasonLabel(t, "priceUnknown")).toBe("购买价格未知");
    expect(attentionReasonLabel(t, "newReason")).toContain("newReason");
    expect(purchaseSkipReasonLabel(t, "newSkip")).toContain("newSkip");
  });
  it("translates insufficient evidence consistently and preserves site text verbatim", () => {
    expect(translateKnownMessage(t, "POST_AVAILABILITY_EVIDENCE_INSUFFICIENT")).toContain(
      "证据不足",
    );
    expect(
      translateKnownMessage(
        t,
        "Some content remains unpurchased; it was confirmed unnecessary for this acquisition scope.",
      ),
    ).toContain("非本次获取所需");
    expect(translateKnownMessage(t, "No supported evidence resolves this restricted block.")).toBe(
      "尚无充分依据判断此受限内容块是否为获取所需。",
    );
    expect(
      translateKnownMessage(
        t,
        "The source-specific evidence does not establish that this block is optional.",
      ),
    ).toBe("来源专属证据尚不能证明此内容块可不解锁。");
    expect(translateKnownMessage(t, "The site requires login")).toBe("The site requires login");
    const translate = vi.fn();
    translateKnownMessage(translate as unknown as TFunction, "POST_OTHER_UNKNOWN");
    expect(translate).not.toHaveBeenCalled();
  });
});
