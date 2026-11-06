import { HeroUIProvider } from "@heroui/react";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";

import { REDOCK_DELAY, WAVING_BYE_DURATION } from "./constants";

import FloatingAssistantV2 from ".";

vi.mock("@/components/bakaui", async () => ({
  Popover: (await import("@/components/bakaui/components/Popover")).default,
}));
vi.mock("@/stores/bTasks", () => ({
  useBTasksStore: (select: (state: { tasks: never[] }) => unknown) => select({ tasks: [] }),
}));
vi.mock("./components/BakaCharacter", () => ({ default: () => null }));
vi.mock("./components/ChatPanel", () => ({ default: () => null }));
vi.mock("./components/TaskPopover", () => ({ default: () => <div>Background tasks</div> }));

afterEach(() => vi.useRealTimers());

it("opens the task panel during the dock animation and keeps the anchor in place", () => {
  vi.useFakeTimers();
  render(<FloatingAssistantV2 />, { wrapper: HeroUIProvider });
  const assistant = screen.getByRole("button", { name: "floatingAssistant.label.taskList" });

  fireEvent.mouseEnter(assistant);
  fireEvent.mouseLeave(assistant);
  act(() => vi.advanceTimersByTime(REDOCK_DELAY));
  expect(assistant).toHaveClass("baka-state-waving-bye");

  fireEvent.click(assistant);
  expect(assistant).toHaveAttribute("aria-expanded", "true");
  expect(screen.getByRole("dialog")).toHaveTextContent("Background tasks");
  act(() => vi.advanceTimersByTime(REDOCK_DELAY + WAVING_BYE_DURATION));
  expect(assistant).toHaveClass("baka-state-hovered");

  fireEvent.keyDown(screen.getByRole("dialog"), { key: "Escape" });
  expect(assistant).toHaveAttribute("aria-expanded", "false");
});
