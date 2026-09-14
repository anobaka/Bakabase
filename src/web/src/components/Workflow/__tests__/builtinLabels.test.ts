import { createInstance } from "i18next";
import { describe, expect, it } from "vitest";

import { workflowLabel } from "../builtinLabels";

import cn from "@/locales/cn/pages/acquisition.json";
import en from "@/locales/en/pages/acquisition.json";
import cnWorkflow from "@/locales/cn/pages/workflow.json";
import enWorkflow from "@/locales/en/pages/workflow.json";

const instance = createInstance();

await instance.init({
  lng: "cn",
  fallbackLng: "en",
  keySeparator: false,
  resources: {
    cn: { translation: { ...cn, ...cnWorkflow } },
    en: { translation: { ...en, ...enWorkflow } },
  },
});

const builtinNames = [
  ["Forum post + cloud drive", "分享内容与网盘"],
  ["Direct download", "直链下载并入库"],
  ["Magnet", "外部下载后入库（磁力链接）"],
  ["Magnet download", "磁力链接下载并入库"],
  ["Torrent download", "BT 种子下载并入库"],
  ["Platform fetch", "平台获取或关联已有文件"],
  ["Local directory", "本地目录入库"],
  ["Download torrent contents", "下载种子内容"],
  ["ExHentai download", "ExHentai 下载并入库"],
  ["Parse post download information", "仅解析帖子"],
];

describe("built-in workflow display names", () => {
  it.each(builtinNames)(
    "localizes the seeded %s workflow without changing stored data",
    (name, translated) => {
      const workflow = Object.freeze({ name, isBuiltin: true });

      expect(workflowLabel(workflow, instance.getFixedT("cn"))).toBe(translated);
      expect(workflow.name).toBe(name);
    },
  );

  it.each(builtinNames)("preserves a user-created workflow named %s", (name) => {
    expect(workflowLabel({ name, isBuiltin: false }, instance.getFixedT("cn"))).toBe(name);
    expect(workflowLabel({ name }, instance.getFixedT("cn"))).toBe(name);
  });

  it("preserves renamed and unknown built-ins and follows language changes", () => {
    const t = instance.getFixedT("cn");

    expect(workflowLabel({ name: "My direct download", isBuiltin: true }, t)).toBe(
      "My direct download",
    );
    expect(workflowLabel({ name: "direct download", isBuiltin: true }, t)).toBe("direct download");
    expect(workflowLabel({ name: "Future built-in", isBuiltin: true }, t)).toBe("Future built-in");
    expect(workflowLabel({ name: "toString", isBuiltin: true }, t)).toBe("toString");
    expect(
      workflowLabel({ name: "Direct download", isBuiltin: true }, instance.getFixedT("en")),
    ).toBe("Direct download and import");
  });
});
