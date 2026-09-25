import type * as Api from "../api";
import type { DataSyncEntityStatusView } from "../api";

import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import EntitySyncBadge from "../components/EntitySyncBadge";
import EntitySyncList from "../components/EntitySyncList";
import EntitySyncMenu from "../components/EntitySyncMenu";
import { dataSyncApi } from "../api";

import { recordingActions } from "./dataSyncFixtures";

import BApi from "@/sdk/BApi";
import { DataSyncEntitySyncState, PropertyType } from "@/sdk/constants";

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: Record<string, unknown>) =>
      options
        ? [
            key,
            ...Object.entries(options)
              .filter(([name, value]) => name !== "defaultValue" && value !== undefined)
              .map(([, value]) => String(value)),
          ].join(" ")
        : key,
    i18n: { language: "en", changeLanguage: vi.fn(), exists: () => false },
  }),
  initReactI18next: { type: "3rdParty", init: vi.fn() },
}));
vi.mock("../api", async (importOriginal) => ({
  ...(await importOriginal<typeof Api>()),
  dataSyncApi: {
    entities: vi.fn(),
    setEntitySync: vi.fn(async () => ({})),
  },
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    customProperty: { getAllCustomProperties: vi.fn() },
    extensionGroup: { getAllExtensionGroups: vi.fn() },
  },
}));

const entity = (localKey: string, patch: Partial<DataSyncEntityStatusView> = {}) => ({
  localKey,
  syncKey: `${localKey.padStart(32, "0")}`,
  state: DataSyncEntitySyncState.Synced,
  childrenLocal: false,
  localOnlyChildren: 0,
  heldChildren: 0,
  openItems: 0,
  differsFromSource: false,
  ...patch,
});

let recorded = recordingActions();

beforeEach(() => {
  vi.clearAllMocks();
  recorded = recordingActions();
});
afterEach(cleanup);

describe("how one definition syncs", () => {
  it("says it in a badge", () => {
    render(<EntitySyncBadge entity={entity("12", { originName: "NAS" })} />);

    expect(screen.getByTestId("data-sync-entity-badge")).toHaveTextContent(
      "dataSync.entity.badge.syncedFrom NAS",
    );
  });

  it("keeps it on this device, through the host", async () => {
    render(
      <EntitySyncMenu
        offersDefinitionOnly
        actions={recorded.actions}
        entity={entity("12")}
        kind="customProperty"
        name="Artist"
      />,
    );
    fireEvent.click(screen.getByTestId("data-sync-entity-menu"));
    const menu = screen.getByRole("menu");

    expect(
      within(menu)
        .getAllByRole("menuitem")
        .map((item) => item.dataset.action),
    ).toEqual(["keepLocal", "definitionOnlyOn", "detach"]);
    await act(async () => {
      fireEvent.click(within(menu).getByText("dataSync.entity.action.keepLocal"));
    });
    expect(recorded.actions.run).toHaveBeenCalledTimes(1);
    expect(dataSyncApi.setEntitySync).toHaveBeenCalledWith("customProperty", "12", {
      state: DataSyncEntitySyncState.LocalOnly,
    });
  });

  it("asks before its options stop or start syncing, saying it applies on every device", async () => {
    render(
      <EntitySyncMenu
        offersDefinitionOnly
        actions={recorded.actions}
        entity={entity("12", { childrenLocal: true })}
        kind="customProperty"
        name="Artist"
      />,
    );
    fireEvent.click(screen.getByTestId("data-sync-entity-menu"));
    fireEvent.click(screen.getByText("dataSync.entity.action.definitionOnlyOff"));
    const confirmation = recorded.confirmations[0];

    expect(confirmation).toMatchObject({
      title: "dataSync.entity.definitionOnly.offTitle Artist",
      description: "dataSync.entity.definitionOnly.offDescription",
    });
    await confirmation.action();
    expect(dataSyncApi.setEntitySync).toHaveBeenCalledWith("customProperty", "12", {
      childrenLocal: false,
    });
  });

  it("closes its menu with Escape, and only its menu", () => {
    const outer = vi.fn();

    render(
      // eslint-disable-next-line jsx-a11y/no-static-element-interactions
      <div onKeyDown={outer}>
        <EntitySyncMenu
          actions={recorded.actions}
          entity={entity("12")}
          kind="customProperty"
          name="Artist"
          offersDefinitionOnly={false}
        />
      </div>,
    );
    fireEvent.click(screen.getByTestId("data-sync-entity-menu"));
    fireEvent.keyDown(screen.getByRole("menu"), { key: "Escape" });
    expect(screen.queryByRole("menu")).toBeNull();
    expect(outer).not.toHaveBeenCalled();
    expect(screen.getByTestId("data-sync-entity-menu")).toHaveFocus();
  });
});

describe("every definition and how it syncs", () => {
  it("names each by its id, and offers the definition only for properties with options", async () => {
    vi.mocked(dataSyncApi.entities).mockResolvedValue([
      entity("12"),
      entity("13", { state: DataSyncEntitySyncState.LocalOnly }),
    ]);
    vi.mocked(BApi.customProperty.getAllCustomProperties).mockResolvedValue({
      code: 0,
      data: [
        { id: 12, name: "Artist", type: PropertyType.Tags },
        { id: 13, name: "Rating note", type: PropertyType.SingleLineText },
      ],
    } as never);
    render(<EntitySyncList actions={recorded.actions} version={1} />);

    await waitFor(() => expect(screen.getByText("Artist")).toBeInTheDocument());
    expect(dataSyncApi.entities).toHaveBeenCalledWith("customProperty");
    expect(screen.getByText("Rating note")).toBeInTheDocument();
    fireEvent.click(
      within(screen.getByText("Artist").closest("li")!).getByTestId("data-sync-entity-menu"),
    );
    expect(screen.getByText("dataSync.entity.action.definitionOnlyOn")).toBeInTheDocument();

    // Only those not synced in full.
    fireEvent.click(screen.getByLabelText("dataSync.entity.onlyApart"));
    expect(screen.queryByText("Artist")).toBeNull();
    expect(screen.getByText("Rating note")).toBeInTheDocument();
  });

  it("reads extension groups by theirs", async () => {
    vi.mocked(dataSyncApi.entities).mockResolvedValue([entity("3")]);
    vi.mocked(BApi.customProperty.getAllCustomProperties).mockResolvedValue({
      code: 0,
      data: [],
    } as never);
    vi.mocked(BApi.extensionGroup.getAllExtensionGroups).mockResolvedValue({
      code: 0,
      data: [{ id: 3, name: "Archives" }],
    } as never);
    render(<EntitySyncList actions={recorded.actions} version={1} />);
    fireEvent.click(screen.getByRole("tab", { name: "dataSync.kind.extensionGroup" }));

    await waitFor(() => expect(screen.getByText("Archives")).toBeInTheDocument());
    expect(dataSyncApi.entities).toHaveBeenLastCalledWith("extensionGroup");
  });
});
