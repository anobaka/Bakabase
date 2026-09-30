import type { Resource } from "@/core/models/Resource";

import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import Operations from "../index";

import { ResourceSource } from "@/sdk/constants";

const state = vi.hoisted(() => ({
  displayOperations: ["move"],
  openMovePanel: vi.fn(),
}));

vi.mock("react-i18next", () => ({
  useTranslation: () => ({ t: (key: string) => key, i18n: { language: "en" } }),
}));
vi.mock("@/stores/options", () => ({
  useUiOptionsStore: (select: (value: unknown) => unknown) =>
    select({ data: { resource: { displayOperations: state.displayOperations } } }),
}));
vi.mock("@/stores/resourceMovePanel", () => ({ openMovePanel: state.openMovePanel }));
vi.mock("@/sdk/BApi", () => ({ default: {} }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn(), createWindow: vi.fn() }),
}));
vi.mock("@/components/Resource/components/ResourceEnhancementsModal.tsx", () => ({
  default: () => null,
}));
vi.mock("@/components/Resource/components/DeleteResourceConfirmContent", () => ({
  default: () => null,
}));
vi.mock("@/components/Playlist", () => ({ PlaylistCollection: () => null }));
vi.mock("@/components/MediaPlayer", () => ({ default: () => null }));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, isDisabled, onPress, "aria-label": ariaLabel }: any) => (
    <button aria-label={ariaLabel} disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Tooltip: ({ children, content }: any) => <div data-tooltip={content}>{children}</div>,
  Dropdown: ({ children }: any) => <div>{children}</div>,
  DropdownTrigger: ({ children }: any) => <div>{children}</div>,
  DropdownMenu: ({ children }: any) => <div role="menu">{children}</div>,
  DropdownItem: ({ children, isDisabled, description }: any) => (
    <div aria-disabled={isDisabled} role="menuitem">
      {children}
      {description && <p>{description}</p>}
    </div>
  ),
  Modal: () => null,
  toast: {},
}));

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  state.displayOperations = ["move"];
  state.openMovePanel.mockReset();
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  container = document.createElement("div");
  document.body.append(container);
  root = createRoot(container);
});
afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

const show = async (resource: Partial<Resource>) => {
  await act(async () =>
    root.render(<Operations resource={{ id: 1, ...resource } as Resource} sourceTabId="tab-one" />),
  );
};

describe("resource card move entry", () => {
  it("keeps the Steam move button visible and disabled with client storage guidance", async () => {
    await show({
      path: "/steam/game",
      hasLocalPath: true,
      sourceLinks: [{ source: ResourceSource.Steam }] as Resource["sourceLinks"],
    });
    const button = container.querySelector("button")!;

    expect(button).toBeDisabled();
    expect(button.closest("[data-tooltip]")?.getAttribute("data-tooltip")).toContain(
      "Steam's storage settings",
    );
    await act(async () => button.click());
    expect(state.openMovePanel).not.toHaveBeenCalled();
  });

  it("keeps the default aggregate move entry visible and explains missing local files", async () => {
    state.displayOperations = ["aggregate"];
    await show({ hasLocalPath: false });
    const item = [...container.querySelectorAll('[role="menuitem"]')].find((entry) =>
      entry.textContent?.includes("resource.operation.move"),
    );

    expect(item).toHaveAttribute("aria-disabled", "true");
    expect(item).toHaveTextContent("local");
  });

  it("allows a path-owned resource with Steam metadata and preserves its preview identity", async () => {
    await show({
      path: "/local/game",
      displayName: "Game",
      hasLocalPath: true,
      sourceLinks: [{ source: ResourceSource.PathMark }] as Resource["sourceLinks"],
      externalIdentities: [{ source: ResourceSource.Steam }],
    } as Partial<Resource>);
    const button = container.querySelector("button")!;

    expect(button).not.toBeDisabled();
    await act(async () => button.click());
    expect(state.openMovePanel).toHaveBeenCalledWith({
      resources: [{ id: 1, path: "/local/game", displayName: "Game" }],
      sourceTabId: "tab-one",
      sourceTabName: undefined,
    });
  });
});
