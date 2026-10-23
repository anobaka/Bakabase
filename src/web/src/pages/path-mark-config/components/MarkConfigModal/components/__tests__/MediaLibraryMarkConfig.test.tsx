import type { ReactNode } from "react";
import type { MarkConfig } from "../../types";
import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";

import { useState } from "react";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import MediaLibraryMarkConfig from "../MediaLibraryMarkConfig";
import MarkDescription from "../../../MarkDescription";
import { parseMarkConfig } from "../../utils";

import { PathMarkType, PathMatchMode, PropertyValueType } from "@/sdk/constants";

const mocks = vi.hoisted(() => ({ list: vi.fn(), createPortal: vi.fn() }));
const t = (key: string) => key;

vi.mock("@/sdk/BApi", () => ({
  default: { mediaLibraryV2: { getAllMediaLibraryV2: mocks.list } },
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: mocks.createPortal }),
}));
vi.mock("@/components/MediaLibrarySelectorV2", () => ({ default: () => null }));
vi.mock("../DynamicValueConfig", () => ({ default: () => null }));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t }) }));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress }: { children: ReactNode; onPress: () => void }) => (
    <button onClick={onPress}>{children}</button>
  ),
  Chip: ({ children, onClose }: { children: ReactNode; onClose: () => void }) => (
    <span>
      {children}
      <button aria-label={`Remove ${children}`} onClick={onClose}>
        ×
      </button>
    </span>
  ),
  RadioGroup: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  Radio: ({ children }: { children: ReactNode }) => <span>{children}</span>,
}));

function Editor() {
  const [config, setConfig] = useState(() =>
    parseMarkConfig(
      JSON.stringify({
        matchMode: PathMatchMode.Layer,
        layer: 0,
        mediaLibraryId: 2,
        valueType: PropertyValueType.Fixed,
      }),
      PathMarkType.MediaLibrary,
    ),
  );

  return (
    <>
      <MediaLibraryMarkConfig
        config={config}
        t={t}
        updateConfig={(updates: Partial<MarkConfig>) =>
          setConfig((value) => ({ ...value, ...updates }))
        }
      />
      <output data-testid="config">{JSON.stringify(config)}</output>
    </>
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.list.mockResolvedValue({
    code: 0,
    data: [
      { id: 1, name: "Comics" },
      { id: 2, name: "Archive" },
    ],
  });
});
afterEach(cleanup);

describe("multiple fixed media library path-mark values", () => {
  it("opens an older single-library mark and can add, deduplicate and remove libraries", async () => {
    render(<Editor />);
    expect(await screen.findByText("Archive")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "pathMarkConfig.action.addMediaLibrary" }));
    await act(async () => mocks.createPortal.mock.lastCall![1].onSelect(1));
    expect(screen.getByText("Comics")).toBeInTheDocument();
    expect(JSON.parse(screen.getByTestId("config").textContent!)).toMatchObject({
      mediaLibraryId: 2,
      mediaLibraryIds: [2, 1],
    });
    fireEvent.click(screen.getByRole("button", { name: "pathMarkConfig.action.addMediaLibrary" }));
    await act(async () => mocks.createPortal.mock.lastCall![1].onSelect(1));
    expect(screen.getAllByText("Comics")).toHaveLength(1);
    fireEvent.click(screen.getByRole("button", { name: "Remove Archive" }));
    expect(JSON.parse(screen.getByTestId("config").textContent!)).toMatchObject({
      mediaLibraryId: 1,
      mediaLibraryIds: [1],
    });
    fireEvent.click(screen.getByRole("button", { name: "Remove Comics" }));
    expect(JSON.parse(screen.getByTestId("config").textContent!).mediaLibraryIds).toEqual([]);
    expect(JSON.parse(screen.getByTestId("config").textContent!)).not.toHaveProperty(
      "mediaLibraryId",
    );
  });

  it("shows all fixed library names in mark descriptions while retaining legacy single-library display", () => {
    const library = {
      id: 2,
      name: "Archive",
      paths: [],
      resourceCount: 0,
      syncMayBeOutdated: false,
    };
    const mark: BakabaseAbstractionsModelsDomainPathMark = {
      id: 1,
      path: "/media",
      priority: 0,
      syncStatus: 0,
      createdAt: "2026-10-23T00:00:00Z",
      updatedAt: "2026-10-23T00:00:00Z",
      isDeleted: false,
      type: PathMarkType.MediaLibrary,
      configJson: JSON.stringify({
        matchMode: PathMatchMode.Layer,
        layer: 0,
        valueType: PropertyValueType.Fixed,
      }),
      mediaLibrary: library,
    };
    const view = render(
      <MarkDescription
        label="Library"
        mark={{ ...mark, mediaLibraries: [library, { ...library, id: 1, name: "Comics" }] }}
      />,
    );

    expect(view.container).toHaveTextContent("[Library:Archive, Comics]");
    view.rerender(<MarkDescription label="Library" mark={mark} />);
    expect(view.container).toHaveTextContent("[Library:Archive]");
  });
});
