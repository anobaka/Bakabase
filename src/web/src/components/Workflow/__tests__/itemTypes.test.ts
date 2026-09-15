import { createInstance } from "i18next";
import { describe, expect, it } from "vitest";

import { workflowItemTypeDisplayName } from "../itemTypes";
import { acquisitionStatusLabel, collectionMembershipOriginLabel } from "../valueLabels";

import cnCommon from "@/locales/cn/common.json";
import cn from "@/locales/cn/pages/workflow.json";
import cnStatus from "@/locales/cn/status.json";
import enCommon from "@/locales/en/common.json";
import en from "@/locales/en/pages/workflow.json";
import enStatus from "@/locales/en/status.json";
import { acquisitionStatuses, collectionMembershipOrigins } from "@/sdk/constants";

const translator = async (lng: "cn" | "en") => {
  const i18n = createInstance();

  await i18n.init({
    lng,
    resources: {
      cn: { translation: { ...cnCommon, ...cnStatus, ...cn } },
      en: { translation: { ...enCommon, ...enStatus, ...en } },
    },
    // Match the app configuration that previously suppressed the descriptor fallback.
    parseMissingKeyHandler: (key) => key,
  });

  return i18n.t.bind(i18n);
};

describe("workflow item type labels", () => {
  it.each([
    ["cn", "获取任务"],
    ["en", "Acquisition task"],
  ] as const)("uses the %s translation ahead of the server label", async (locale, expected) => {
    const t = await translator(locale);

    expect(workflowItemTypeDisplayName(t, "item.acquisition", "Acquisition")).toBe(expected);
  });

  it("keeps unknown server types readable even when the missing-key handler returns the key", async () => {
    const t = await translator("cn");

    expect(workflowItemTypeDisplayName(t, "item.custom", "Custom item")).toBe("Custom item");
    expect(workflowItemTypeDisplayName(t, "item.custom")).toBe("item.custom");
    expect(
      workflowItemTypeDisplayName(t, "item.custom", "workflow.itemType.item.custom.displayName"),
    ).toBe("item.custom");
  });
});

describe("acquisition and collection labels", () => {
  it.each([
    ["cn", ["待处理", "运行中", "等待中", "已完成", "失败", "已取消"], ["手动", "订阅"]],
    [
      "en",
      ["Pending", "Running", "Waiting", "Completed", "Failed", "Cancelled"],
      ["Manual", "Subscription"],
    ],
  ] as const)(
    "translates every status and membership origin in %s",
    async (locale, statuses, origins) => {
      const t = await translator(locale);

      expect(acquisitionStatuses.map(({ value }) => acquisitionStatusLabel(t, value))).toEqual(
        statuses,
      );
      expect(
        collectionMembershipOrigins.map(({ value }) => collectionMembershipOriginLabel(t, value)),
      ).toEqual(origins);
    },
  );
});
