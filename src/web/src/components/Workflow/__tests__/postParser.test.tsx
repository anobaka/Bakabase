import React, { useState } from "react";
import type { PostContentSnapshot } from "@/core/models/PostParserTask";
import type { ContentReadinessView } from "@/pages/post-parser/contentReadiness";
import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { HeroUIProvider } from "@heroui/react";
import { screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { PostParserManualTriggerUI as trigger } from "../Triggers/PostParserManual";
import {
  PostParserReadContentUI as read,
  PostParserUnlockContentUI as unlock,
} from "../Activities/PostParser";
import { isProcessingPlanPayloadValid } from "../Triggers/FsProcessingPlan";

const openUrl = vi.hoisted(() => vi.fn());
const reviewPortal = vi.hoisted(() => vi.fn());
const readinessApi = vi.hoisted(() => ({ get: vi.fn() }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: reviewPortal }),
}));

vi.mock("@/sdk/BApi", () => ({
  default: {
    gui: { openUrlInDefaultBrowser: openUrl },
    postParser: { getPostParserContentReadiness: readinessApi.get },
  },
}));

function translate(key: string) {
  return key;
}
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: translate }) }));
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
const savedContent = (): PostContentSnapshot => ({
  sourceUrl: "https://www.soul-plus.net/read.php?tid=14&snapshot=old",
  currencyCode: "SP",
  currencyName: "SP币",
  locks: [
    {
      blockId: "saved-block",
      url: "https://example.test/buy",
      price: 8,
      isBought: false,
      contextText: "saved waiting block",
    },
  ],
});
const unlockedContent = (): PostContentSnapshot => ({
  sourceUrl: "https://www.soul-plus.net/read.php?tid=14&snapshot=current",
  currencyCode: "SP",
  currencyName: "SP币",
  locks: [
    { blockId: "current-block", price: 8, isBought: true, contextText: "currently readable block" },
  ],
});
const currentView = (
  content = unlockedContent(),
  updates: Partial<ContentReadinessView> = {},
): ContentReadinessView => ({
  taskId: 14,
  revision: 4,
  workflowRunId: 100,
  contentSnapshot: content,
  source: "SoulPlus",
  contentFingerprint: "current-content-fingerprint",
  decisionVersion: 2,
  status: "sufficient",
  canConfirm: false,
  canRevoke: false,
  canContinue: false,
  hasStaleDecision: false,
  history: [],
  ...updates,
});
const response = (data = currentView()) => ({ code: 0, data });
const deferred = () => {
  let resolve!: (value: ReturnType<typeof response>) => void;
  let reject!: (error: Error) => void;
  const promise = new Promise<ReturnType<typeof response>>((done, fail) => {
    resolve = done;
    reject = fail;
  });
  return { promise, resolve, reject };
};
const prompt = (updates: Record<string, unknown> = {}) => ({
  taskId: 14,
  revision: 4,
  workflowRunId: 100,
  source: "SoulPlus",
  content: savedContent(),
  ...updates,
});
const renderUnlock = async (value: Record<string, unknown>, onSubmit = vi.fn()) => {
  const ResumeForm = unlock.ResumeForm!;
  await act(async () =>
    root.render(
      <HeroUIProvider disableAnimation>
        <ResumeForm promptJson={JSON.stringify(value)} submitting={false} onSubmit={onSubmit} />
      </HeroUIProvider>,
    ),
  );
  return onSubmit;
};
const resumeButton = () =>
  within(container).getByRole("button", {
    name: (name) =>
      name === "postParser.action.continueParsing" ||
      name === "postParser.action.refreshAfterUnlock",
  });
const button = (name: string) => within(container).getByRole("button", { name });

beforeEach(async () => {
  readinessApi.get.mockReset().mockResolvedValue(response());
  reviewPortal.mockReset();
  openUrl.mockReset();
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
    expect(container).toHaveTextContent("postParser.review.resumeUnlockHint");

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
    expect(readinessApi.get).not.toHaveBeenCalled();
    expect(container.querySelector('input[type="checkbox"]')).toBeNull();
  });
  it("shares accurate page prices and opens persisted review without treating optional blocks as purchased", async () => {
    const Form = unlock.ResumeForm!;
    const onSubmit = vi.fn();
    const content: PostContentSnapshot = {
      currencyCode: "SP",
      currencyName: "SP币",
      sourceUrl: "https://example.com/post",
      locks: [
        {
          blockId: "proof",
          isBought: false,
          priceText: "9007199254740993.125",
          contextText: "Purchase proof",
          url: "https://example.com/buy",
        },
      ],
      readiness: {
        basis: "analysis",
        fingerprint: "current",
        policyVersion: "v1",
        decisions: [
          {
            blockId: "proof",
            relevance: "optional",
            purpose: "purchaseProof",
            evidence: ["Proof only"],
          },
        ],
      },
    };
    readinessApi.get.mockResolvedValue(response(currentView(content)));
    await act(async () =>
      root.render(
        <HeroUIProvider>
          <Form
            promptJson={JSON.stringify({ taskId: 14, revision: 4, workflowRunId: 100, content })}
            submitting={false}
            onSubmit={onSubmit}
          />
        </HeroUIProvider>,
      ),
    );
    expect(container).toHaveTextContent("9007199254740993.125 SP币");
    expect(container).toHaveTextContent("postParser.lockedContent.relevance.optional");
    expect(container).not.toHaveTextContent("postParser.review.resumeUnlockHint");
    await act(async () =>
      Array.from(container.querySelectorAll("button"))
        .find((button) => button.textContent === "postParser.contentPurchaseReview.title")!
        .click(),
    );
    expect(reviewPortal.mock.lastCall?.[1]).toMatchObject({
      scope: { status: "all", taskIds: [14] },
      expectedIdentity: { taskId: 14, revision: 4, workflowRunId: 100 },
    });
    expect(onSubmit).not.toHaveBeenCalled();
    expect(content.locks[0].isBought).toBe(false);
    await act(async () => reviewPortal.mock.lastCall![1].onChanged({ queuedCount: 1 }));
    expect(container).toHaveTextContent("postParser.contentReadiness.continuationQueued");
    expect(
      Array.from(container.querySelectorAll("button")).find(
        (button) => button.textContent === "postParser.action.continueParsing",
      ),
    ).toBeDisabled();
    expect(onSubmit).not.toHaveBeenCalled();
    expect(readinessApi.get).toHaveBeenCalledOnce();
  });
  it.each([0, 4])(
    "uses a fresh unlocked snapshot for revision %s without requiring another purchase",
    async (revision) => {
      const pending = deferred();
      readinessApi.get.mockReturnValue(pending.promise);
      const onSubmit = await renderUnlock(prompt({ revision }));
      expect(readinessApi.get).toHaveBeenCalledExactlyOnceWith(
        14,
        expect.objectContaining({ sensitiveResponse: true }),
      );
      expect(resumeButton()).toBeDisabled();
      await act(async () =>
        pending.resolve(response(currentView(unlockedContent(), { revision }))),
      );
      expect(resumeButton()).toBeEnabled();
      expect(resumeButton()).toHaveTextContent("postParser.action.continueParsing");
      expect(container).not.toHaveTextContent("postParser.review.resumeUnlockHint");
      expect(
        within(container).queryByRole("button", { name: "postParser.action.openPostToUnlock" }),
      ).toBeNull();
      await act(async () => button("postParser.action.openPost").click());
      expect(openUrl).toHaveBeenCalledWith({ url: unlockedContent().sourceUrl });
      expect(onSubmit).not.toHaveBeenCalled();
      await act(async () => resumeButton().click());
      expect(onSubmit).toHaveBeenCalledExactlyOnceWith("{}");
    },
  );
  it.each([{ taskId: 15 }, { revision: 5 }, { workflowRunId: 101 }])(
    "rejects a current snapshot for a different task identity: %j",
    async (updates) => {
      const other: PostContentSnapshot = {
        sourceUrl: "https://example.test/other-task",
        locks: [
          {
            blockId: "other",
            isBought: false,
            price: 99,
            contextText: "secret content from a different task or run",
          },
        ],
      };
      readinessApi.get.mockResolvedValue(response(currentView(other, updates)));
      const onSubmit = await renderUnlock(prompt());
      expect(screen.getByRole("alert")).toBeVisible();
      expect(container).not.toHaveTextContent("secret content from a different task or run");
      expect(resumeButton()).toBeDisabled();
      await act(async () => resumeButton().click());
      expect(onSubmit).not.toHaveBeenCalled();
      expect(reviewPortal).not.toHaveBeenCalled();
    },
  );
  it("keeps continuation disabled on GET failure and allows rereading the exact same identity", async () => {
    readinessApi.get
      .mockRejectedValueOnce(new Error("Connection interrupted."))
      .mockResolvedValueOnce(response());
    const onSubmit = await renderUnlock(prompt());
    const alert = screen.getByRole("alert");
    expect(alert).toBeVisible();
    expect(resumeButton()).toBeDisabled();
    await act(async () => within(alert).getByRole("button").click());
    await waitFor(() => expect(resumeButton()).toBeEnabled());
    expect(readinessApi.get).toHaveBeenCalledTimes(2);
    for (const call of readinessApi.get.mock.calls) expect(call[0]).toBe(14);
    expect(onSubmit).not.toHaveBeenCalled();
    await act(async () => resumeButton().click());
    expect(onSubmit).toHaveBeenCalledExactlyOnceWith("{}");
  });
  it.each(["refresh", "revoke", "confirm"] as const)(
    "reloads the current snapshot after a non-continuing %s review action",
    async (action) => {
      const refreshed = deferred();
      readinessApi.get
        .mockResolvedValueOnce(
          response(
            currentView(savedContent(), {
              status: "needsUnlock",
              canConfirm: true,
              canContinue: true,
            }),
          ),
        )
        .mockReturnValueOnce(refreshed.promise);
      const onSubmit = await renderUnlock(prompt());
      expect(container).toHaveTextContent("postParser.review.resumeUnlockHint");
      await act(async () => button("postParser.contentPurchaseReview.title").click());
      const callback = reviewPortal.mock.lastCall![1].onChanged;
      await act(async () => callback({ queuedCount: 0 }));
      expect(readinessApi.get).toHaveBeenCalledTimes(2);
      expect(resumeButton()).toBeDisabled();
      await act(async () => refreshed.resolve(response()));
      expect(resumeButton()).toBeEnabled();
      expect(container).not.toHaveTextContent("postParser.review.resumeUnlockHint");
      expect(button("postParser.action.openPost")).toBeVisible();
      expect(onSubmit).not.toHaveBeenCalled();
    },
  );
  it("isolates a late response after the waiting form changes to a newer task identity", async () => {
    const old = deferred();
    const nextContent = { ...unlockedContent(), sourceUrl: "https://example.test/current-task-15" };
    readinessApi.get
      .mockReturnValueOnce(old.promise)
      .mockResolvedValueOnce(
        response(currentView(nextContent, { taskId: 15, revision: 5, workflowRunId: 101 })),
      );
    const onSubmit = await renderUnlock(prompt());
    await renderUnlock(prompt({ taskId: 15, revision: 5, workflowRunId: 101 }), onSubmit);
    expect(resumeButton()).toBeEnabled();
    await act(async () => old.resolve(response(currentView(savedContent()))));
    expect(resumeButton()).toBeEnabled();
    expect(container).not.toHaveTextContent("postParser.review.resumeUnlockHint");
    await act(async () => button("postParser.action.openPost").click());
    expect(openUrl).toHaveBeenCalledExactlyOnceWith({ url: nextContent.sourceUrl });
    expect(onSubmit).not.toHaveBeenCalled();
  });
  it("does not trust a task-bound prompt that lacks a complete task identity", async () => {
    const onSubmit = await renderUnlock(prompt({ workflowRunId: undefined }));
    expect(readinessApi.get).not.toHaveBeenCalled();
    expect(screen.getByRole("alert")).toHaveTextContent("postParser.review.currentContentChanged");
    expect(resumeButton()).toBeDisabled();
    await act(async () => resumeButton().click());
    expect(onSubmit).not.toHaveBeenCalled();
  });
  it("resets continuation for a new identity and ignores callbacks from an older review portal", async () => {
    readinessApi.get
      .mockResolvedValueOnce(response())
      .mockResolvedValueOnce(
        response(currentView(unlockedContent(), { taskId: 15, revision: 5, workflowRunId: 101 })),
      );
    const onSubmit = await renderUnlock(prompt());
    await act(async () => button("postParser.contentPurchaseReview.title").click());
    const oldCallback = reviewPortal.mock.lastCall![1].onChanged;
    await act(async () => oldCallback({ queuedCount: 1 }));
    expect(resumeButton()).toBeDisabled();
    expect(readinessApi.get).toHaveBeenCalledOnce();
    await renderUnlock(prompt({ taskId: 15, revision: 5, workflowRunId: 101 }), onSubmit);
    expect(resumeButton()).toBeEnabled();
    await act(async () => oldCallback({ queuedCount: 1 }));
    await act(async () => oldCallback());
    expect(resumeButton()).toBeEnabled();
    expect(readinessApi.get).toHaveBeenCalledTimes(2);
    expect(onSubmit).not.toHaveBeenCalled();
  });
  it("ignores a GET failure after the waiting form has unmounted", async () => {
    const pending = deferred();
    readinessApi.get.mockReturnValue(pending.promise);
    const onSubmit = await renderUnlock(prompt());
    await act(async () => root.render(<p>The waiting form is closed.</p>));
    await act(async () => pending.reject(new Error("Late read failure.")));
    expect(container).toHaveTextContent("The waiting form is closed.");
    expect(screen.queryByRole("alert")).toBeNull();
    expect(readinessApi.get).toHaveBeenCalledOnce();
    expect(onSubmit).not.toHaveBeenCalled();
    expect(reviewPortal).not.toHaveBeenCalled();
  });
  it("accepts incomplete local plans for manual confirmation without treating files as already processed", () => {
    const value = {
      directory: "/downloads/resource",
      extractionPlanJson: JSON.stringify({ requirement: "unknown", steps: [] }),
    };

    expect(isProcessingPlanPayloadValid(JSON.stringify(value))).toBe(true);
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
    expect(
      isProcessingPlanPayloadValid(
        JSON.stringify({
          ...value,
          extractionPlanJson: JSON.stringify({ requirement: "required", steps: [] }),
        }),
      ),
    ).toBe(true);
    for (const extractionPlanJson of [
      "not json",
      JSON.stringify({ requirement: "unknown" }),
      JSON.stringify({ requirement: "required", steps: {} }),
      JSON.stringify({ requirement: "invalid", steps: [] }),
    ]) {
      expect(isProcessingPlanPayloadValid(JSON.stringify({ ...value, extractionPlanJson }))).toBe(
        false,
      );
      expect(
        isProcessingPlanPayloadValid(
          JSON.stringify({ ...value, extractionPlanJson, alreadyProcessed: true }),
        ),
      ).toBe(true);
    }
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
