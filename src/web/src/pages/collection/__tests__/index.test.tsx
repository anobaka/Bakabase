import type { ReactNode } from "react";
import type { Root } from "react-dom/client";
import type { CollectionModel } from "@/stores/collections";

import { act } from "react";
import { createRoot } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import CollectionPage from "..";

import { useCollectionsStore } from "@/stores/collections";

const { getAllCollections, navigate, createPortal } = vi.hoisted(() => ({
  getAllCollections: vi.fn(),
  navigate: vi.fn(),
  createPortal: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: { collection: { getAllCollections } },
}));

vi.mock("react-router-dom", () => ({
  useNavigate: () => navigate,
  useLocation: () => ({ pathname: "/collections" }),
}));

vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal }),
}));

// Keep the page and its Zustand subscription real. Replace UI boundaries so loading the
// collection list does not also initialize unrelated dialogs and property editors.
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress }: { children?: ReactNode; onPress?: () => void }) => (
    <button type="button" onClick={onPress}>
      {children}
    </button>
  ),
  Card: ({ children }: { children?: ReactNode }) => <article>{children}</article>,
  CardBody: ({ children }: { children?: ReactNode }) => <div>{children}</div>,
  Chip: ({ children }: { children?: ReactNode }) => <span>{children}</span>,
  ColorPicker: () => null,
  Input: ({
    placeholder,
    value,
    onValueChange,
  }: {
    placeholder?: string;
    value?: string;
    onValueChange?: (value: string) => void;
  }) => (
    <input
      placeholder={placeholder}
      value={value}
      onChange={(event) => onValueChange?.(event.target.value)}
    />
  ),
  Modal: () => null,
  Spinner: () => <div role="status">Loading collections</div>,
  Tooltip: ({ children }: { children?: ReactNode }) => <>{children}</>,
  toast: { success: vi.fn(), danger: vi.fn() },
}));

vi.mock("@/components/bakaui/components/ColorPicker", () => ({
  buildColorValueString: (color: string) => color,
}));

vi.mock("@/components/EditableValue", () => ({
  EditableValue: ({ value }: { value: string }) => <h2>{value}</h2>,
}));

vi.mock("@/components/StandardValue", () => ({
  serializeStandardValue: JSON.stringify,
}));

const collection = (id: number, name: string, order = 0): CollectionModel => ({
  id,
  name,
  order,
  autoAcquire: false,
  hasRule: false,
  createdAt: "2026-09-13T00:00:00",
  updatedAt: "2026-09-13T00:00:00",
});

const displayedNames = () =>
  Array.from(document.querySelectorAll("article h2"), (heading) => heading.textContent);

let container: HTMLDivElement;
let root: Root;

const renderPage = async () => {
  await act(async () => root.render(<CollectionPage />));
};

beforeEach(() => {
  (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
  vi.clearAllMocks();
  useCollectionsStore.setState({ collections: new Map(), loaded: false });
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
});

describe("CollectionPage", () => {
  it("finishes loading an empty list without entering a store snapshot render loop", async () => {
    let completeLoad!: (response: { data: CollectionModel[] }) => void;

    getAllCollections.mockReturnValueOnce(
      new Promise((resolve) => {
        completeLoad = resolve;
      }),
    );

    await renderPage();

    expect(container.querySelector('[role="status"]')).toHaveTextContent("Loading collections");
    await act(async () => completeLoad({ data: [] }));

    expect(container.querySelector('[role="status"]')).not.toBeInTheDocument();
    expect(container.querySelector("h2")).toHaveTextContent("collection.empty.title");
    expect(useCollectionsStore.getState().loaded).toBe(true);
    expect(getAllCollections).toHaveBeenCalledExactlyOnceWith({ withProgress: true });
  });

  it("renders collections by order and then id, and stays stable on a parent render", async () => {
    getAllCollections.mockResolvedValueOnce({
      data: [collection(3, "Later", 10), collection(2, "Second"), collection(1, "First")],
    });

    await renderPage();

    expect(displayedNames()).toEqual(["First", "Second", "Later"]);

    await renderPage();

    expect(displayedNames()).toEqual(["First", "Second", "Later"]);
    expect(getAllCollections).toHaveBeenCalledTimes(1);
  });

  it("reflects pushed additions, updates and removals without reloading the list", async () => {
    getAllCollections.mockResolvedValueOnce({ data: [collection(1, "Original", 10)] });

    await renderPage();
    expect(displayedNames()).toEqual(["Original"]);

    act(() => useCollectionsStore.getState().updateCollection(collection(2, "New", 0)));
    expect(displayedNames()).toEqual(["New", "Original"]);

    act(() => useCollectionsStore.getState().updateCollection(collection(1, "Renamed", -1)));
    expect(displayedNames()).toEqual(["Renamed", "New"]);

    act(() => useCollectionsStore.getState().removeCollection(1));
    expect(displayedNames()).toEqual(["New"]);

    act(() => useCollectionsStore.getState().removeCollection(2));
    expect(displayedNames()).toEqual([]);
    expect(container.querySelector("h2")).toHaveTextContent("collection.empty.title");
    expect(getAllCollections).toHaveBeenCalledTimes(1);
  });
});
