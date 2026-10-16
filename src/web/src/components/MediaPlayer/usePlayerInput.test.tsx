/* eslint-disable jsx-a11y/no-noninteractive-tabindex -- The fixture models the focusable player region. */
import React, { useRef, useState } from "react";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { usePlayerInput } from "./usePlayerInput";

import { MediaType } from "@/sdk/constants";
import { WindowManager } from "@/components/Window/WindowManager";

const manager = WindowManager.getInstance();
let now = 0;

const Player = ({ id, mediaType = MediaType.Image }: { id?: string; mediaType?: MediaType }) => {
  const root = useRef<HTMLDivElement>(null);
  const [index, setIndex] = useState(0);
  const [playing, setPlaying] = useState(false);

  usePlayerInput(root, {
    mediaType,
    canGoPrevious: index > 0,
    canGoNext: index < 3,
    previous: () => setIndex((value) => Math.max(0, value - 1)),
    next: () => setIndex((value) => Math.min(3, value + 1)),
    togglePlaying: () => setPlaying((value) => !value),
  });
  const content = (
    <div ref={root} data-testid={`player-${id ?? "embedded"}`} tabIndex={0}>
      <span data-testid={`index-${id ?? "embedded"}`}>{index}</span>
      <span data-testid={`playing-${id ?? "embedded"}`}>{String(playing)}</span>
      <input aria-label={`search-${id}`} />
      <textarea aria-label={`notes-${id}`} />
      <div contentEditable suppressContentEditableWarning data-testid={`editable-${id}`} />
      <video controls data-testid={`video-${id}`}>
        <track kind="captions" srcLang="en" />
      </video>
      <div data-testid={`files-${id}`} role="listbox" />
      <div data-testid={`scroll-${id}`} style={{ overflowY: "auto" }}>
        <div data-testid={`text-${id}`}>Scrollable text</div>
      </div>
      <button>Control {id}</button>
    </div>
  );

  return id ? (
    <div data-bakabase-window-id={id} data-testid={`window-${id}`}>
      <div data-testid={`title-${id}`}>Title {id}</div>
      {content}
      <div data-testid={`footer-${id}`}>Footer {id}</div>
    </div>
  ) : (
    content
  );
};

const registerWindow = (id: string) => manager.registerWindow(id, {});
const wheel = (target: HTMLElement, deltaY = 100, options: WheelEventInit = {}) => {
  const event = new WheelEvent("wheel", { bubbles: true, cancelable: true, deltaY, ...options });

  fireEvent(target, event);

  return event;
};
const index = (id = "one") => screen.getByTestId(`index-${id}`).textContent;

beforeEach(() => {
  now = 1000;
  vi.spyOn(performance, "now").mockImplementation(() => now);
});
afterEach(() => {
  cleanup();
  manager.getAllWindows().forEach(({ id }) => manager.unregisterWindow(id));
  vi.restoreAllMocks();
});

describe("media player input scope", () => {
  it("restores file-navigation focus after dragging a title and covers the footer", () => {
    registerWindow("one");
    render(<Player id="one" />);
    screen.getByLabelText("search-one").focus();
    fireEvent.mouseDown(screen.getByTestId("title-one"), { button: 0 });
    expect(screen.getByTestId("player-one")).toHaveFocus();
    // Model the browser's default mousedown blur, then release a title drag
    // outside its original window. The release must restore the player focus.
    screen.getByTestId("player-one").blur();
    fireEvent.mouseUp(document.body, { button: 0 });
    expect(screen.getByTestId("player-one")).toHaveFocus();
    fireEvent.keyDown(document.activeElement!, { key: "ArrowRight" });
    expect(index()).toBe("1");
    fireEvent.keyDown(screen.getByTestId("footer-one"), { key: "ArrowRight" });
    expect(index()).toBe("2");
    expect(wheel(screen.getByTestId("title-one"), -100).defaultPrevented).toBe(true);
    expect(index()).toBe("1");
  });

  it("does not restore drag focus when another window became active", () => {
    registerWindow("one");
    registerWindow("two");
    render(
      <>
        <Player id="one" />
        <Player id="two" />
      </>,
    );
    manager.bringToFront("one");
    fireEvent.mouseDown(screen.getByTestId("title-one"), { button: 0 });
    manager.bringToFront("two");
    screen.getByLabelText("search-two").focus();
    fireEvent.mouseUp(document.body, { button: 0 });
    expect(screen.getByLabelText("search-two")).toHaveFocus();
  });

  it("only lets the active window react, including wheel events over a rear player", () => {
    registerWindow("one");
    registerWindow("two");
    render(
      <>
        <Player id="one" />
        <Player id="two" />
      </>,
    );
    fireEvent.keyDown(screen.getByTestId("title-one"), { key: "ArrowRight" });
    wheel(screen.getByTestId("title-one"));
    expect(index("one")).toBe("0");
    fireEvent.keyDown(screen.getByTestId("title-two"), { key: "ArrowRight" });
    expect(index("two")).toBe("1");
    manager.bringToFront("one");
    fireEvent.mouseDown(screen.getByTestId("title-one"), { button: 0 });
    fireEvent.keyDown(document.activeElement!, { key: "ArrowRight" });
    expect(index("one")).toBe("1");
    expect(index("two")).toBe("1");
  });

  it("keeps embedded shortcuts local to their own region", () => {
    render(<Player />);
    fireEvent.keyDown(document.body, { key: "ArrowRight" });
    wheel(document.body);
    expect(index("embedded")).toBe("0");
    fireEvent.keyDown(screen.getByTestId("player-embedded"), { key: "ArrowRight" });
    expect(index("embedded")).toBe("1");
  });

  it("does not steal text input, contenteditable or native video controls", () => {
    registerWindow("one");
    render(<Player id="one" />);
    const controls = [
      screen.getByLabelText("search-one"),
      screen.getByLabelText("notes-one"),
      screen.getByTestId("editable-one"),
      screen.getByTestId("video-one"),
    ];

    for (const control of controls) {
      fireEvent.keyDown(control, { key: "ArrowRight" });
      expect(wheel(control).defaultPrevented).toBe(false);
    }
    expect(index()).toBe("0");
    screen.getByLabelText("search-one").focus();
    fireEvent.mouseDown(screen.getByLabelText("search-one"), { button: 0 });
    expect(screen.getByLabelText("search-one")).toHaveFocus();
  });

  it("keeps arrow keys and image wheel usable over toolbar buttons without stealing their focus or activation", () => {
    registerWindow("one");
    render(<Player id="one" />);
    const button = screen.getByRole("button");

    button.focus();
    fireEvent.mouseDown(button, { button: 0 });
    fireEvent.mouseUp(button, { button: 0 });
    expect(button).toHaveFocus();
    fireEvent.keyDown(button, { key: "ArrowRight" });
    expect(index()).toBe("1");
    expect(wheel(button).defaultPrevented).toBe(true);
    expect(index()).toBe("2");
    const space = new KeyboardEvent("keydown", { bubbles: true, cancelable: true, key: " " });

    fireEvent(button, space);
    expect(space.defaultPrevented).toBe(false);
  });

  it("leaves menu triggers and tabs in control of their arrow keys and wheel", () => {
    registerWindow("one");
    render(<Player id="one" />);
    const button = screen.getByRole("button");

    for (const attributes of [{ "aria-haspopup": "menu" }, { role: "tab" }]) {
      for (const [name, value] of Object.entries(attributes)) button.setAttribute(name, value);
      fireEvent.keyDown(button, { key: "ArrowRight" });
      expect(wheel(button).defaultPrevented).toBe(false);
      for (const name of Object.keys(attributes)) button.removeAttribute(name);
    }
    expect(index()).toBe("0");
  });

  it("leaves file lists and scrollable text available for normal scrolling", () => {
    registerWindow("one");
    render(<Player id="one" />);
    const scroll = screen.getByTestId("scroll-one");

    Object.defineProperties(scroll, {
      clientHeight: { value: 100 },
      scrollHeight: { value: 500 },
    });
    const files = screen.getByTestId("files-one");

    expect(wheel(files).defaultPrevented).toBe(false);
    expect(wheel(screen.getByTestId("text-one")).defaultPrevented).toBe(false);
    fireEvent.keyDown(files, { key: "ArrowRight" });
    fireEvent.keyDown(screen.getByTestId("text-one"), { key: "ArrowRight" });
    expect(index()).toBe("0");
  });

  it("leaves pinch zoom, modifier wheels and horizontal gestures untouched", () => {
    registerWindow("one");
    render(<Player id="one" />);
    for (const options of [
      { ctrlKey: true },
      { metaKey: true },
      { shiftKey: true },
      { deltaX: 200 },
    ])
      expect(wheel(screen.getByTestId("title-one"), 100, options).defaultPrevented).toBe(false);
    fireEvent.keyDown(screen.getByTestId("title-one"), { key: "ArrowRight", metaKey: true });
    expect(index()).toBe("0");
  });

  it("accumulates small trackpad deltas and throttles momentum", () => {
    registerWindow("one");
    render(<Player id="one" />);
    const title = screen.getByTestId("title-one");

    wheel(title, 20);
    now += 20;
    wheel(title, 20);
    expect(index()).toBe("0");
    now += 20;
    wheel(title, 20);
    expect(index()).toBe("1");
    for (let n = 0; n < 10; n++) {
      now += 10;
      wheel(title, 100);
    }
    expect(index()).toBe("1");
    now += 300;
    wheel(title, 3, { deltaMode: WheelEvent.DOM_DELTA_LINE });
    expect(index()).toBe("2");
  });

  it("resets small wheel accumulation when direction changes or a gesture ends", () => {
    registerWindow("one");
    render(<Player id="one" />);
    const title = screen.getByTestId("title-one");

    wheel(title, 30);
    now += 300;
    wheel(title, 30);
    expect(index()).toBe("0");
    fireEvent.keyDown(title, { key: "ArrowRight" });
    now += 20;
    wheel(title, -30);
    expect(index()).toBe("1");
    now += 20;
    wheel(title, -30);
    expect(index()).toBe("0");
  });

  it("does not wrap past either boundary", () => {
    registerWindow("one");
    render(<Player id="one" />);
    const title = screen.getByTestId("title-one");

    expect(wheel(title, -100).defaultPrevented).toBe(false);
    expect(index()).toBe("0");
    for (let n = 0; n < 8; n++) fireEvent.keyDown(title, { key: "ArrowRight" });
    expect(index()).toBe("3");
    expect(wheel(title).defaultPrevented).toBe(false);
    expect(index()).toBe("3");
  });

  it.each([MediaType.Video, MediaType.Audio, MediaType.Text])(
    "does not navigate media type %s with wheel",
    (mediaType) => {
      registerWindow("one");
      render(<Player id="one" mediaType={mediaType} />);
      expect(wheel(screen.getByTestId("title-one")).defaultPrevented).toBe(false);
      expect(index()).toBe("0");
    },
  );

  it.each([MediaType.Video, MediaType.Audio])(
    "allows space on the window to toggle media type %s playback",
    (mediaType) => {
      registerWindow("one");
      render(<Player id="one" mediaType={mediaType} />);
      fireEvent.keyDown(screen.getByTestId("footer-one"), { key: " " });
      expect(screen.getByTestId("playing-one")).toHaveTextContent("true");
      fireEvent.keyDown(screen.getByTestId("video-one"), { key: " " });
      expect(screen.getByTestId("playing-one")).toHaveTextContent("true");
    },
  );

  it("does not navigate while minimized or behind a modal dialog", () => {
    registerWindow("one");
    const { unmount } = render(<Player id="one" />);
    const title = screen.getByTestId("title-one");

    manager.updateWindow("one", { isMinimized: true });
    fireEvent.keyDown(title, { key: "ArrowRight" });
    expect(index()).toBe("0");
    manager.updateWindow("one", { isMinimized: false });
    const modal = document.createElement("div");

    modal.setAttribute("role", "dialog");
    modal.setAttribute("aria-modal", "true");
    document.body.append(modal);
    expect(wheel(title).defaultPrevented).toBe(false);
    fireEvent.keyDown(title, { key: "ArrowRight" });
    expect(index()).toBe("0");
    modal.remove();
    unmount();
    expect(() => wheel(title)).not.toThrow();
  });
});
