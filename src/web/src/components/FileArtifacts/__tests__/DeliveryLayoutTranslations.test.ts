import { expect, it } from "vitest";
import en from "@/locales/en/pages/cloudDownload.json";
import cn from "@/locales/cn/pages/cloudDownload.json";

it("ships matching Chinese and English delivery layout labels and interpolation tokens", () => {
  const entries = (value: Record<string, string>) =>
    Object.fromEntries(
      Object.entries(value).filter(([key]) => key.startsWith("artifact.deliveryLayout.")),
    );
  const english = entries(en);
  const chinese = entries(cn);
  expect(Object.keys(chinese).sort()).toEqual(Object.keys(english).sort());
  expect(Object.keys(english).length).toBeGreaterThan(20);
  for (const [key, value] of Object.entries(english)) {
    expect(chinese[key].trim().length).toBeGreaterThan(0);
    expect(chinese[key].match(/\{\{\w+\}\}/g) ?? []).toEqual(value.match(/\{\{\w+\}\}/g) ?? []);
  }
});
