import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ResourceContextMenu from "..";

const mocks = vi.hoisted(() => ({ activate: vi.fn(), rendered: vi.fn() }));

vi.mock("../../ContextMenuItems", async () => {
  const { MenuItem, SubMenu } = await import("@szhsin/react-menu");

  return {
    default: () => {
      mocks.rendered();

      return (
        <>
          <MenuItem disabled>Unavailable action</MenuItem>
          <MenuItem onClick={mocks.activate}>Open folder</MenuItem>
          <SubMenu label="More actions">
            <MenuItem onClick={mocks.activate}>Nested action</MenuItem>
          </SubMenu>
        </>
      );
    },
  };
});

const renderMenu = (disabled = false) => {
  render(
    <ResourceContextMenu
      contextResource={{ id: 42, displayName: "Example" }}
      disabled={disabled}
      selectedResourceIds={[42]}
    >
      <span>Resource cover</span>
    </ResourceContextMenu>,
  );

  return screen.getByRole("group");
};

beforeEach(() => {
  vi.clearAllMocks();
  vi.stubGlobal(
    "ResizeObserver",
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  );
  vi.spyOn(document, "hasFocus").mockReturnValue(true);
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("resource context menu entry", () => {
  it("lazily opens a portal at the pointer and activates the selected action once", async () => {
    const trigger = renderMenu();

    expect(mocks.rendered).not.toHaveBeenCalled();
    fireEvent.contextMenu(trigger, { clientX: 220, clientY: 160 });
    const menu = await screen.findByRole("menu");

    expect(trigger.contains(menu)).toBe(false);
    expect(menu).toHaveClass("resource-context-menu");
    fireEvent.click(screen.getByRole("menuitem", { name: "Open folder" }));
    expect(mocks.activate).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(screen.queryByRole("menu")).not.toBeInTheDocument());
  });

  it.each([{ key: "ContextMenu" }, { key: "F10", shiftKey: true }])(
    "supports $key and returns focus to the resource on Escape",
    async (key) => {
      const trigger = renderMenu();

      trigger.focus();
      fireEvent.keyDown(trigger, key);
      const firstAction = await screen.findByRole("menuitem", { name: "Open folder" });

      await waitFor(() => expect(firstAction).toHaveFocus());
      fireEvent.keyDown(firstAction, { key: "Escape" });
      await waitFor(() => expect(screen.queryByRole("menu")).not.toBeInTheDocument());
      expect(trigger).toHaveFocus();
      expect(mocks.activate).not.toHaveBeenCalled();
    },
  );

  it("keeps native arrow-key submenu navigation and Enter activation", async () => {
    const trigger = renderMenu();

    fireEvent.keyDown(trigger, { key: "ContextMenu" });
    const firstAction = await screen.findByRole("menuitem", { name: "Open folder" });

    await waitFor(() => expect(firstAction).toHaveFocus());
    fireEvent.keyDown(firstAction, { key: "ArrowDown" });
    const submenu = screen.getByRole("menuitem", { name: "More actions" });

    await waitFor(() => expect(submenu).toHaveFocus());
    fireEvent.keyDown(submenu, { key: "ArrowRight" });
    const nested = await screen.findByRole("menuitem", { name: "Nested action" });

    await waitFor(() => expect(nested).toHaveFocus());
    fireEvent.keyDown(nested, { key: "Enter" });
    expect(mocks.activate).toHaveBeenCalledTimes(1);
  });

  it("keeps moving resources unavailable through both pointer and keyboard", () => {
    const trigger = renderMenu(true);

    expect(trigger).toHaveAttribute("tabindex", "-1");
    fireEvent.contextMenu(trigger, { clientX: 220, clientY: 160 });
    fireEvent.keyDown(trigger, { key: "F10", shiftKey: true });
    expect(screen.queryByRole("menu")).not.toBeInTheDocument();
    expect(mocks.rendered).not.toHaveBeenCalled();
  });
});
