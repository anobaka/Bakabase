import React from "react";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { WindowHeader } from "./WindowHeader";
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({ onPress, children, "aria-label": label }: any) => (
    <button
      aria-label={label}
      onClick={() =>
        onPress({ type: "press", pointerType: "mouse", continuePropagation: () => {} })
      }
    >
      {children}
    </button>
  ),
  Tooltip: ({ children }: any) => <>{children}</>,
  Kbd: () => null,
  Modal: () => null,
}));
afterEach(cleanup);
it("does not pass HeroUI's PressEvent into window actions that accept a React mouse event", () => {
  const close = vi.fn((event?: { stopPropagation: () => void }) => event?.stopPropagation());
  const minimize = vi.fn();
  const maximize = vi.fn();
  render(
    <WindowHeader
      windowState={{
        id: "header",
        x: 0,
        y: 0,
        width: 100,
        height: 100,
        isMaximized: false,
        isMinimized: false,
        zIndex: 1,
      }}
      onClose={close}
      onMinimize={minimize}
      onMaximize={maximize}
    />,
  );
  fireEvent.click(screen.getByRole("button", { name: "mediaPlayer.window.close" }));
  expect(close).toHaveBeenCalledWith();
  fireEvent.click(screen.getByRole("button", { name: "mediaPlayer.window.minimize" }));
  expect(minimize).toHaveBeenCalledWith();
  fireEvent.click(screen.getByRole("button", { name: "mediaPlayer.window.maximize" }));
  expect(maximize).toHaveBeenCalledWith();
});
