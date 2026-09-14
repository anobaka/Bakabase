import React, { useState } from "react";
import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { HeroUIProvider } from "@heroui/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { PostParserManualTriggerUI as trigger } from "../Triggers/PostParserManual";
import { PostParserReadContentUI as read } from "../Activities/PostParser";

vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/components/bakaui", async () => ({ ...(await import("@heroui/react")) }));

const Form = trigger.ManualRunForm!;
const Harness = () => {
  const [value, onChange] = useState(trigger.defaultManualPayload!());
  return (
    <>
      <Form value={value} onChange={onChange} />
      <output>{value}</output>
    </>
  );
};
let container: HTMLDivElement;
let root: Root;
beforeEach(async () => {
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
  await act(async () =>
    root.render(
      <HeroUIProvider>
        <Harness />
      </HeroUIProvider>,
    ),
  );
});
afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});
async function fill(selector: string, value: string) {
  const input = container.querySelector(selector) as HTMLInputElement | HTMLTextAreaElement;
  const proto =
    input.tagName === "TEXTAREA" ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
  await act(async () => {
    Object.getOwnPropertyDescriptor(proto, "value")!.set!.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}
const payload = () => JSON.parse(container.querySelector("output")!.textContent!);

describe("post parser workflow input", () => {
  it("accepts a post link without a resource or saved task", async () => {
    await fill("input", "https://example.org/post/1");
    expect(payload()).toEqual({ link: "https://example.org/post/1" });
    expect(trigger.isManualPayloadValid!(JSON.stringify(payload()))).toBe(true);
  });
  it("switches to pasted text without retaining the previous link", async () => {
    await fill("input", "https://example.org/post/1");
    const tab = Array.from(container.querySelectorAll<HTMLButtonElement>('[role="tab"]')).find(
      (t) => t.textContent === "workflow.postParser.text",
    )!;
    await act(async () => tab.click());
    await fill("textarea", "Download: https://example.org/archive.zip");
    expect(payload().link).toBeUndefined();
    expect(payload().text).toContain("archive.zip");
    expect(trigger.isManualPayloadValid!(JSON.stringify(payload()))).toBe(true);
  });
  it("rejects missing, ambiguous and non-web input", () => {
    for (const value of [
      {},
      { link: "javascript:alert(1)" },
      { text: " " },
      { link: "https://example.org", text: "a post" },
    ]) {
      expect(trigger.isManualPayloadValid!(JSON.stringify(value))).toBe(false);
    }
    expect(trigger.isManualPayloadValid!("null")).toBe(false);
    expect(read.defaultConfig().useConfiguredSoulPlusPurchaseLimit).toBe(false);
  });
});
