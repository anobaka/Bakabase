import { HeroUIProvider } from "@heroui/react";
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import TaskLinkCopyButton from "../TaskLinkCopyButton";

const { dangerToast } = vi.hoisted(() => ({ dangerToast: vi.fn() }));

vi.mock("@/components/bakaui", async () => ({
  ...(await import("@heroui/react")),
  Button: (await import("@/components/bakaui/components/Button")).Button,
  toast: { danger: dangerToast },
}));

const source = "https://exhentai.org/g/4171374/abcdef0123/?p=2&f_search=a%20b";
const parentEvents = { click: vi.fn(), contextMenu: vi.fn(), keyDown: vi.fn() };
let root: Root;
let container: HTMLDivElement;
let writeText: ReturnType<typeof vi.fn>;
let originalClipboard: PropertyDescriptor | undefined;
let originalExecCommand: typeof document.execCommand;

const button = () =>
  document.querySelector<HTMLElement>('[aria-label="downloader.action.copyDownloadLink"]')!;
const feedback = () => container.querySelector<HTMLElement>('[aria-live="polite"]')!;

async function show(value: string | undefined = source) {
  await act(async () =>
    root.render(
      <HeroUIProvider disableAnimation>
        <div
          role="presentation"
          onClick={parentEvents.click}
          onContextMenu={parentEvents.contextMenu}
          onKeyDown={parentEvents.keyDown}
        >
          <TaskLinkCopyButton value={value} />
        </div>
      </HeroUIProvider>,
    ),
  );
}

async function click() {
  await act(async () => {
    button().dispatchEvent(new MouseEvent("click", { bubbles: true, cancelable: true }));
  });
}

beforeEach(() => {
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  originalClipboard = Object.getOwnPropertyDescriptor(navigator, "clipboard");
  originalExecCommand = document.execCommand;
  writeText = vi.fn().mockResolvedValue(undefined);
  Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText } });
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  if (originalClipboard) Object.defineProperty(navigator, "clipboard", originalClipboard);
  else Reflect.deleteProperty(navigator, "clipboard");
  document.execCommand = originalExecCommand;
  vi.useRealTimers();
  vi.unstubAllGlobals();
  vi.clearAllMocks();
});

describe("download task link copying", () => {
  it("copies the exact address without invoking its parent's actions", async () => {
    await show();
    await click();
    await act(async () => {
      button().dispatchEvent(new MouseEvent("contextmenu", { bubbles: true }));
    });

    expect(writeText).toHaveBeenCalledExactlyOnceWith(source);
    expect(feedback()).toHaveTextContent("common.state.copied");
    expect(parentEvents.click).not.toHaveBeenCalled();
    expect(parentEvents.contextMenu).not.toHaveBeenCalled();
  });

  it.each(["Enter", " "])("copies with %s without bubbling into row selection", async (key) => {
    await show();
    await act(async () => {
      button().focus();
      for (const type of ["keydown", "keyup"]) {
        button().dispatchEvent(
          new KeyboardEvent(type, {
            key,
            code: key === " " ? "Space" : key,
            bubbles: true,
            cancelable: true,
          }),
        );
      }
    });

    expect(writeText).toHaveBeenCalledExactlyOnceWith(source);
    expect(parentEvents.click).not.toHaveBeenCalled();
    expect(parentEvents.keyDown).not.toHaveBeenCalled();
  });

  it("restores the brief copied feedback after two seconds", async () => {
    vi.useFakeTimers();
    await show();
    await click();
    expect(feedback()).toHaveTextContent("common.state.copied");

    await act(async () => {
      await vi.advanceTimersByTimeAsync(2000);
    });
    expect(feedback()).toBeEmptyDOMElement();
    expect(button()).toHaveAccessibleName("downloader.action.copyDownloadLink");
  });

  it("uses the existing insecure-context clipboard fallback", async () => {
    Object.defineProperty(navigator, "clipboard", { configurable: true, value: undefined });
    const execCommand = vi.fn().mockReturnValue(true);

    document.execCommand = execCommand;
    await show();
    await click();

    expect(execCommand).toHaveBeenCalledExactlyOnceWith("copy");
    expect(container.querySelector("textarea")).toBeNull();
    expect(feedback()).toHaveTextContent("common.state.copied");
    expect(dangerToast).not.toHaveBeenCalled();
  });

  it("reports failed clipboard attempts without displaying copied feedback", async () => {
    writeText.mockRejectedValue(new Error("denied"));
    document.execCommand = vi.fn().mockReturnValue(false);
    await show();
    await click();

    expect(dangerToast).toHaveBeenCalledExactlyOnceWith("common.message.copyFailed");
    expect(feedback()).toBeEmptyDOMElement();
    expect(button()).not.toBeDisabled();
  });

  it("clears a previous success hint when another copy attempt fails", async () => {
    await show();
    await click();
    expect(feedback()).toHaveTextContent("common.state.copied");

    writeText.mockRejectedValue(new Error("denied"));
    document.execCommand = vi.fn().mockReturnValue(false);
    await click();
    expect(feedback()).toBeEmptyDOMElement();
    expect(dangerToast).toHaveBeenCalledExactlyOnceWith("common.message.copyFailed");
  });

  it("copies the current edited value rather than retaining the original address", async () => {
    await show();
    await click();
    const edited = "https://exhentai.org/g/4171375/abcdef0123/";

    await show(edited);
    expect(feedback()).toBeEmptyDOMElement();
    await click();
    expect(writeText.mock.calls).toEqual([[source], [edited]]);
    expect(feedback()).toHaveTextContent("common.state.copied");
  });

  it("does not attribute an old pending copy to a newly edited address", async () => {
    let finish!: () => void;

    writeText.mockImplementation(
      () =>
        new Promise<void>((resolve) => {
          finish = resolve;
        }),
    );
    await show();
    await click();
    await show("https://exhentai.org/g/4171375/abcdef0123/");
    await act(async () => {
      finish();
    });

    expect(feedback()).toBeEmptyDOMElement();
    expect(button()).not.toBeDisabled();
  });

  it("does not offer to copy an empty field", async () => {
    await show(" ");
    expect(button()).toBeDisabled();
    await click();
    expect(writeText).not.toHaveBeenCalled();
    expect(feedback()).toBeEmptyDOMElement();
  });
});
