import { createInstance } from "i18next";
import { I18nextProvider, initReactI18next } from "react-i18next";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { activityErrorCodes, activityErrorText } from "../activityErrors";
import ActivityErrorNotice from "../components/ActivityErrorNotice";

import cn from "@/locales/cn/pages/dataSync.json";
import en from "@/locales/en/pages/dataSync.json";
import { DataSyncPeerErrorCodeLabel, DataSyncPauseReasonLabel } from "@/sdk/constants";

vi.unmock("react-i18next");

afterEach(cleanup);

async function translations(lng: string) {
  const i18n = createInstance();

  await i18n.use(initReactI18next).init({
    lng,
    fallbackLng: "en",
    keySeparator: false,
    resources: { cn: { translation: cn }, en: { translation: en } },
  });

  return i18n;
}

describe("localized sync errors", () => {
  it.each(["cn", "en"])(
    "covers known errors in %s without translating raw diagnostics",
    async (language) => {
      const i18n = await translations(language);
      const dictionary = language === "cn" ? cn : en;

      for (const engine of ["metadata", "processing"] as const) {
        for (const code of activityErrorCodes[engine]) {
          const key = `dataSync.activity.error.${engine}.${code}`;

          expect(dictionary, key).toHaveProperty(key);
          expect(activityErrorText(i18n.t, { engine, errorCode: code, error: "RAW STACK" })).toBe(
            dictionary[key as keyof typeof dictionary],
          );
        }
      }
      for (const code of [
        ...Object.values(DataSyncPeerErrorCodeLabel),
        ...Object.values(DataSyncPauseReasonLabel),
      ]) {
        const message = activityErrorText(i18n.t, {
          engine: "definitions",
          errorCode: code,
          sourceName: "NAS",
        });

        expect(message).not.toContain("dataSync.");
        expect(message).not.toContain("{{");
        expect(message).not.toBe(i18n.t("dataSync.activity.error.unknown"));
      }
    },
  );

  it("keeps same-looking codes scoped to the correct engine and unknown values out of copy", async () => {
    const { t } = await translations("cn");
    const unknown = t("dataSync.activity.error.unknown");

    for (const code of ["Unreachable", "HttpRequestException", "__proto__", "<img src=x>"]) {
      expect(activityErrorText(t, { engine: "metadata", errorCode: code })).toBe(unknown);
    }
    expect(activityErrorText(t, { engine: "definitions", errorCode: "Unreachable" })).toBe(
      "无法连接另一台设备",
    );
    // A generic metadata failure can be network, I/O or invalid data; do not assert a precise cause.
    expect(activityErrorText(t, { engine: "metadata", errorCode: "MetadataSyncFailed" })).toBe(
      cn["dataSync.activity.error.metadata.MetadataSyncFailed"],
    );
  });

  it("understands nested read-back codes and restore pauses", async () => {
    const { t } = await translations("cn");

    expect(
      activityErrorText(t, {
        engine: "definitions",
        errorCode: "ReadBackFailed",
        error: "AccessMissing",
        sourceName: "NAS",
      }),
    ).toBe(
      t("dataSync.status.ReadBackFailed", {
        name: "NAS",
        reason: t("dataSync.peerError.AccessMissing"),
      }),
    );
    expect(
      activityErrorText(t, {
        engine: "definitions",
        errorCode: "ReadBackFailed",
        error: "FutureCode",
        sourceName: "NAS",
      }),
    ).not.toContain("FutureCode");
    expect(
      activityErrorText(t, {
        engine: "definitions",
        errorCode: "PeerReset",
        error: "restored",
        sourceName: "NAS",
      }),
    ).toBe(t("dataSync.status.paused.PeerResetRestored", { name: "NAS" }));
  });

  it("starts with plain-language copy and collapsed diagnostics, including code-only failures", async () => {
    const i18n = await translations("cn");

    render(
      <I18nextProvider i18n={i18n}>
        <ActivityErrorNotice
          engine="processing"
          error="RAW <b>diagnostic</b>"
          errorCode="PathNotMapped"
        />
      </I18nextProvider>,
    );
    const notice = screen.getByTestId("sync-error-explanation");
    const details = screen.getByTestId("sync-error-technical");

    expect(notice.querySelector("p")).toHaveTextContent(
      cn["dataSync.activity.error.processing.PathNotMapped"],
    );
    expect(notice.querySelector("p")).not.toHaveTextContent("RAW");
    expect(details).not.toHaveAttribute("open");
    fireEvent.click(screen.getByText(cn["dataSync.error.technical"]));
    expect(details).toHaveAttribute("open");
    expect(details).toHaveTextContent("PathNotMapped");
    expect(details).toHaveTextContent("RAW <b>diagnostic</b>");
    expect(details.querySelector("b")).toBeNull();
  });

  it("does not create an error notice for a successful row", async () => {
    const i18n = await translations("cn");
    const { container } = render(
      <I18nextProvider i18n={i18n}>
        <ActivityErrorNotice engine="metadata" />
      </I18nextProvider>,
    );

    expect(container).toBeEmptyDOMElement();
  });
});
