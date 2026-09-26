import type * as Api from "../api";

import { act, cleanup, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import DataSyncStatusIndicator from "../components/DataSyncStatusIndicator";
import { dataSyncApi } from "../api";
import { useDataSyncStore } from "../stores/dataSync";

import { overview, status } from "./dataSyncFixtures";

import { UIHubConnection } from "@/components/SignalR/UIHubConnection";
import { ClientMode, DataSyncStatusLevel, RemoteAccessMode } from "@/sdk/constants";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

/*
 * The UI hub delivers data sync's pushes (hook H-hub in `UIHubConnection.ts`): the status the
 * indicator and the map follow (DataSyncStatus), and what an apply wrote (DataSyncApplied), which
 * the Properties and Extension groups pages read again on.
 */

const { handlers, connection } = vi.hoisted(() => {
  const handlers = new Map<string, (...args: any[]) => void>();
  const connection = {
    state: "Disconnected",
    on: vi.fn((name: string, handler: (...args: any[]) => void) => handlers.set(name, handler)),
    onclose: vi.fn(),
    start: vi.fn(async () => {
      connection.state = "Connected";
    }),
    stop: vi.fn(async () => {
      connection.state = "Disconnected";
    }),
    send: vi.fn(async () => undefined),
  };

  return { handlers, connection };
});

vi.mock("@microsoft/signalr", () => ({
  HubConnectionState: { Disconnected: "Disconnected", Connected: "Connected" },
  LogLevel: { Information: "Information" },
  HubConnectionBuilder: class {
    withUrl() {
      return this;
    }
    configureLogging() {
      return this;
    }
    build() {
      return connection;
    }
  },
}));
vi.mock("@/components/utils", () => ({ buildLogger: () => () => undefined }));
vi.mock("@/stores/options", () => ({ optionsStores: {} }));
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: Record<string, unknown>) =>
      // How English joins the parts of a label, so a label reads as it will.
      (
        ({
          "dataSync.a11y.sentenceBreak": ". ",
          "dataSync.a11y.clauseBreak": ", ",
          "dataSync.a11y.listBreak": ", ",
        }) as Record<string, string | undefined>
      )[key] ??
      (options
        ? [
            key,
            ...Object.entries(options)
              .filter(([name, value]) => name !== "defaultValue" && value !== undefined)
              .map(([, value]) => String(value)),
          ].join(" ")
        : key),
    i18n: { language: "en", changeLanguage: vi.fn(), exists: () => false },
  }),
  initReactI18next: { type: "3rdParty", init: vi.fn() },
}));
vi.mock("../api", async (importOriginal) => ({
  ...(await importOriginal<typeof Api>()),
  dataSyncApi: { overview: vi.fn() },
}));
// The design system, reduced to what the indicator relies on.
vi.mock("@/components/bakaui", () => ({
  toast: {},
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
const push = (key: string, data: unknown) =>
  act(() => handlers.get("GetIncrementalData")!(key, data));

beforeEach(() => {
  vi.clearAllMocks();
  handlers.clear();
  connection.state = "Disconnected";
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

describe("data sync over the UI hub", () => {
  it("brings a pushed status to the indicator without reading the overview again", async () => {
    vi.mocked(dataSyncApi.overview).mockResolvedValue(
      overview({ status: status({ level: DataSyncStatusLevel.InStep, openItems: 0 }) }),
    );
    await act(async () => {
      render(
        <MemoryRouter>
          <UIHubConnection />
          <DataSyncStatusIndicator />
        </MemoryRouter>,
      );
    });
    await waitFor(() => expect(screen.getByTestId("data-sync-indicator")).toBeInTheDocument());
    expect(screen.queryByTestId("data-sync-indicator-count")).toBeNull();
    expect(dataSyncApi.overview).toHaveBeenCalledTimes(1);

    push("DataSyncStatus", status({ level: DataSyncStatusLevel.NeedsYou, openItems: 4 }));

    expect(screen.getByTestId("data-sync-indicator")).toHaveAccessibleName(
      "dataSync.indicator.label dataSync.status.NeedsYou 4",
    );
    expect(screen.getByTestId("data-sync-indicator-count")).toHaveTextContent("4");
    expect(dataSyncApi.overview).toHaveBeenCalledTimes(1);
  });

  it("keeps what an apply wrote for the pages that list definitions", async () => {
    await act(async () => {
      render(<UIHubConnection />);
    });

    push("DataSyncApplied", { kinds: ["customProperty"], localKeys: ["12", "13"] });

    expect(useDataSyncStore.getState().lastApplied).toEqual({
      kinds: ["customProperty"],
      localKeys: ["12", "13"],
    });
  });

  it("drops a malformed push and leaves the other keys to the hub's own stores", async () => {
    await act(async () => {
      render(<UIHubConnection />);
    });
    useDataSyncStore.getState().setStatus(status({ openItems: 2 }));

    push("DataSyncStatus", { level: "nonsense" });
    push("PathMark", { id: 1 });

    expect(useDataSyncStore.getState().status?.openItems).toBe(2);
    expect(useDataSyncStore.getState().lastApplied).toBeUndefined();
  });
});
