import type { BTask } from "@/core/models/BTask";

import { afterEach, beforeEach, describe, expect, it } from "vitest";

import { useBTasksStore } from "../bTasks";

import i18n from "@/i18n";
import { localizeBTask } from "@/core/bTaskLocalization";
import { BTaskResourceType, BTaskStatus, BTaskType } from "@/sdk/constants";

const task = (id = "index"): BTask => ({
  id,
  name: "Legacy name",
  process: "Legacy progress",
  createdAt: "2026-10-09T00:00:00",
  status: BTaskStatus.Completed,
  type: BTaskType.Any,
  resourceType: BTaskResourceType.Resource,
  isPersistent: true,
  localizedTexts: {
    en: { name: "Index resources", process: "Completed: 38743 resources indexed" },
    cn: { name: "索引资源", process: "已完成：已索引 38743 个资源" },
  },
});

beforeEach(async () => {
  useBTasksStore.getState().setTasks([]);
  await i18n.changeLanguage("en");
});
afterEach(async () => {
  useBTasksStore.getState().setTasks([]);
  await i18n.changeLanguage("en");
});

describe("browser-local task text", () => {
  it.each(["cn", "zh-CN", "zh_Hans"])("selects Chinese for %s", (language) => {
    expect(localizeBTask(task(), language).name).toBe("索引资源");
  });
  it("retranslates existing completed tasks without another server push", async () => {
    const source = task();

    useBTasksStore.getState().setTasks([source]);
    await i18n.changeLanguage("cn");
    expect(useBTasksStore.getState().tasks[0].process).toBe("已完成：已索引 38743 个资源");
    await i18n.changeLanguage("en");
    expect(useBTasksStore.getState().tasks[0].process).toBe("Completed: 38743 resources indexed");
    expect(source.process).toBe("Legacy progress");
  });
  it("retains legacy text for old servers and literal file or third-party errors", () => {
    const source = {
      ...task(),
      localizedTexts: undefined,
      error: "C:/files/error.log: unavailable",
    };

    expect(localizeBTask(source, "cn")).toBe(source);
    expect(localizeBTask({ ...source, localizedTexts: task().localizedTexts }, "cn").error).toBe(
      source.error,
    );
  });
  it("preserves original fallback fields across partial translations and clears explicit nulls", async () => {
    const source = {
      ...task(),
      localizedTexts: {
        en: { name: "Index resources", process: null },
        cn: { name: "索引资源" },
      },
    };

    useBTasksStore.getState().setTasks([source]);
    expect(useBTasksStore.getState().tasks[0].process).toBeUndefined();
    await i18n.changeLanguage("cn");
    expect(useBTasksStore.getState().tasks[0].process).toBe("Legacy progress");
  });
  it("keeps original fallbacks when a file row reprojects a cached store task", () => {
    const source = {
      ...task(),
      briefError: "Original diagnostic",
      localizedTexts: {
        en: { briefError: null },
        cn: { name: "索引资源" },
      },
    };
    const english = localizeBTask(source, "en");

    expect(english.briefError).toBeUndefined();
    expect(localizeBTask(english, "cn").briefError).toBe("Original diagnostic");
    expect(localizeBTask(localizeBTask(english, "cn"), "en").briefError).toBeUndefined();
  });
  it("localizes updates and new tasks while preserving unaffected task references", async () => {
    await i18n.changeLanguage("cn");
    useBTasksStore.getState().setTasks([task(), task("other")]);
    const other = useBTasksStore.getState().tasks[1];

    useBTasksStore.getState().updateTask({ ...task(), percentage: 42 });
    expect(useBTasksStore.getState().tasks[0]).toMatchObject({ name: "索引资源", percentage: 42 });
    expect(useBTasksStore.getState().tasks[1]).toBe(other);
    useBTasksStore.getState().updateTask(task("new"));
    expect(useBTasksStore.getState().tasks.find((item) => item.id === "new")?.name).toBe(
      "索引资源",
    );
    useBTasksStore.getState().removeTask("index");
    await i18n.changeLanguage("en");
    expect(useBTasksStore.getState().tasks.map((item) => item.id)).toEqual(["other", "new"]);
  });
});
