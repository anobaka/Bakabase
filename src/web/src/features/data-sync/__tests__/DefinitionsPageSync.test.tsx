import type * as Api from "../api";
import type { DataSyncEntityStatusView } from "../api";

import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import {
  DataSyncEmptyStateLine,
  DataSyncHeaderLink,
  DefinitionSyncRow,
  useDefinitionSync,
} from "../components/DefinitionsPageSync";
import { dataSyncApi } from "../api";
import { useDataSyncStore } from "../stores/dataSync";

import { status } from "./dataSyncFixtures";

import {
  ClientMode,
  DataSyncEntitySyncState,
  DataSyncHeldReason,
  DataSyncStatusLevel,
  RemoteAccessMode,
} from "@/sdk/constants";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

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

/*
 * What the Properties and Extension groups pages show of data sync (hooks H-props, H-ext): the
 * header link with its dot, a badge and the sync choices per row, the hint for a property too
 * large to sync whole, and the empty page's way to another device — only where this window may
 * administer the server it shows.
 */

const initialRemote = useRemoteAccessStore.getState();

const entity = (
  localKey: string,
  patch: Partial<DataSyncEntityStatusView> = {},
): DataSyncEntityStatusView => ({
  localKey,
  syncKey: `${localKey}`.padStart(32, "0"),
  state: DataSyncEntitySyncState.Synced,
  childrenLocal: false,
  localOnlyChildren: 0,
  heldChildren: 0,
  originNodeId: "node-nas",
  originName: "NAS",
  openItems: 0,
  differsFromSource: false,
  ...patch,
});

function PropertiesRows({ onApplied }: { onApplied?: () => void }) {
  const sync = useDefinitionSync("customProperty", onApplied);

  return (
    <>
      {sync.host}
      {["12", "13", "99"].map((key) => (
        <div key={key} data-row={key}>
          <DefinitionSyncRow
            offersDefinitionOnly
            kind="customProperty"
            localKey={key}
            name={`Property ${key}`}
            sync={sync}
          />
        </div>
      ))}
    </>
  );
}

const renderIn = (node: JSX.Element) => render(<MemoryRouter>{node}</MemoryRouter>);
const rowOf = (key: string) => document.querySelector<HTMLElement>(`[data-row="${key}"]`)!;

const asWindow = (window: "local" | "unrestricted" | "lan") =>
  useRemoteAccessStore.setState({
    initialized: true,
    context: "known",
    isLocal: window === "local",
    clientMode: window === "local" ? ClientMode.AllInOne : ClientMode.RemoteBrowser,
    mode:
      window === "unrestricted"
        ? RemoteAccessMode.Unrestricted
        : window === "lan"
          ? RemoteAccessMode.Enabled
          : RemoteAccessMode.Disabled,
  });

beforeEach(() => {
  vi.clearAllMocks();
  useDataSyncStore.getState().clear();
  asWindow("local");
  vi.mocked(dataSyncApi.entities).mockResolvedValue([
    entity("12", { openItems: 2 }),
    entity("13", { heldAtSource: DataSyncHeldReason.TooLarge, originName: undefined }),
  ]);
});
afterEach(() => {
  cleanup();
  useRemoteAccessStore.setState(initialRemote, true);
});

describe("the definitions pages", () => {
  it("link to data sync from the header, with the dot of how it is going", () => {
    useDataSyncStore
      .getState()
      .setStatus(status({ level: DataSyncStatusLevel.NeedsYou, openItems: 2 }));
    renderIn(<DataSyncHeaderLink />);

    const header = screen.getByTestId("data-sync-header-link");

    expect(header).toHaveAttribute("href", "/data-sync");
    expect(header).toHaveTextContent("dataSync.title");
    expect(screen.getByTestId("data-sync-header-dot")).toHaveAttribute("data-tone", "warning");
  });

  it("badge each definition, and offer its sync choices from the row", async () => {
    renderIn(<PropertiesRows />);
    await waitFor(() =>
      expect(within(rowOf("12")).getByTestId("data-sync-entity-badge")).toBeInTheDocument(),
    );

    expect(dataSyncApi.entities).toHaveBeenCalledWith("customProperty");
    expect(within(rowOf("12")).getByTestId("data-sync-entity-badge")).toHaveAttribute(
      "data-badge",
      "needsYou",
    );
    // A definition data sync does not know yet shows nothing.
    expect(rowOf("99")).toBeEmptyDOMElement();

    fireEvent.click(within(rowOf("12")).getByTestId("data-sync-entity-menu"));
    expect(
      within(rowOf("12"))
        .getAllByRole("menuitem")
        .map((item) => item.getAttribute("data-action")),
    ).toEqual(["keepLocal", "definitionOnlyOn", "detach"]);
    await act(async () => {
      fireEvent.click(within(rowOf("12")).getByText("dataSync.entity.action.keepLocal"));
    });
    expect(dataSyncApi.setEntitySync).toHaveBeenCalledWith("customProperty", "12", {
      state: DataSyncEntitySyncState.LocalOnly,
    });
  });

  it("offer to sync only the definition of a property with too many options", async () => {
    renderIn(<PropertiesRows />);
    await waitFor(() =>
      expect(screen.getByTestId("data-sync-too-many-options")).toBeInTheDocument(),
    );

    fireEvent.click(screen.getByTestId("data-sync-too-many-options"));
    const dialog = screen.getByRole("alertdialog");

    expect(dialog).toHaveTextContent("dataSync.entity.definitionOnly.everyDevice");
    await act(async () => {
      fireEvent.click(within(dialog).getByText("federation.confirm"));
    });
    expect(dataSyncApi.setEntitySync).toHaveBeenCalledWith("customProperty", "13", {
      childrenLocal: true,
    });
  });

  it("read the page's list again when an apply wrote definitions of its kind", async () => {
    const onApplied = vi.fn();

    renderIn(<PropertiesRows onApplied={onApplied} />);
    await waitFor(() => expect(dataSyncApi.entities).toHaveBeenCalledTimes(1));
    act(() =>
      useDataSyncStore.getState().setApplied({ kinds: ["extensionGroup"], localKeys: ["1"] }),
    );
    expect(onApplied).not.toHaveBeenCalled();
    act(() =>
      useDataSyncStore.getState().setApplied({ kinds: ["customProperty"], localKeys: ["12"] }),
    );
    expect(onApplied).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(dataSyncApi.entities).toHaveBeenCalledTimes(2));
  });

  it("point an empty Properties page to another device", () => {
    renderIn(<DataSyncEmptyStateLine />);

    const line = screen.getByTestId("data-sync-empty-state-line");

    expect(line).toHaveTextContent("customProperty.empty.syncHint");
    expect(within(line).getByRole("link")).toHaveAttribute("href", "/data-sync?add=1");
  });

  it("show all of it to an Unrestricted browser, which may administer the server", async () => {
    asWindow("unrestricted");
    renderIn(
      <>
        <DataSyncHeaderLink />
        <PropertiesRows />
      </>,
    );

    expect(screen.getByTestId("data-sync-header-link")).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.getAllByTestId("data-sync-entity-badge").length).toBeGreaterThan(0),
    );
  });

  it("show none of it to a browser that may not administer the server, and ask nothing", () => {
    asWindow("lan");
    renderIn(
      <>
        <DataSyncHeaderLink />
        <DataSyncEmptyStateLine />
        <PropertiesRows />
      </>,
    );

    expect(screen.queryByTestId("data-sync-header-link")).toBeNull();
    expect(screen.queryByTestId("data-sync-empty-state-line")).toBeNull();
    expect(screen.queryByTestId("data-sync-entity-badge")).toBeNull();
    expect(dataSyncApi.entities).not.toHaveBeenCalled();
  });
});
