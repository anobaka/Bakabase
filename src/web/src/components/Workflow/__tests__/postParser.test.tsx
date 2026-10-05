import React, { useState } from "react";
import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { HeroUIProvider } from "@heroui/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { PostParserManualTriggerUI as trigger } from "../Triggers/PostParserManual";
import {
  PostParserReadContentUI as read,
  PostParserUnlockContentUI as unlock,
} from "../Activities/PostParser";
import { isProcessingPlanPayloadValid } from "../Triggers/FsProcessingPlan";

const openUrl = vi.hoisted(() => vi.fn());

vi.mock("@/sdk/BApi", () => ({ default: { gui: { openUrlInDefaultBrowser: openUrl } } }));

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
  it("takes users to the source and resumes without submitting a purchase selection", async () => {
    const Form = unlock.ResumeForm!;
    const onSubmit = vi.fn();

    await act(async () =>
      root.render(
        <HeroUIProvider>
          <Form
            promptJson={JSON.stringify({
              content: {
                sourceUrl: "https://www.soul-plus.net/read.php?tid=123",
                locks: [{ url: "https://example.test/buy", isBought: false, price: 8 }],
              },
            })}
            submitting={false}
            onSubmit={onSubmit}
          />
        </HeroUIProvider>,
      ),
    );
    const buttons = Array.from(container.querySelectorAll("button"));

    await act(async () =>
      buttons
        .find((button) => button.textContent === "postParser.action.openPostToUnlock")!
        .click(),
    );
    expect(openUrl).toHaveBeenCalledWith({ url: "https://www.soul-plus.net/read.php?tid=123" });
    await act(async () =>
      buttons
        .find((button) => button.textContent === "postParser.action.refreshAfterUnlock")!
        .click(),
    );
    expect(onSubmit).toHaveBeenCalledWith("{}");
    expect(container.querySelector('input[type="checkbox"]')).toBeNull();
  });
  it("requires a known extraction plan for local files unless they were explicitly processed already", () => {
    const value = {
      directory: "/downloads/resource",
      extractionPlanJson: JSON.stringify({ requirement: "unknown", steps: [] }),
    };

    expect(isProcessingPlanPayloadValid(JSON.stringify(value))).toBe(false);
    expect(isProcessingPlanPayloadValid(JSON.stringify({ ...value, alreadyProcessed: true }))).toBe(
      true,
    );
    expect(
      isProcessingPlanPayloadValid(
        JSON.stringify({ ...value, directory: "", alreadyProcessed: true }),
      ),
    ).toBe(false);
    expect(
      isProcessingPlanPayloadValid(
        JSON.stringify({
          ...value,
          extractionPlanJson: JSON.stringify({ requirement: "notRequired", steps: [] }),
        }),
      ),
    ).toBe(true);
  });
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
