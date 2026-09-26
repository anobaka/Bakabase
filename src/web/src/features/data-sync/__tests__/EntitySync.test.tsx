import type * as Api from "../api";
import type { DataSyncEntityStatusView } from "../api";

import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import EntitySyncBadge from "../components/EntitySyncBadge";
import EntitySyncList from "../components/EntitySyncList";
import EntitySyncMenu from "../components/EntitySyncMenu";
import { dataSyncApi } from "../api";

import { recordingActions } from "./dataSyncFixtures";
import EscapableDetails from "./EscapableDetails";

import BApi from "@/sdk/BApi";
import { DataSyncEntitySyncState, DataSyncHeldReason, PropertyType } from "@/sdk/constants";

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
    const closeDetails = vi.fn();

    render(
      // eslint-disable-next-line jsx-a11y/no-static-element-interactions
      <div onKeyDown={outer}>
        {/* Details that close on Escape by a listener of their own, as the page's do. */}
        <EscapableDetails onEscape={closeDetails}>
          <EntitySyncMenu
            actions={recorded.actions}
            entity={entity("12")}
            kind="customProperty"
            name="Artist"
            offersDefinitionOnly={false}
          />
        </EscapableDetails>
      </div>,
    );
    fireEvent.click(screen.getByTestId("data-sync-entity-menu"));
    fireEvent.keyDown(screen.getByRole("menu"), { key: "Escape" });
    expect(screen.queryByRole("menu")).toBeNull();
    expect(outer).not.toHaveBeenCalled();
    expect(closeDetails).not.toHaveBeenCalled();
    expect(screen.getByTestId("data-sync-entity-menu")).toHaveFocus();
  });

  it("is a menu to the keyboard: focus on its first item, arrows between them, Tab out", () => {
    render(
      <EntitySyncMenu
        offersDefinitionOnly
        actions={recorded.actions}
        entity={entity("12")}
        kind="customProperty"
        name="Artist"
      />,
    );
    const trigger = screen.getByTestId("data-sync-entity-menu");

    act(() => trigger.focus());
    fireEvent.keyDown(trigger, { key: "ArrowDown" });
    const items = within(screen.getByRole("menu")).getAllByRole("menuitem");

    expect(items[0]).toHaveFocus();
    expect(items.every((item) => item.getAttribute("tabindex") === "-1")).toBe(true);
    fireEvent.keyDown(items[0], { key: "ArrowUp" });
    expect(items[items.length - 1]).toHaveFocus();
    fireEvent.keyDown(items[items.length - 1], { key: "Home" });
    expect(items[0]).toHaveFocus();
    fireEvent.keyDown(items[0], { key: "Tab" });
    expect(screen.queryByRole("menu")).toBeNull();
    expect(trigger).toHaveFocus();
  });

  it("says why a definition is held back here, and never asks for an update that would not help", () => {
    render(
      <EntitySyncBadge entity={entity("12", { heldAtSource: DataSyncHeldReason.TooLarge })} />,
    );
    expect(screen.getByTestId("data-sync-entity-badge")).toHaveTextContent(
      "dataSync.entity.badge.tooLarge",
    );
    expect(screen.getByTestId("data-sync-entity-badge")).toHaveAttribute(
      "title",
      "dataSync.entity.tooManyOptions",
    );
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
    // The kinds are buttons pressed one at a time, not tabs without a tab panel.
    expect(screen.queryAllByRole("tab")).toHaveLength(0);
    expect(screen.getByRole("group", { name: "dataSync.entity.kinds" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "dataSync.kind.customProperty" })).toHaveAttribute(
      "aria-pressed",
      "true",
    );
    fireEvent.click(screen.getByRole("button", { name: "dataSync.kind.extensionGroup" }));
    expect(screen.getByRole("button", { name: "dataSync.kind.extensionGroup" })).toHaveAttribute(
      "aria-pressed",
      "true",
    );

    await waitFor(() => expect(screen.getByText("Archives")).toBeInTheDocument());
    expect(dataSyncApi.entities).toHaveBeenLastCalledWith("extensionGroup");
  });
});
