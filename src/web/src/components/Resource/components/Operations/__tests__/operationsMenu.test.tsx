import type { Resource } from "@/core/models/Resource";
import type { CoverOperationsOptions } from "../coverOperations";

import { Children, cloneElement } from "react";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import Operations from "../index";
import { ResourceOwnerScopeProvider } from "@/features/management/resourceContext";
import BApi from "@/sdk/BApi";

const state = vi.hoisted(() => ({
  options: {} as CoverOperationsOptions,
  openFolder: vi.fn(),
  openMovePanel: vi.fn(),
  createPortal: vi.fn(),
  bulkDelete: vi.fn(),
  refresh: vi.fn(),
  pin: vi.fn(),
}));

vi.mock("react-i18next", () => ({
  useTranslation: () => ({ t: (key: string) => key, i18n: { language: "en" } }),
}));
vi.mock("@/stores/options", () => ({
  useUiOptionsStore: (select: any) => select({ data: { resource: state.options } }),
}));
vi.mock("@/stores/resourceMovePanel", () => ({ openMovePanel: state.openMovePanel }));
vi.mock("@/hooks/useOpenResourceDirectory", () => ({
  useOpenResourceDirectory: () => ({
    label: "View server folder location",
    open: state.openFolder,
  }),
}));
vi.mock("@/hooks/useResourceBrowserPlayer", () => ({ useResourceBrowserPlayer: () => vi.fn() }));
vi.mock("@/sdk/BApi", () => ({
  default: {
    resource: { bulkDeleteResources: state.bulkDelete, pinResource: state.pin },
    cache: { refreshResourceCache: state.refresh },
  },
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: state.createPortal }),
}));
vi.mock("@/components/Resource/components/ResourceEnhancementsModal.tsx", () => ({
  default: () => null,
}));
vi.mock("@/components/Resource/components/DeleteResourceConfirmContent", () => ({
  default: () => null,
}));
vi.mock("@/components/Playlist", () => ({ PlaylistCollection: () => null }));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, isDisabled, onPress, "aria-label": name }: any) => (
    <button aria-label={name} disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Tooltip: ({ children }: any) => children,
  Dropdown: ({ children }: any) => <div>{children}</div>,
  DropdownTrigger: ({ children }: any) => children,
  DropdownMenu: ({ children, onAction }: any) => (
    <div role="menu">
      {Children.map(children, (child: any) =>
        cloneElement(child, { action: () => onAction(child.key) }),
      )}
    </div>
  ),
  DropdownItem: ({ children, isDisabled, action }: any) => (
    <button disabled={isDisabled} role="menuitem" onClick={action}>
      {children}
    </button>
  ),
  Modal: () => null,
  toast: { success: vi.fn() },
}));

beforeEach(() => {
  state.options = {};
  vi.clearAllMocks();
  state.bulkDelete.mockResolvedValue({ code: 0 });
  state.pin.mockResolvedValue({ code: 0 });
  state.refresh.mockResolvedValue({ code: 0 });
});
afterEach(cleanup);

const resource = {
  id: 123,
  path: "/srv/downloaded/book",
  hasLocalPath: true,
  dataStates: [{}],
} as Resource;
const show = (value: Resource = resource) => render(<Operations resource={value} />);

describe("independent cover aggregate menu and shortcuts", () => {
  it("never sends a managed resource's numeric ID to the local move panel", () => {
    const owner = {
      serverId: "other",
      libraryEpoch: "epoch",
      name: "Other device",
      available: true,
      isLocal: false,
      contractVersion: 1,
    };

    render(
      <ResourceOwnerScopeProvider
        scope={{ source: owner, resourceId: resource.id, key: "other", api: BApi }}
      >
        <Operations resource={resource} />
      </ResourceOwnerScopeProvider>,
    );
    const move = screen.getByRole("menuitem", { name: /resource.operation.move/ });

    expect(move).toBeDisabled();
    fireEvent.click(move);
    expect(state.openMovePanel).not.toHaveBeenCalled();
  });
  it.each([
    {},
    { displayOperations: [] },
    { displayOperations: ["aggregate"] },
    { showAggregateOperations: true, displayOperations: [] },
  ])(
    "always includes the server-aware folder action with default/all-unchecked options: %j",
    (options) => {
      state.options = options;
      show();
      const folder = screen.getByRole("menuitem", { name: "View server folder location" });

      fireEvent.click(folder);
      expect(state.openFolder).toHaveBeenCalledWith(resource);
      expect(screen.getByRole("menuitem", { name: "common.action.delete" })).toBeEnabled();
      expect(screen.getByRole("menuitem", { name: "resource.action.refreshCache" })).toBeEnabled();
      expect(screen.getAllByRole("button")).toHaveLength(1);
    },
  );

  it("keeps all menu actions when just one shortcut is checked, and renders both together", () => {
    state.options = { showAggregateOperations: true, displayOperations: ["pin"] };
    show();
    expect(screen.getByRole("button", { name: "resource.operation.pin" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "resource.display.aggregateButton" })).toBeEnabled();
    expect(screen.getByRole("menuitem", { name: "View server folder location" })).toBeEnabled();
    expect(screen.getByRole("menuitem", { name: "resource.operation.preview" })).toBeEnabled();
    expect(screen.getByRole("menuitem", { name: "common.action.delete" })).toBeEnabled();
  });

  it("renders nothing when aggregation is off and no shortcut is selected, even with cache data", () => {
    state.options = { showAggregateOperations: false, displayOperations: [] };
    const view = show();

    expect(view.container).toBeEmptyDOMElement();
  });

  it("shows only chosen shortcuts when aggregation is off", async () => {
    state.options = {
      showAggregateOperations: false,
      displayOperations: ["openFolder", "refreshCache"],
    };
    show();
    expect(screen.queryByRole("menu")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "View server folder location" }));
    expect(state.openFolder).toHaveBeenCalledWith(resource);
    fireEvent.click(screen.getByRole("button", { name: "resource.action.refreshCache" }));
    await waitFor(() => expect(state.refresh).toHaveBeenCalledWith(123));
  });

  it("omits folder and preview without a path and retains disabled move guidance", () => {
    state.options = { showAggregateOperations: true, displayOperations: ["openFolder", "preview"] };
    show({ id: 123, hasLocalPath: false } as Resource);
    expect(
      screen.queryByRole("menuitem", { name: "View server folder location" }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("menuitem", { name: "resource.operation.preview" }),
    ).not.toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: "resource.operation.move" })).toBeDisabled();
    expect(screen.getAllByRole("button")).toHaveLength(1);
  });

  it("keeps deletion behind confirmation and passes the user's file-deletion choice", async () => {
    show();
    fireEvent.click(screen.getByRole("menuitem", { name: "common.action.delete" }));
    expect(state.bulkDelete).not.toHaveBeenCalled();
    const options = state.createPortal.mock.calls[0][1];

    expect(options.footer.actions).toEqual(["cancel", "ok"]);
    options.children.props.onDeleteFilesChange(true);
    await options.onOk();
    expect(state.bulkDelete).toHaveBeenCalledWith({ ids: [123], deleteFiles: true });
  });
});
