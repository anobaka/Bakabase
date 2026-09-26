import type { TFunction } from "i18next";
import type * as Api from "../api";

import i18next from "i18next";
import { cleanup, render, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import ThisDeviceSection, { readerStateText } from "../components/ThisDeviceSection";
import { useDataSyncActions } from "../hooks/useDataSyncActions";

import { NOW, overview, reader } from "./dataSyncFixtures";

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
  dataSyncApi: { setSharing: vi.fn(), setAllPaused: vi.fn(), revokeReader: vi.fn() },
}));

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
