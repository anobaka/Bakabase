import { HeroUIProvider } from "@heroui/react";
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import TaskErrorMessage, { CLICK_TO_COPY_DELAY_MS } from "../TaskErrorMessage";

const toast = vi.hoisted(() => ({ success: vi.fn(), danger: vi.fn() }));

vi.mock("@/components/bakaui", async () => ({
  ...(await import("@heroui/react")),
  Button: (await import("@/components/bakaui/components/Button")).Button,
  toast,
}));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));

const message = `An error occurred during downloading files.
System.Net.Http.HttpRequestException: The SSL connection could not be established, see inner exception.
 ---> System.IO.IOException: Received an unexpected EOF or 0 bytes from the transport stream.`;

let container: HTMLDivElement;
let root: Root;
let writeText: ReturnType<typeof vi.fn>;

function setClipboard(value: unknown) {
  Object.defineProperty(navigator, "clipboard", { configurable: true, value });
}

async function show(copyTip?: string) {
  await act(async () =>
    root.render(
      <HeroUIProvider disableAnimation>
        <TaskErrorMessage copyTip={copyTip} message={message} />
      </HeroUIProvider>,
    ),
  );
}

// detail 0 is how react-aria recognises a press that arrives as a bare click (keyboard, screen
// reader); a pointer click on the text reports 1, 2, 3 for single, double and triple clicks.
async function click(target: Element, detail = 0) {
  await act(async () => {
    target.dispatchEvent(new MouseEvent("click", { bubbles: true, cancelable: true, detail }));
  });
}

/** Lets a click on the text outlive the double-click window it waits out. */
async function afterClickDelay() {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(CLICK_TO_COPY_DELAY_MS);
  });
}

function selectWord(target: Element) {
  const range = document.createRange();

  range.setStart(target.firstChild!, 0);
  range.setEnd(target.firstChild!, 2);
  window.getSelection()!.addRange(range);
}

const copyButton = () => document.querySelector<HTMLElement>('[aria-label="common.action.copy"]')!;
const text = () => document.querySelector("pre")!;

beforeEach(() => {
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  writeText = vi.fn().mockResolvedValue(undefined);
  setClipboard({ writeText });
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});
afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  window.getSelection()?.removeAllRanges();
  vi.useRealTimers();
  vi.unstubAllGlobals();
  vi.clearAllMocks();
});

describe("download task error message", () => {
  it("shows the whole message with its line breaks", async () => {
    await show();

    expect(text().textContent).toBe(message);
    expect(text()).toHaveClass("whitespace-pre-wrap");
  });

  it("copies the whole message from the copy button and says so", async () => {
    await show();
    await click(copyButton());

    expect(writeText).toHaveBeenCalledExactlyOnceWith(message);
    expect(toast.success).toHaveBeenCalledExactlyOnceWith("common.message.copiedToClipboard");
    expect(toast.danger).not.toHaveBeenCalled();
  });

  it("copies the whole message when the text itself is clicked", async () => {
    vi.useFakeTimers();
    await show();

    expect(text()).toHaveAttribute("title", "downloader.tip.clickToCopyError");
    await click(text(), 1);
    expect(writeText).not.toHaveBeenCalled();
    await afterClickDelay();

    expect(writeText).toHaveBeenCalledExactlyOnceWith(message);
    expect(toast.success).toHaveBeenCalledOnce();
  });

  it("uses the given hover hint, e.g. for a completed task's notes", async () => {
    vi.useFakeTimers();
    await show("downloader.tip.clickToCopyNotices");

    expect(text()).toHaveAttribute("title", "downloader.tip.clickToCopyNotices");
    await click(text(), 1);
    await afterClickDelay();

    expect(writeText).toHaveBeenCalledExactlyOnceWith(message);
  });

  it("leaves a dragged selection alone", async () => {
    vi.useFakeTimers();
    await show();
    const range = document.createRange();

    range.selectNodeContents(text());
    window.getSelection()!.addRange(range);
    await click(text(), 1);
    await afterClickDelay();

    expect(writeText).not.toHaveBeenCalled();
    expect(toast.success).not.toHaveBeenCalled();
  });

  it.each([2, 3])("leaves a %s-click word or line selection alone", async (clicks) => {
    vi.useFakeTimers();
    await show();

    await click(text(), 1);
    // A comfortable double-click pace: the first click must still be waiting when the next lands.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(150);
    });
    selectWord(text());
    for (let detail = 2; detail <= clicks; detail++) {
      await click(text(), detail);
    }
    await afterClickDelay();

    expect(writeText).not.toHaveBeenCalled();
    expect(toast.success).not.toHaveBeenCalled();
  });

  it("falls back to a hidden textarea where the Clipboard API is unavailable", async () => {
    // Plain-HTTP access from another machine is not a secure context: no navigator.clipboard.
    setClipboard(undefined);
    const execCommand = vi.fn().mockReturnValue(true);

    document.execCommand = execCommand;
    await show();
    await click(copyButton());

    expect(execCommand).toHaveBeenCalledWith("copy");
    expect(document.querySelector("textarea")).not.toBeInTheDocument();
    expect(toast.success).toHaveBeenCalledOnce();
  });

  it("reports a failed copy instead of claiming success", async () => {
    writeText.mockRejectedValue(new Error("denied"));
    document.execCommand = vi.fn().mockReturnValue(false);
    await show();
    await click(copyButton());

    expect(toast.danger).toHaveBeenCalledExactlyOnceWith("common.message.copyFailed");
    expect(toast.success).not.toHaveBeenCalled();
  });
});
