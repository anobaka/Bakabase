import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { HeroUIProvider } from "@heroui/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";

import { ResourceCardPlaybackButton } from "./PlaybackButtons";

import { DataOrigin } from "@/sdk/constants";

// Keep the real React Aria keyboard handlers: a native-button mock would hide
// Tooltip/Button consuming keydown before it reaches a parent's bubble handler.
vi.mock("@/components/bakaui", async () => ({
  Button: (await import("@/components/bakaui/components/Button")).Button,
  Tooltip: (await import("@/components/bakaui/components/Tooltip")).default,
}));

beforeEach(() => {
  global.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
});
afterEach(cleanup);

it("enters and dismisses playback alternatives from a real HeroUI play button", async () => {
  const play = vi.fn();

  render(
    <HeroUIProvider disableAnimation>
      <ResourceCardPlaybackButton
        hasPath
        isFile
        fsDiscoveryStatus="ready"
        openFolderLabel="Open folder"
        originalSourceActions={[{ key: "source", label: "Original webpage", onOpen: vi.fn() }]}
        playbackCaption="Play resource"
        sources={[{ source: DataOrigin.FileSystem, items: [] }]}
        status="ready"
        triggerFsDiscovery={vi.fn()}
        onNotFound={vi.fn()}
        onOpenFolder={vi.fn()}
        onPlaySource={play}
      />
    </HeroUIProvider>,
  );
  const primary = screen.getByRole("button", { name: "Play resource" });

  act(() => primary.focus());
  expect(primary).toHaveFocus();
  fireEvent.keyDown(primary, { key: "ArrowDown", code: "ArrowDown" });
  const folder = screen.getByRole("menuitem", { name: "Open folder" });

  await waitFor(() => expect(folder).toHaveFocus());
  expect(play).not.toHaveBeenCalled();
  fireEvent.keyDown(folder, { key: "ArrowDown", code: "ArrowDown" });
  const original = screen.getByRole("menuitem", { name: "Original webpage" });

  await waitFor(() => expect(original).toHaveFocus());
  fireEvent.keyDown(original, { key: "Escape", code: "Escape" });
  expect(primary).toHaveFocus();
  expect(screen.getByRole("menu", { hidden: true })).toHaveClass("szh-menu--state-closed");

  // Re-enter the same menu and keep the primary button's direct-play keys intact.
  fireEvent.keyDown(primary, { key: "Tab", code: "Tab" });
  await waitFor(() => expect(folder).toHaveFocus());
  expect(play).not.toHaveBeenCalled();
  fireEvent.keyDown(folder, { key: "Escape", code: "Escape" });
  for (const [key, code] of [
    ["Enter", "Enter"],
    [" ", "Space"],
  ]) {
    fireEvent.keyDown(primary, { key, code });
    fireEvent.keyUp(primary, { key, code });
  }
  expect(play).toHaveBeenCalledTimes(2);
  expect(play).toHaveBeenLastCalledWith(DataOrigin.FileSystem);
});
