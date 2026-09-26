import type * as Api from "../api";

import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import DataSyncStatusIndicator from "../components/DataSyncStatusIndicator";
import { dataSyncApi, DataSyncRequestError } from "../api";
import { applyDataSyncHubData, useDataSyncStore } from "../stores/dataSync";

import { overview, status } from "./dataSyncFixtures";

import { ClientMode, DataSyncStatusLevel, RemoteAccessMode } from "@/sdk/constants";
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
  dataSyncApi: { overview: vi.fn() },
}));
// The design system's tooltip and button, reduced to what the indicator relies on.
vi.mock("@/components/bakaui", () => ({
  Tooltip: ({ children }: { children: React.ReactNode }) => <>{children}</>,
  Button: ({
    children,
    onPress,
    isIconOnly: _icon,
    ...rest
  }: {
    children: React.ReactNode;
    onPress?: () => void;
    isIconOnly?: boolean;
  } & Record<string, unknown>) => (
    <button type="button" {...rest} onClick={onPress}>
      {children}
    </button>
  ),
}));

const initialRemote = useRemoteAccessStore.getState();

function Where() {
  const location = useLocation();

  return <p data-testid="location">{location.pathname}</p>;
}

const show = () =>
  render(
    <MemoryRouter initialEntries={["/somewhere"]}>
      <DataSyncStatusIndicator />
      <Routes>
        <Route element={<Where />} path="*" />
      </Routes>
    </MemoryRouter>,
  );

const indicator = () => screen.queryByTestId("data-sync-indicator");

beforeEach(() => {
  vi.clearAllMocks();
  useDataSyncStore.getState().clear();
  useRemoteAccessStore.setState({
    initialized: true,
    context: "known",
    isLocal: true,
    clientMode: ClientMode.AllInOne,
    mode: RemoteAccessMode.Disabled,
  });
});
afterEach(() => {
  cleanup();
  useRemoteAccessStore.setState(initialRemote, true);
});

describe("the data sync status indicator", () => {
  it("is hidden while data sync is off", async () => {
    vi.mocked(dataSyncApi.overview).mockResolvedValue(
      overview({ status: status({ level: DataSyncStatusLevel.Off, links: 0 }) }),
    );
    show();
    await waitFor(() => expect(dataSyncApi.overview).toHaveBeenCalled());
    await waitFor(() => expect(useDataSyncStore.getState().overview).toBeDefined());
    expect(indicator()).toBeNull();
  });

  it("shows the status line, the dot and what needs you, and opens the page", async () => {
    vi.mocked(dataSyncApi.overview).mockResolvedValue(
      overview({ status: status({ level: DataSyncStatusLevel.NeedsYou, openItems: 12 }) }),
    );
    show();
    await waitFor(() => expect(indicator()).not.toBeNull());

    expect(indicator()).toHaveAccessibleName("dataSync.status.NeedsYou 12");
    expect(screen.getByTestId("data-sync-indicator-count")).toHaveTextContent("12");
    expect(screen.getByTestId("data-sync-indicator-dot")).toHaveAttribute("data-tone", "warning");
    expect(screen.queryByTestId("data-sync-indicator-elsewhere")).toBeNull();
    fireEvent.click(indicator()!);
    expect(screen.getByTestId("location")).toHaveTextContent("/data-sync");
  });

  it("shows a hollow bubble while other devices hold decisions", async () => {
    vi.mocked(dataSyncApi.overview).mockResolvedValue(
      overview({ status: status({ peersNeedingDecisions: 2 }) }),
    );
    show();
    await waitFor(() => expect(indicator()).not.toBeNull());

    expect(screen.getByTestId("data-sync-indicator-elsewhere")).toHaveTextContent("2");
    expect(screen.queryByTestId("data-sync-indicator-count")).toBeNull();
    expect(indicator()).toHaveAccessibleName(
      /^dataSync\.status\.InStep .+\. dataSync\.status\.peersNeedingDecisions 2$/,
    );
  });

  it("shows for a device that is only read, and says so", async () => {
    vi.mocked(dataSyncApi.overview).mockResolvedValue(
      overview({
        status: status({ links: 0, linksInStep: 0, lastSyncedAt: undefined, readers: 2 }),
      }),
    );
    show();
    await waitFor(() => expect(indicator()).not.toBeNull());

    expect(indicator()).toHaveAccessibleName("dataSync.status.level.ReadersOnly 2");
    expect(screen.getByTestId("data-sync-indicator-dot")).toHaveAttribute("data-tone", "success");
  });

  it("shows for a request that waits here, and says it beside what else it says", async () => {
    vi.mocked(dataSyncApi.overview).mockResolvedValue(
      overview({
        status: status({
          links: 0,
          linksInStep: 0,
          lastSyncedAt: undefined,
          pendingRequests: 1,
        }),
      }),
    );
    show();
    await waitFor(() => expect(indicator()).not.toBeNull());
    // Nothing else going on: the request is the line, said once.
    expect(indicator()).toHaveAccessibleName("dataSync.status.level.Requests 1");

    act(() => {
      applyDataSyncHubData("DataSyncStatus", status({ pendingRequests: 2 }));
    });
    expect(indicator()).toHaveAccessibleName(
      /^dataSync\.status\.InStep .+\. dataSync\.status\.level\.Requests 2$/,
    );
  });

  it("follows the hub's status pushes", async () => {
    vi.mocked(dataSyncApi.overview).mockResolvedValue(overview());
    show();
    await waitFor(() => expect(indicator()).not.toBeNull());
    expect(indicator()).toHaveAttribute("data-level", String(DataSyncStatusLevel.InStep));

    act(() => {
      expect(
        applyDataSyncHubData(
          "DataSyncStatus",
          status({ level: DataSyncStatusLevel.Paused, openItems: 3 }),
        ),
      ).toBe(true);
    });
    expect(indicator()).toHaveAttribute("data-level", String(DataSyncStatusLevel.Paused));
    expect(screen.getByTestId("data-sync-indicator-count")).toHaveTextContent("3");

    // Off again: hidden.
    act(() => {
      applyDataSyncHubData("DataSyncStatus", status({ level: DataSyncStatusLevel.Off }));
    });
    expect(indicator()).toBeNull();
    // Other keys, and malformed pushes, are not data sync's.
    expect(applyDataSyncHubData("BTask", {})).toBe(false);
    act(() => {
      expect(applyDataSyncHubData("DataSyncStatus", { nonsense: true })).toBe(true);
    });
    expect(useDataSyncStore.getState().status?.level).toBe(DataSyncStatusLevel.Off);
  });

  it("keeps the applied push for the pages showing definitions", () => {
    applyDataSyncHubData("DataSyncApplied", { kinds: ["customProperty"], localKeys: ["12"] });
    expect(useDataSyncStore.getState().lastApplied).toEqual({
      kinds: ["customProperty"],
      localKeys: ["12"],
    });
  });

  it("asks nothing in a window that may not use data sync", () => {
    useRemoteAccessStore.setState({
      isLocal: false,
      clientMode: ClientMode.RemoteBrowser,
      mode: RemoteAccessMode.Enabled,
    });
    show();
    expect(dataSyncApi.overview).not.toHaveBeenCalled();
    expect(indicator()).toBeNull();
  });

  it("goes away when the server refuses this window", async () => {
    useRemoteAccessStore.setState({ context: "unknown" });
    vi.mocked(dataSyncApi.overview).mockRejectedValue(
      new DataSyncRequestError("HostOnly", "refused", 403),
    );
    show();
    await waitFor(() => expect(useDataSyncStore.getState().reach).toBe("refused"));
    expect(indicator()).toBeNull();
  });
});
