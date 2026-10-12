import React from "react";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import Window from "./Window";
import { WindowManager } from "./WindowManager";
import { readWindowBounds, writeWindowBounds } from "./persistence";

const handlers = vi.hoisted(() => new Map<string, any>());

vi.mock("react-rnd", async () => {
  const { forwardRef } = await import("react");

  return {
    Rnd: forwardRef(({ children, ...props }: any, _ref) => {
      const id = props["data-bakabase-window-id"];

      handlers.set(id, props);

      return (
        <div
          data-bakabase-window-id={id}
          data-height={props.size.height}
          data-testid="window"
          data-width={props.size.width}
          data-x={props.position.x}
          data-y={props.position.y}
        >
          {children}
        </div>
      );
    }),
  };
});
vi.mock("./WindowHeader", () => ({
  WindowHeader: ({ title, onClose, onMinimize, onMaximize }: any) => (
    <div>
      <span>{title}</span>
      <button onClick={() => onClose()}>{title} close</button>
      <button onClick={() => onMinimize()}>{title} minimize</button>
      <button onClick={() => onMaximize()}>{title} maximize</button>
    </div>
  ),
}));

const manager = WindowManager.getInstance();
const options = {
  persistenceKey: "media-player",
  initialSize: { width: 800, height: 500 },
  initialPosition: { x: 100, y: 100 },
};
const viewport = (width: number, height: number) => {
  Object.defineProperty(window, "innerWidth", { configurable: true, value: width });
  Object.defineProperty(window, "innerHeight", { configurable: true, value: height });
};
const getWindow = (title: string) =>
  screen.getByText(title).closest('[data-testid="window"]')! as HTMLElement;
const getHandlers = (title: string) =>
  handlers.get(getWindow(title).getAttribute("data-bakabase-window-id")!);
const drag = (title: string, x: number, y: number) => {
  act(() => getHandlers(title).onDragStart());
  act(() => getHandlers(title).onDragStop({}, { x, y }));
};
const resize = (title: string, width: number, height: number) => {
  act(() => getHandlers(title).onResizeStart());
  const position = { ...getHandlers(title).position };

  act(() =>
    getHandlers(title).onResizeStop(
      {},
      "bottomRight",
      { offsetWidth: width, offsetHeight: height },
      {},
      position,
    ),
  );
};

beforeEach(() => {
  viewport(1600, 1200);
  localStorage.clear();
  handlers.clear();
  for (const window of manager.getAllWindows()) manager.unregisterWindow(window.id);
});
afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe("remembered media-player windows", () => {
  it("reopens at the user-dragged and resized ordinary bounds", () => {
    const first = render(
      <Window title="First" windowOptions={options}>
        media
      </Window>,
    );

    expect(readWindowBounds("media-player")).toBeUndefined();
    drag("First", 240, 180);
    resize("First", 660, 420);
    expect(readWindowBounds("media-player")).toEqual({ x: 240, y: 180, width: 660, height: 420 });
    first.unmount();
    render(
      <Window title="Reopened" windowOptions={options}>
        media
      </Window>,
    );
    expect(getWindow("Reopened").dataset).toMatchObject({
      x: "240",
      y: "180",
      width: "660",
      height: "420",
    });
  });

  it("does not save maximizing, minimizing, or viewport corrections", () => {
    render(
      <Window title="First" windowOptions={options}>
        media
      </Window>,
    );
    drag("First", 240, 180);
    const saved = readWindowBounds("media-player");

    fireEvent.click(screen.getByText("First maximize"));
    act(() => getHandlers("First").onDragStop({}, { x: 999, y: 999 }));
    viewport(640, 480);
    fireEvent(window, new Event("resize"));
    expect(getWindow("First").dataset).toMatchObject({
      x: "0",
      y: "0",
      width: "640",
      height: "480",
    });
    fireEvent.click(screen.getByText("First maximize"));
    expect(getWindow("First").dataset).toMatchObject({ width: "608", height: "448" });
    fireEvent.click(screen.getByText("First minimize"));
    drag("First", 50, 50);
    expect(readWindowBounds("media-player")).toEqual(saved);
  });

  it("keeps the 40px cascade and does not remember an automatic offset when resizing a second window", () => {
    render(
      <>
        <Window title="First" windowOptions={options}>
          media
        </Window>
        <Window title="Second" windowOptions={options}>
          media
        </Window>
      </>,
    );
    expect(getWindow("First").dataset).toMatchObject({ x: "100", y: "100" });
    expect(getWindow("Second").dataset).toMatchObject({ x: "140", y: "140" });
    drag("Second", 140, 140);
    expect(readWindowBounds("media-player")).toBeUndefined();
    resize("Second", 660, 420);
    expect(readWindowBounds("media-player")).toEqual({ x: 100, y: 100, width: 660, height: 420 });
    fireEvent.click(screen.getByText("Second close"));
    const reopened = render(
      <Window title="Third" windowOptions={options}>
        media
      </Window>,
    );

    expect(getWindow("Third").dataset).toMatchObject({
      x: "140",
      y: "140",
      width: "660",
      height: "420",
    });
    resize("Third", 700, 460);
    expect(readWindowBounds("media-player")).toEqual({ x: 100, y: 100, width: 700, height: 460 });
    reopened.unmount();
    fireEvent.click(screen.getByText("First close"));
    render(
      <Window title="Alone" windowOptions={options}>
        media
      </Window>,
    );
    expect(getWindow("Alone").dataset).toMatchObject({
      x: "100",
      y: "100",
      width: "700",
      height: "460",
    });
  });

  it("remembers the positional delta of a top-left resize without saving its cascade offset", () => {
    render(
      <>
        <Window title="First" windowOptions={options}>
          media
        </Window>
        <Window title="Second" windowOptions={options}>
          media
        </Window>
      </>,
    );
    act(() => getHandlers("Second").onResizeStart());
    act(() =>
      getHandlers("Second").onResizeStop(
        {},
        "topLeft",
        { offsetWidth: 700, offsetHeight: 400 },
        {},
        { x: 240, y: 240 },
      ),
    );
    expect(readWindowBounds("media-player")).toEqual({ x: 200, y: 200, width: 700, height: 400 });
  });

  it("cascades inward from remembered bottom-right bounds without changing the saved position", () => {
    writeWindowBounds("media-player", { x: 800, y: 700, width: 800, height: 500 });
    render(
      <>
        <Window title="First" windowOptions={options}>
          media
        </Window>
        <Window title="Second" windowOptions={options}>
          media
        </Window>
        <Window title="Third" windowOptions={options}>
          media
        </Window>
      </>,
    );
    expect(getWindow("First").dataset).toMatchObject({ x: "800", y: "700" });
    expect(getWindow("Second").dataset).toMatchObject({ x: "760", y: "660" });
    expect(getWindow("Third").dataset).toMatchObject({ x: "720", y: "620" });
    resize("Second", 660, 420);
    expect(readWindowBounds("media-player")).toEqual({ x: 800, y: 700, width: 660, height: 420 });
  });

  it("clamps offscreen saved bounds on a small screen and recovers from a blocked storage write", () => {
    writeWindowBounds("media-player", { x: 1500, y: 1000, width: 1000, height: 700 });
    viewport(320, 240);
    render(
      <Window title="Small" windowOptions={options}>
        media
      </Window>,
    );
    expect(getWindow("Small").dataset).toMatchObject({
      x: "32",
      y: "32",
      width: "288",
      height: "208",
    });
    const setItem = vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new Error("full");
    });

    expect(() => drag("Small", 0, 0)).not.toThrow();
    expect(getWindow("Small").dataset).toMatchObject({ x: "0", y: "0" });
    expect(setItem).toHaveBeenCalledTimes(1);
  });
});
