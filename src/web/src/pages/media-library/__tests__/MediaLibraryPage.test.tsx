import type { ReactNode } from "react";
import type { ManagementSource } from "@/features/management/api";

import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { HeroUIProvider } from "@heroui/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import MediaLibraryPage from "../index";

import MediaLibraryFilterValue from "@/components/ResourceFilter/components/Filter/MediaLibraryFilterValue";
import { useUiOptionsStore } from "@/stores/options";
import { mediaLibraryChoiceValue } from "@/features/management/mediaLibraryFilter";
import { deserializeStandardValue } from "@/components/StandardValue/helpers";
import {
  InternalProperty,
  PropertyPool,
  SearchOperation,
  StandardValueType,
  PropertyType,
} from "@/sdk/constants";

const mocks = vi.hoisted(() => ({
  local: { list: vi.fn(), add: vi.fn(), patch: vi.fn(), remove: vi.fn() },
  remote: { list: vi.fn(), add: vi.fn(), patch: vi.fn(), remove: vi.fn() },
  sources: [] as ManagementSource[],
  refresh: vi.fn(),
  navigate: vi.fn(),
  pendingSearch: vi.fn(),
  createPortal: vi.fn(),
  orderPatch: vi.fn(),
  changed: vi.fn(),
  dragEnd: vi.fn(),
  toast: { danger: vi.fn(), success: vi.fn() },
}));

vi.mock("react-i18next", () => ({
  initReactI18next: { type: "3rdParty", init: vi.fn() },
  Trans: ({ children }: { children: ReactNode }) => children,
  useTranslation: () => ({
    t: (key: string, values?: Record<string, unknown>) =>
      key.endsWith("Named") || key === "mediaLibrary.optionOrder.drag"
        ? `${key}:${values?.name}`
        : key === "mediaLibrary.editor.color.option"
          ? `${key}:${values?.color}`
          : key,
    i18n: { language: "en" },
  }),
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    options: new Proxy({}, { get: () => mocks.orderPatch }),
    mediaLibraryV2: {
      getAllMediaLibraryV2: mocks.local.list,
      addMediaLibraryV2: mocks.local.add,
      patchMediaLibraryV2: mocks.local.patch,
      deleteMediaLibraryV2: mocks.local.remove,
    },
  },
}));
vi.mock("@dnd-kit/core", async () => ({
  ...(await vi.importActual("@dnd-kit/core")),
  DndContext: ({ children, onDragEnd }: any) => {
    mocks.dragEnd.mockImplementation(onDragEnd);

    return <>{children}</>;
  },
}));
vi.mock("@/components/ResourceFilter/components/Filter/FilterValueRenderer", () => ({
  default: ({ property, dbValue }: any) => {
    const choices = property.options.choices;

    return (
      <div data-testid="library-filter">
        {choices.map((choice: any) => (
          <span key={choice.value} data-filter-library>
            {choice.label}
          </span>
        ))}
        <output>{dbValue}</output>
      </div>
    );
  },
}));
vi.mock("react-router-dom", () => ({ useNavigate: () => mocks.navigate }));
vi.mock("@/stores/pendingSearch", () => ({
  usePendingSearchStore: (selector: (store: unknown) => unknown) =>
    selector({ setPendingSearch: mocks.pendingSearch }),
}));
vi.mock("@/features/management/useManagementSources", () => ({
  useManagementSources: () => ({ sources: mocks.sources, refresh: mocks.refresh }),
}));
vi.mock("@/features/management/DeviceDot", () => ({ default: () => null }));
vi.mock("@/features/management/resourceApi", () => ({
  createResourceApi: (source?: ManagementSource) => {
    const api = source?.isLocal === false ? mocks.remote : mocks.local;

    return {
      mediaLibraryV2: {
        getAllMediaLibraryV2: api.list,
        addMediaLibraryV2: api.add,
        patchMediaLibraryV2: api.patch,
        deleteMediaLibraryV2: api.remove,
      },
    };
  },
  subscribeResourceOwnerChanges: () => () => {},
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: mocks.createPortal }),
}));
vi.mock("@/components/HelpCenter", () => ({
  HelpCenterButton: () => <button>Help</button>,
}));
vi.mock("@/components/bakaui", async () => ({
  ...(await vi.importActual("@heroui/react")),
  toast: mocks.toast,
  Modal: ({ children, title }: { children: ReactNode; title: string }) => (
    <div aria-label={title} role="dialog">
      {children}
    </div>
  ),
  ColorPicker: ({ trigger }: { trigger: ReactNode }) => trigger,
}));

function source(serverId: string, available = true): ManagementSource {
  return {
    serverId,
    name: serverId,
    libraryEpoch: `epoch-${serverId}`,
    isLocal: serverId === "local",
    available,
    contractVersion: 1,
    metadata: {
      identity: { serverId, libraryEpoch: `epoch-${serverId}` },
      name: serverId,
      denied: false,
      data: {
        activeGeneration: "complete",
        activeSequence: 1,
        resourceCount: 10,
        stagedResourceCount: 0,
      },
      sync: { isOffline: !available, paused: false },
    },
  };
}

const libraries = [
  { id: 1, name: "Archive", resourceCount: 7, color: "#6366f1" },
  { id: 2, name: "Comics", resourceCount: 19, color: "#059669" },
  { id: 3, name: "Films", resourceCount: 0, color: "#2563eb" },
];

function renderPage() {
  return render(
    <HeroUIProvider disableAnimation>
      <MediaLibraryPage />
    </HeroUIProvider>,
  );
}

function browseButtons() {
  return screen.queryAllByRole("button", { name: /^mediaLibrary\.action\.browseNamed:/ });
}

function names() {
  return browseButtons().map((button) => button.getAttribute("aria-label")!.split(":")[1]);
}

async function loaded() {
  await waitFor(() => expect(browseButtons()).toHaveLength(3));
}

async function manage(name: string, action: "edit" | "delete") {
  await userEvent.click(
    screen.getByRole("button", { name: `mediaLibrary.action.manageNamed:${name}` }),
  );
  await userEvent.click(
    await screen.findByRole("menuitem", { name: `mediaLibrary.action.${action}` }),
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  useUiOptionsStore.setState({ data: {}, initialized: true });
  mocks.orderPatch.mockResolvedValue({ code: 0 });
  mocks.sources = [source("local")];
  mocks.local.list.mockResolvedValue({ code: 0, data: libraries });
  mocks.remote.list.mockResolvedValue({ code: 0, data: [] });
  mocks.local.add.mockResolvedValue({ code: 0 });
  mocks.local.patch.mockResolvedValue({ code: 0 });
  mocks.local.remove.mockResolvedValue({ code: 0 });
  mocks.remote.patch.mockResolvedValue({ code: 0 });
  mocks.remote.remove.mockResolvedValue({ code: 0 });
});

afterEach(cleanup);

describe("media library discovery and owner-aware resource entry", () => {
  it("combines name search and resource filters, then clears the complete filter state", async () => {
    renderPage();
    await loaded();
    expect(names()).toEqual(["Archive", "Comics", "Films"]);

    expect(screen.queryByLabelText("mediaLibrary.sort.label")).toBeNull();
    await userEvent.click(
      screen.getByRole("button", { name: /^mediaLibrary.filter.withResources/ }),
    );
    expect(names()).toEqual(["Archive", "Comics"]);
    fireEvent.change(screen.getByLabelText("mediaLibrary.search.label"), {
      target: { value: "  aRcHiVe  " },
    });
    expect(names()).toEqual(["Archive"]);

    await userEvent.click(screen.getByRole("button", { name: /^mediaLibrary.filter.empty/ }));
    expect(browseButtons()).toHaveLength(0);
    expect(screen.getByText("mediaLibrary.search.empty.title")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "mediaLibrary.action.clearSearch" }));
    expect(screen.getByLabelText("mediaLibrary.search.label")).toHaveValue("");
    expect(names()).toEqual(["Archive", "Comics", "Films"]);

    await userEvent.click(screen.getByRole("button", { name: /^mediaLibrary.filter.empty/ }));
    expect(names()).toEqual(["Films"]);
    await userEvent.click(screen.getByRole("button", { name: /^mediaLibrary.filter.all/ }));
    expect(names()).toEqual(["Archive", "Comics", "Films"]);
  });

  it("opens zero-resource libraries directly and retains separate owners for overlapping library IDs", async () => {
    mocks.sources = [source("local"), source("remote", false)];
    mocks.remote.list.mockResolvedValue({
      code: 0,
      data: [{ id: 1, name: "Remote archive", resourceCount: 0 }],
    });
    renderPage();
    await waitFor(() => expect(browseButtons()).toHaveLength(4));

    for (const [name, id, owner] of [
      ["Films", 3, "local"],
      ["Archive", 1, "local"],
      ["Remote archive", 1, "remote"],
    ] as const) {
      await userEvent.click(
        screen.getByRole("button", { name: `mediaLibrary.action.browseNamed:${name}` }),
      );
      const search = mocks.pendingSearch.mock.lastCall![0];

      expect(search).toMatchObject({
        owner: { serverId: owner, libraryEpoch: `epoch-${owner}` },
        deviceIds: [owner],
        page: 1,
        pageSize: 100,
      });
      expect(search.group.filters).toHaveLength(1);
      expect(search.group.filters[0]).toMatchObject({
        propertyPool: PropertyPool.Internal,
        propertyId: InternalProperty.MediaLibraryV2Multi,
        operation: SearchOperation.In,
        disabled: false,
      });
      expect(
        deserializeStandardValue(search.group.filters[0].dbValue, StandardValueType.ListString),
      ).toEqual([String(id)]);
      expect(mocks.navigate).toHaveBeenLastCalledWith("/resource");
    }
    expect(mocks.createPortal).not.toHaveBeenCalled();
    expect(
      screen.getByRole("button", { name: "mediaLibrary.action.manageNamed:Remote archive" }),
    ).toBeDisabled();
  });

  it("recovers an initial load error and keeps the last valid libraries after a failed refresh", async () => {
    mocks.local.list.mockResolvedValueOnce({ code: 500, message: "unavailable" });
    renderPage();
    expect(await screen.findByRole("alert")).toHaveTextContent("mediaLibrary.error.load");
    expect(browseButtons()).toHaveLength(0);
    await userEvent.click(screen.getByRole("button", { name: "mediaLibrary.action.retry" }));
    await loaded();
    expect(screen.queryByRole("alert")).toBeNull();

    mocks.local.list.mockRejectedValueOnce(new Error("offline"));
    await userEvent.click(screen.getByRole("button", { name: "mediaLibrary.action.refresh" }));
    await waitFor(() =>
      expect(screen.getByRole("alert")).toHaveTextContent("mediaLibrary.error.load"),
    );
    expect(names()).toEqual(["Archive", "Comics", "Films"]);
    await userEvent.click(screen.getByRole("button", { name: "mediaLibrary.action.retry" }));
    await waitFor(() => expect(screen.queryByRole("alert")).toBeNull());
  });
});

describe("media library creation and management", () => {
  it("validates a trimmed name, preserves input on API failure and saves the selected color before closing", async () => {
    renderPage();
    await loaded();
    await userEvent.click(screen.getByRole("button", { name: "mediaLibrary.action.add" }));
    const dialog = screen.getByRole("dialog", { name: "mediaLibrary.editor.createTitle" });
    const name = within(dialog).getByLabelText("mediaLibrary.editor.name.label");
    const create = within(dialog).getByRole("button", { name: "mediaLibrary.action.create" });

    expect(create).toBeDisabled();
    fireEvent.change(name, { target: { value: "   " } });
    fireEvent.blur(name);
    expect(create).toBeDisabled();
    expect(within(dialog).getByText("mediaLibrary.error.nameEmpty")).toBeInTheDocument();
    expect(mocks.local.add).not.toHaveBeenCalled();

    fireEvent.change(name, { target: { value: "  New collection  " } });
    await userEvent.click(
      within(dialog).getByRole("button", { name: "mediaLibrary.editor.color.option:#059669" }),
    );
    mocks.local.add.mockResolvedValueOnce({ code: 500, message: "not saved" });
    await userEvent.click(create);
    expect(await within(dialog).findByRole("alert")).toHaveTextContent("mediaLibrary.error.save");
    expect(name).toHaveValue("  New collection  ");
    expect(mocks.local.list).toHaveBeenCalledTimes(1);
    expect(mocks.local.add).toHaveBeenLastCalledWith({
      name: "New collection",
      color: "#059669",
      paths: [],
    });

    await userEvent.click(create);
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(mocks.local.add).toHaveBeenCalledTimes(2);
    expect(mocks.local.list).toHaveBeenCalledTimes(2);
  });

  it("submits only once and prevents closing while the create request is pending", async () => {
    let finish!: (value: { code: number }) => void;

    mocks.local.add.mockReturnValueOnce(
      new Promise((resolve) => {
        finish = resolve;
      }),
    );
    renderPage();
    await loaded();
    await userEvent.click(screen.getByRole("button", { name: "mediaLibrary.action.add" }));
    const dialog = screen.getByRole("dialog", { name: "mediaLibrary.editor.createTitle" });
    const name = within(dialog).getByLabelText("mediaLibrary.editor.name.label");
    const create = within(dialog).getByRole("button", { name: "mediaLibrary.action.create" });

    await act(async () => {
      fireEvent.change(name, { target: { value: "New archive" } });
      fireEvent.submit(dialog.querySelector("form")!);
      fireEvent.submit(dialog.querySelector("form")!);
    });
    expect(mocks.local.add).toHaveBeenCalledTimes(1);
    expect(name).toBeDisabled();
    expect(create).toBeDisabled();
    const cancel = within(dialog).getByRole("button", { name: "mediaLibrary.action.cancel" });

    expect(cancel).toBeDisabled();
    await userEvent.click(cancel);
    expect(dialog).toBeInTheDocument();
    await act(async () => finish({ code: 0 }));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(mocks.local.add).toHaveBeenCalledTimes(1);
  });

  it("sends edits to the owning server even when a local library has the same numeric ID", async () => {
    mocks.sources = [source("local"), source("remote")];
    mocks.remote.list.mockResolvedValue({
      code: 0,
      data: [{ id: 1, name: "Remote archive", color: "#0891b2", resourceCount: 2 }],
    });
    renderPage();
    await waitFor(() => expect(browseButtons()).toHaveLength(4));
    await manage("Remote archive", "edit");
    const dialog = screen.getByRole("dialog", { name: "mediaLibrary.editor.editTitle" });

    fireEvent.change(within(dialog).getByLabelText("mediaLibrary.editor.name.label"), {
      target: { value: "Remote revised" },
    });
    mocks.remote.patch.mockResolvedValueOnce({ code: 500, message: "not saved" });
    await userEvent.click(within(dialog).getByRole("button", { name: "mediaLibrary.action.save" }));
    expect(await within(dialog).findByRole("alert")).toHaveTextContent("mediaLibrary.error.save");
    expect(within(dialog).getByLabelText("mediaLibrary.editor.name.label")).toHaveValue(
      "Remote revised",
    );
    expect(mocks.remote.list).toHaveBeenCalledTimes(1);
    await userEvent.click(within(dialog).getByRole("button", { name: "mediaLibrary.action.save" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(mocks.remote.patch).toHaveBeenCalledWith(1, {
      name: "Remote revised",
      color: "#0891b2",
    });
    expect(mocks.remote.patch).toHaveBeenCalledTimes(2);
    expect(mocks.local.patch).not.toHaveBeenCalled();
    expect(mocks.pendingSearch).not.toHaveBeenCalled();
  });

  it("preserves an open edit when its device disconnects and rejects the stale mutation", async () => {
    mocks.sources = [source("local"), source("remote")];
    mocks.remote.list.mockResolvedValue({
      code: 0,
      data: [{ id: 1, name: "Remote archive", color: "#0891b2", resourceCount: 2 }],
    });
    const page = renderPage();

    await waitFor(() => expect(browseButtons()).toHaveLength(4));
    await manage("Remote archive", "edit");
    const dialog = screen.getByRole("dialog", { name: "mediaLibrary.editor.editTitle" });

    fireEvent.change(within(dialog).getByLabelText("mediaLibrary.editor.name.label"), {
      target: { value: "Unsaved rename" },
    });
    mocks.sources = [source("local"), source("remote", false)];
    page.rerender(
      <HeroUIProvider disableAnimation>
        <MediaLibraryPage />
      </HeroUIProvider>,
    );
    await waitFor(() =>
      expect(
        screen.getByRole("button", { name: "mediaLibrary.action.manageNamed:Remote archive" }),
      ).toBeDisabled(),
    );
    await userEvent.click(within(dialog).getByRole("button", { name: "mediaLibrary.action.save" }));
    expect(await within(dialog).findByRole("alert")).toHaveTextContent("mediaLibrary.error.save");
    expect(within(dialog).getByLabelText("mediaLibrary.editor.name.label")).toHaveValue(
      "Unsaved rename",
    );
    expect(mocks.remote.patch).not.toHaveBeenCalled();
    expect(mocks.local.patch).not.toHaveBeenCalled();
    expect(names()).toContain("Remote archive");
  });

  it("confirms deletion with the actual name and does not refresh or remove items on a rejected API response", async () => {
    renderPage();
    await loaded();
    await manage("Archive", "delete");
    expect(mocks.local.remove).not.toHaveBeenCalled();
    expect(mocks.pendingSearch).not.toHaveBeenCalled();
    const confirmation = mocks.createPortal.mock.lastCall![1];
    const contents = render(<>{confirmation.children}</>);

    expect(contents.getByText("mediaLibrary.confirm.deleteNamed:Archive")).toBeInTheDocument();
    expect(confirmation.footer.okProps.color).toBe("danger");
    mocks.local.remove.mockResolvedValueOnce({ code: 500, message: "delete denied" });
    await expect(confirmation.onOk()).rejects.toThrow();
    expect(mocks.local.list).toHaveBeenCalledTimes(1);
    expect(names()).toEqual(["Archive", "Comics", "Films"]);

    await act(async () => confirmation.onOk());
    expect(mocks.local.remove).toHaveBeenLastCalledWith(1);
    expect(mocks.local.list).toHaveBeenCalledTimes(2);
    expect(mocks.refresh).toHaveBeenCalled();
  });
});

describe("shared media-library order", () => {
  const key = (id: number, serverId = "local") => mediaLibraryChoiceValue(source(serverId), id);

  it("persists a drag without changing resource search and retains order after remount", async () => {
    const view = renderPage();

    await loaded();
    await act(async () => mocks.dragEnd({ active: { id: key(3) }, over: { id: key(1) } }));
    expect(mocks.orderPatch).toHaveBeenLastCalledWith({
      mediaLibraryOrder: { keys: [key(3), key(1), key(2)] },
    });
    expect(names()).toEqual(["Films", "Archive", "Comics"]);
    expect(mocks.pendingSearch).not.toHaveBeenCalled();
    view.unmount();
    renderPage();
    await loaded();
    expect(names()).toEqual(["Films", "Archive", "Comics"]);
    await userEvent.click(screen.getByRole("button", { name: "mediaLibrary.optionOrder.name" }));
    await waitFor(() => expect(names()).toEqual(["Archive", "Comics", "Films"]));
    expect(mocks.orderPatch).toHaveBeenLastCalledWith({
      mediaLibraryOrder: { keys: [key(1), key(2), key(3)] },
    });
  });

  it("preserves hidden positions on filtered drag and keeps failed changes unapplied", async () => {
    renderPage();
    await loaded();
    await userEvent.click(
      screen.getByRole("button", { name: /^mediaLibrary.filter.withResources/ }),
    );
    await act(async () => mocks.dragEnd({ active: { id: key(2) }, over: { id: key(1) } }));
    expect(mocks.orderPatch).toHaveBeenLastCalledWith({
      mediaLibraryOrder: { keys: [key(2), key(1), key(3)] },
    });
    expect(names()).toEqual(["Comics", "Archive"]);
    mocks.orderPatch.mockResolvedValueOnce({ code: 500 });
    await act(async () => mocks.dragEnd({ active: { id: key(1) }, over: { id: key(2) } }));
    expect(names()).toEqual(["Comics", "Archive"]);
    expect(screen.getByRole("alert")).toHaveTextContent("mediaLibrary.optionOrder.saveFailed");
    await userEvent.click(screen.getByRole("button", { name: /^mediaLibrary.filter.all/ }));
    expect(names()).toEqual(["Comics", "Archive", "Films"]);
  });

  it("shares owner-qualified keys for colliding library IDs and follows externally confirmed order", async () => {
    mocks.sources = [source("local"), source("remote", false)];
    mocks.remote.list.mockResolvedValue({
      code: 0,
      data: [{ id: 1, name: "Remote archive", resourceCount: 0 }],
    });
    renderPage();
    await waitFor(() => expect(browseButtons()).toHaveLength(4));
    act(() =>
      useUiOptionsStore
        .getState()
        .update({ mediaLibraryOrder: { keys: [key(1, "remote"), key(3), key(2), key(1)] } }),
    );
    expect(names()).toEqual(["Remote archive", "Films", "Comics", "Archive"]);
    await userEvent.click(screen.getByRole("button", { name: "mediaLibrary.optionOrder.name" }));
    await waitFor(() => expect(names()).toEqual(["Archive", "Comics", "Films", "Remote archive"]));
    expect(mocks.remote.patch).not.toHaveBeenCalled();
    expect(mocks.local.patch).not.toHaveBeenCalled();
  });
});

it("shows configured library order in native filters while restricting sorting controls to management", async () => {
  render(
    <HeroUIProvider disableAnimation>
      <MediaLibraryPage />
      <MediaLibraryFilterValue
        isEditing
        dbValue={JSON.stringify(["2"])}
        orderOwner={source("local")}
        property={{
          id: InternalProperty.MediaLibraryV2Multi,
          pool: PropertyPool.Internal,
          type: PropertyType.MultipleChoice,
          name: "Libraries",
          typeName: "",
          poolName: "",
          order: 0,
          dbValueType: StandardValueType.ListString,
          bizValueType: StandardValueType.ListString,
          options: {
            choices: libraries.map((library) => ({
              value: String(library.id),
              label: library.name,
            })),
          },
        }}
        onValueChange={mocks.changed}
      />
    </HeroUIProvider>,
  );
  await loaded();
  const filterNames = () =>
    [...screen.getByTestId("library-filter").querySelectorAll("[data-filter-library]")].map(
      (choice) => choice.textContent,
    );
  const key = (id: number) => mediaLibraryChoiceValue(source("local"), id);

  await act(async () => mocks.dragEnd({ active: { id: key(3) }, over: { id: key(1) } }));
  expect(names()).toEqual(["Films", "Archive", "Comics"]);
  expect(filterNames()).toEqual(names());
  expect(within(screen.getByTestId("library-filter")).queryByRole("button")).toBeNull();
  await userEvent.click(screen.getByRole("button", { name: "mediaLibrary.optionOrder.name" }));
  await waitFor(() => expect(names()).toEqual(["Archive", "Comics", "Films"]));
  expect(filterNames()).toEqual(names());
  expect(screen.getByTestId("library-filter").querySelector("output")).toHaveTextContent('["2"]');
  expect(mocks.changed).not.toHaveBeenCalled();
  expect(mocks.pendingSearch).not.toHaveBeenCalled();
});
