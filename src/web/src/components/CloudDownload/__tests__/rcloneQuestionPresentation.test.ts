import { createInstance } from "i18next";
import { beforeEach, expect, it } from "vitest";
import cn from "@/locales/cn/pages/cloudDownload.json";
import en from "@/locales/en/pages/cloudDownload.json";
import { presentRcloneAnswerOptions } from "../rcloneQuestionPresentation";

const i18n = createInstance();
beforeEach(async () => {
  await i18n.init({
    lng: "cn",
    resources: { cn: { translation: cn }, en: { translation: en } },
    parseMissingKeyHandler: (key) => key,
    interpolation: { escapeValue: false },
  });
});

it("explains every official connection choice without changing values or ordering", async () => {
  const values = ["onedrive", "sharepoint", "url", "search", "driveid", "siteid", "path"];
  const question = {
    name: "config_type",
    isSecret: false,
    examples: values.map((value) => ({ value, help: `Native ${value}` })),
  };
  const options = presentRcloneAnswerOptions(question, i18n.t);
  expect(options.map(({ value }) => value)).toEqual(values);
  expect(options[0].label).toBe("常用 · 我的 OneDrive（个人 / 工作或学校）");
  expect(options[2].description).toContain("不是文件分享链接");
  expect(options[4].label).toBe("高级 · 按磁盘 ID 连接");
  expect(options[6].description).toContain("不是本地文件夹路径");
  expect(options.every((option) => option.description && !option.label.includes("rclone."))).toBe(
    true,
  );
  await i18n.changeLanguage("en");
  expect(presentRcloneAnswerOptions(question, i18n.t)[0].label).toBe(
    "Common · My OneDrive (personal / work or school)",
  );
});

it("uses readable drive names and types, replacing only opaque display names with stable numbered labels", () => {
  const examples = [
    { value: "fixture-root-1", help: "b!abcdefghijklmnopqrstuvwxyz0123456789 (business)" },
    { value: "fixture-root-2", help: "12345678-1234-1234-1234-123456789012 (documentLibrary)" },
    { value: "fixture-root-3", help: "ABCDEF0123456789 (personal)" },
    { value: "fixture-root-4", help: "OneDrive (personal)" },
    { value: "fixture-root-5", help: "项目资料 (Q4) (business)" },
    { value: "fixture-root-6", help: "Shared documents (documentLibrary)" },
  ];
  const options = presentRcloneAnswerOptions(
    { name: "config_driveid", isSecret: false, examples },
    i18n.t,
  );
  expect(options.map(({ label }) => label)).toEqual([
    "工作或学校网盘 1",
    "SharePoint 文档库 2",
    "个人网盘 3",
    "OneDrive · 个人网盘",
    "项目资料 (Q4) · 工作或学校网盘",
    "Shared documents · SharePoint 文档库",
  ]);
  expect(options.map(({ value }) => value)).toEqual(examples.map(({ value }) => value));
  expect(options.map(({ originalHelp }) => originalHelp)).toEqual(examples.map(({ help }) => help));
  expect(options[0].description).toContain("不代表它已通过访问检查");
});

it("keeps identically named drives distinguishable and preserves unknown native choices", () => {
  const options = presentRcloneAnswerOptions(
    {
      name: "config_driveid",
      isSecret: false,
      examples: [
        { value: "drive-a", help: "Documents (personal)" },
        { value: "drive-b", help: "Documents (personal)" },
        { value: "drive-c", help: "Unrecognized future format" },
      ],
    },
    i18n.t,
  );
  expect(options[0].label).toBe("Documents · 个人网盘 (1)");
  expect(options[1].label).toBe("Documents · 个人网盘 (2)");
  expect(options[2].label).toBe("Unrecognized future format");
  expect(
    presentRcloneAnswerOptions(
      {
        name: "config_type",
        isSecret: false,
        examples: [{ value: "future-method", help: "Future native option" }],
      },
      i18n.t,
    ),
  ).toEqual([
    {
      value: "future-method",
      label: "Future native option",
      originalHelp: "Future native option",
    },
  ]);
});

it("localizes confirmation values while leaving their native values intact and never presents secret examples", () => {
  const question = {
    name: "config_drive_ok",
    isSecret: false,
    examples: [
      { value: "true", help: "Yes" },
      { value: "false", help: "No" },
    ],
  };
  expect(
    presentRcloneAnswerOptions(question, i18n.t).map(({ label, value }) => ({ label, value })),
  ).toEqual([
    { label: "使用此磁盘", value: "true" },
    { label: "返回重新选择", value: "false" },
  ]);
  expect(presentRcloneAnswerOptions({ ...question, isSecret: true }, i18n.t)).toEqual([]);
});

it("deduplicates only exact drive IDs, preferring a friendly name and retaining same-name different IDs", () => {
  const options = presentRcloneAnswerOptions(
    {
      name: "config_driveid_fixed",
      isSecret: false,
      examples: [
        { value: "b!opaqueValue", help: "b!abcdefghijklmnopqrstuvwxyz0123456789 (business)" },
        { value: "b!opaqueValue", help: "My documents (business)" },
        { value: "other-id", help: "My documents (business)" },
        { value: "OTHER-ID", help: "System library (documentLibrary)" },
      ],
    },
    i18n.t,
  );
  expect(options.map((option) => option.value)).toEqual(["b!opaqueValue", "other-id", "OTHER-ID"]);
  expect(options[0].label).toContain("My documents");
  expect(options[0].originalHelp).toBe("My documents (business)");
  expect(options[1].label).toContain("My documents");
  expect(options[2].label).toContain("System library");
});
