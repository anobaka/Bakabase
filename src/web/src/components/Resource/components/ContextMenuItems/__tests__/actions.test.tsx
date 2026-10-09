import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { ControlledMenu } from "@szhsin/react-menu";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ContextMenuItems from "..";

import { ResourceSource } from "@/sdk/constants";

const mocks = vi.hoisted(() => ({
  createPortal: vi.fn(),
  openFolder: vi.fn(),
  openMovePanel: vi.fn(),
  bulkDeleteResources: vi.fn(),
  onResourcesDeleted: vi.fn(),
}));

vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: mocks.createPortal }),
}));
vi.mock("@/hooks/useOpenResourceDirectory", () => ({
  useOpenResourceDirectory: () => ({ open: mocks.openFolder, label: "View folder location" }),
}));
vi.mock("@/stores/options", () => ({
  useUiOptionsStore: () => ({ data: { resource: { customContextMenuItems: [] } } }),
}));
vi.mock("@/stores/resourceMovePanel", () => ({ openMovePanel: mocks.openMovePanel }));
vi.mock("@/components/ResourceMovePanel/messages", () => ({
  useMoveReasonText: () => (reason: string) => reason,
}));
vi.mock("@/sdk/BApi", () => ({
  default: { resource: { bulkDeleteResources: mocks.bulkDeleteResources } },
}));
vi.mock("../BatchPlayMenuItems", () => ({ default: () => null }));
vi.mock("../PropertyValuePanel", () => ({ default: () => null }));
vi.mock("@/components/MediaLibraryMultiSelector", () => ({ default: () => null }));
vi.mock("@/components/CollectionMultiSelector", () => ({ default: () => null }));
vi.mock("@/components/ResourceTransferModal", () => ({ default: () => null }));
vi.mock("@/components/Resource/components/ResourceEnhancementsModal", () => ({
  default: () => null,
}));
vi.mock("@/components/Resource/components/BulkPropertyEditor", () => ({ default: () => null }));
vi.mock("@/components/Resource/components/DeleteResourceConfirmContent", () => ({
  default: () => null,
}));
vi.mock("@/components/Playlist", () => ({ PlaylistCollection: () => null }));
vi.mock("@/components/bakaui", () => ({
  Modal: () => null,
  Tooltip: ({ children }: any) => children,
  toast: {},
}));

const contextResource = { id: 42, displayName: "Resource title", path: "/nas/media/title" };

beforeEach(() => {
  vi.clearAllMocks();
  mocks.bulkDeleteResources.mockResolvedValue({ code: 0 });
  vi.stubGlobal(
    "ResizeObserver",
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  );
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const show = (blocked = false) =>
  render(
    <ControlledMenu anchorPoint={{ x: 100, y: 100 }} state="open">
      <ContextMenuItems
        contextResource={{
          ...contextResource,
          ...(blocked ? { sourceLinks: [{ source: ResourceSource.Steam }] } : {}),
        }}
        moveResourceIds={[42]}
        selectedResourceIds={[11, 42]}
        selectedResources={[
          { id: 11, path: "/nas/media/other" },
          { ...contextResource, sourceLinks: blocked ? [{ source: ResourceSource.Steam }] : [] },
        ]}
        sourceTabId="tab-one"
        onResourcesDeleted={mocks.onResourcesDeleted}
      />
    </ControlledMenu>,
  );

describe("resource context menu actions", () => {
  it("keeps folder opening scoped to the clicked resource within a larger selection", () => {
    show();
    expect(screen.getByText("Resource title")).toBeInTheDocument();
    expect(screen.getByText("resource.contextMenu.currentOnly")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("menuitem", { name: /View folder location/ }));
    expect(mocks.openFolder).toHaveBeenCalledExactlyOnceWith(contextResource);
  });

  it("keeps move targeting separate from the overall batch", () => {
    show();
    fireEvent.click(screen.getByRole("menuitem", { name: "resource.contextMenu.moveResource" }));
    expect(mocks.openMovePanel).toHaveBeenCalledWith({
      resources: [{ id: 42, path: "/nas/media/title", displayName: "Resource title" }],
      sourceTabId: "tab-one",
      sourceTabName: undefined,
    });
  });

  it("exposes the disabled move reason without enabling the action", () => {
    show(true);
    const item = screen.getByRole("menuitem", { name: /resource.contextMenu.moveResource/ });

    expect(item).toHaveAttribute("aria-disabled", "true");
    expect(item).toHaveTextContent("steamManaged");
    fireEvent.click(item);
    expect(mocks.openMovePanel).not.toHaveBeenCalled();
  });

  it.each([false, true])(
    "requires delete confirmation and preserves the file checkbox (%s)",
    async (deleteFiles) => {
      show();
      const deleteItem = screen.getByRole("menuitem", {
        name: "resource.contextMenu.deleteCountResources",
      });

      expect(deleteItem).toHaveClass("resource-context-menu__danger");
      fireEvent.click(deleteItem);
      expect(mocks.bulkDeleteResources).not.toHaveBeenCalled();
      const [, confirmation] = mocks.createPortal.mock.calls[0]!;

      expect(confirmation.footer.okProps.color).toBe("danger");
      if (deleteFiles) confirmation.children.props.onDeleteFilesChange(true);
      await act(async () => confirmation.onOk());
      expect(mocks.bulkDeleteResources).toHaveBeenCalledExactlyOnceWith({
        ids: [11, 42],
        deleteFiles,
      });
      expect(mocks.onResourcesDeleted).toHaveBeenCalledExactlyOnceWith([11, 42]);
    },
  );
});
