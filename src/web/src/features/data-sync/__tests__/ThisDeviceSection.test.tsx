import type { TFunction } from "i18next";
import type * as Api from "../api";

import i18next from "i18next";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ThisDeviceSection, { readerStateText } from "../components/ThisDeviceSection";
import { useDataSyncActions } from "../hooks/useDataSyncActions";
import { dataSyncApi } from "../api";

import { NOW, overview, reader } from "./dataSyncFixtures";

import { RemoteAccessMode } from "@/sdk/constants";
import en from "@/locales/en/pages/dataSync.json";
import cn from "@/locales/cn/pages/dataSync.json";

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
    setSharing: vi.fn(async () => undefined),
    setAllPaused: vi.fn(),
    revokeReader: vi.fn(),
  },
}));

beforeEach(() => vi.clearAllMocks());
afterEach(cleanup);

/** Every word a reader can declare (spec §7.5.6), as the server's `GetDeclaredState` words them. */
const declared = ["ok", "awaitingReview", "waitingForPeerReview", "paused:byUser", "needsYou:2"];

/**
 * i18n as the app sets it up: a key it lacks comes back as the key itself — which is how
 * "dataSync.readers.state.ok" once reached the page.
 */
const translator = async (lng: string, resources: Record<string, string>) => {
  const instance = i18next.createInstance();

  await instance.init({
    lng,
    fallbackLng: false,
    resources: { [lng]: { translation: resources } },
    interpolation: { escapeValue: false },
    parseMissingKeyHandler: (key: string) => key,
  });

  return instance.t as TFunction;
};

describe("the devices that read this device", () => {
  it.each([
    ["en", en],
    ["zh-CN", cn],
  ])("words every state a reader declares in %s, never as a key", async (lng, resources) => {
    const t = await translator(lng, resources);

    for (const state of declared) {
      const text = readerStateText(t, state);

      expect(text, state).toEqual(expect.any(String));
      expect(text, state).not.toContain("dataSync.");
    }
  });

  it("says the steady state of a reader as in step, and the one waiting here as waiting", async () => {
    const t = await translator("en", en);

    expect(readerStateText(t, "ok")).toBe(en["dataSync.readers.state.inStep"]);
    expect(readerStateText(t, "waitingForPeerReview")).toBe(
      en["dataSync.readers.state.waitingForPeerReview"],
    );
    expect(readerStateText(t, "needsYou:1")).toBe("1 waits for a decision there");
    expect(readerStateText(t, "needsYou:3")).toBe("3 wait for a decision there");
  });

  it("says nothing of a word it does not know, or of none", async () => {
    const t = await translator("en", en);

    expect(readerStateText(t, "somethingNewer")).toBeUndefined();
    expect(readerStateText(t, "")).toBeUndefined();
    expect(readerStateText(t, null)).toBeUndefined();
  });

  it("lists each reader with what it declared", () => {
    const Host = () => {
      const actions = useDataSyncActions(() => undefined);

      return (
        <ThisDeviceSection
          canManage
          actions={actions}
          now={NOW}
          overview={overview()}
          readers={[
            reader("node-a", "A", { state: "ok" }),
            reader("node-b", "B", { state: "waitingForPeerReview" }),
            reader("node-c", "C", { state: "needsYou:2" }),
            reader("node-d", "D", { state: "somethingNewer" }),
          ]}
          onCreateCode={vi.fn()}
          onRetryReaders={vi.fn()}
        />
      );
    };

    render(<Host />);
    const row = (nodeId: string) =>
      document.querySelector<HTMLElement>(`[data-reader="${nodeId}"]`)!;

    expect(row("node-a")).toHaveTextContent("dataSync.readers.state.inStep");
    expect(row("node-b")).toHaveTextContent("dataSync.readers.state.waitingForPeerReview");
    expect(row("node-c")).toHaveTextContent("dataSync.readers.state.needsYou 2");
    expect(row("node-d")).not.toHaveTextContent("somethingNewer");
    expect(row("node-d")).not.toHaveTextContent("dataSync.readers.state");
    expect(within(screen.getByTestId("data-sync-readers")).getAllByRole("listitem")).toHaveLength(
      4,
    );
  });
});

describe("this device's sharing while remote access is off", () => {
  /** The section with the page's confirmation: what it asks, and a way to answer yes. */
  const Host = ({
    sharingEnabled,
    onCreateCode = vi.fn(),
  }: {
    sharingEnabled: boolean;
    onCreateCode?: () => void;
  }) => {
    const actions = useDataSyncActions(() => undefined);

    return (
      <>
        <ThisDeviceSection
          canManage
          actions={actions}
          now={NOW}
          overview={overview({ sharingEnabled, remoteAccessMode: RemoteAccessMode.Disabled })}
          readers={[]}
          onCreateCode={onCreateCode}
          onRetryReaders={vi.fn()}
        />
        {actions.confirmation && (
          <div data-testid="confirmation">
            <p data-testid="confirmation-title">{actions.confirmation.title}</p>
            <p data-testid="confirmation-warning">{actions.confirmation.warning}</p>
            <button type="button" onClick={actions.confirmCurrent}>
              yes
            </button>
          </div>
        )}
      </>
    );
  };

  it("offers to turn remote access on where another device's status sends the reader", async () => {
    render(<Host sharingEnabled />);

    // The switch is on: the hint no longer says what turning it on would do.
    expect(screen.getByTestId("data-sync-sharing-switch")).toBeChecked();
    expect(document.getElementById("data-sync-sharing-hint")).not.toHaveTextContent(
      "dataSync.sharing.remoteAccess",
    );
    expect(screen.getByTestId("data-sync-remote-access-off")).toHaveTextContent(
      "dataSync.remoteAccess.offLine",
    );
    fireEvent.click(screen.getByTestId("data-sync-remote-access-on"));
    expect(screen.getByTestId("confirmation-title")).toHaveTextContent(
      "dataSync.remoteAccess.onTitle",
    );
    expect(screen.getByTestId("confirmation-warning")).toHaveTextContent(
      "dataSync.remoteAccess.onWarning",
    );
    expect(dataSyncApi.setSharing).not.toHaveBeenCalled();
    await act(async () => {
      fireEvent.click(screen.getByText("yes"));
    });
    // The server turns remote access on, with pairing required, only from off.
    expect(dataSyncApi.setSharing).toHaveBeenCalledWith({
      enabled: true,
      enablePairedRemoteAccess: true,
    });
  });

  it("offers a code that turns remote access on first, then shows it", async () => {
    const onCreateCode = vi.fn();

    render(<Host sharingEnabled onCreateCode={onCreateCode} />);
    const code = screen.getByTestId("data-sync-create-code");

    expect(code).not.toBeDisabled();
    fireEvent.click(code);
    expect(onCreateCode).not.toHaveBeenCalled();
    expect(screen.getByTestId("confirmation-title")).toHaveTextContent(
      "dataSync.remoteAccess.onTitle",
    );
    // Only what is off is said: sharing is on already.
    expect(screen.getByTestId("confirmation-warning")).toHaveTextContent(
      /^dataSync\.sharing\.remoteAccess$/,
    );
    await act(async () => {
      fireEvent.click(screen.getByText("yes"));
    });
    expect(dataSyncApi.setSharing).toHaveBeenCalledWith({
      enabled: true,
      enablePairedRemoteAccess: true,
    });
    await waitFor(() => expect(onCreateCode).toHaveBeenCalledTimes(1));
  });

  it("says what turning sharing on also turns on, only while it is off", () => {
    render(<Host sharingEnabled={false} />);

    expect(document.getElementById("data-sync-sharing-hint")).toHaveTextContent(
      "dataSync.sharing.remoteAccess",
    );
    expect(screen.queryByTestId("data-sync-remote-access-off")).toBeNull();
  });
});
