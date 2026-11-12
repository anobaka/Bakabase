import { createInstance } from "i18next";
import { expect, it } from "vitest";
import cn from "@/locales/cn/pages/acquisition.json";
import en from "@/locales/en/pages/acquisition.json";
import cnAcquire from "@/locales/cn/components/acquire.json";
import enAcquire from "@/locales/en/components/acquire.json";
import cnCloud from "@/locales/cn/pages/cloudDownload.json";
import enCloud from "@/locales/en/pages/cloudDownload.json";
import { localBatchReasonLabel } from "../components/localBatchReasonLabel";

const languages = [
  { language: "cn", messages: cn, acquire: cnAcquire, cloud: cnCloud },
  { language: "en", messages: en, acquire: enAcquire, cloud: enCloud },
];
const translator = async (language: string, messages: Record<string, string>) => {
  const i18n = createInstance();
  await i18n.init({
    lng: language,
    fallbackLng: false,
    resources: { [language]: { translation: messages } },
    returnNull: false,
    returnObjects: false,
    interpolation: { escapeValue: false },
    parseMissingKeyHandler: (key) => key,
  });
  return i18n.t.bind(i18n);
};

it.each(languages)(
  "resolves inherited recovery messages with the real $language missing-key handler",
  async ({ language, messages, acquire }) => {
    const t = await translator(language, { ...acquire, ...messages });
    for (const code of [
      "RECOVERY_MANUAL_REVIEW",
      "RECOVERY_NODE_UNSUPPORTED",
      "RECOVERY_FILES_UNVERIFIED",
      "ACQUISITION_TASK_CHANGED",
    ]) {
      expect(localBatchReasonLabel(t, code)).toBe(
        messages[`acquisition.recovery.reason.${code}` as keyof typeof messages],
      );
      expect(localBatchReasonLabel(t, code)).not.toContain("acquisition.");
    }
  },
);

it.each(languages)(
  "reuses translated preview errors and labels all local batch codes in $language",
  async ({ language, messages, acquire }) => {
    const t = await translator(language, { ...acquire, ...messages });
    for (const [key, value] of Object.entries(messages).filter(([key]) =>
      key.startsWith("acquisition.localBatch.reason."),
    )) {
      const code = key.slice("acquisition.localBatch.reason.".length);
      expect(localBatchReasonLabel(t, code)).toBe(value);
    }
    expect(localBatchReasonLabel(t, "RECOVERY_PREVIEW_EXPIRED")).toBe(
      messages["acquisition.recovery.error.RECOVERY_PREVIEW_EXPIRED"],
    );
    expect(localBatchReasonLabel(t, "RECOVERY_OUTCOME_UNKNOWN")).toBe(
      messages["acquisition.recovery.error.RECOVERY_OUTCOME_UNKNOWN"],
    );
  },
);

it.each(languages)(
  "shows a meaningful generic explanation for unknown, absent and malformed $language reasons",
  async ({ language, messages, acquire }) => {
    const t = await translator(language, { ...acquire, ...messages });
    for (const code of [
      undefined,
      null,
      "",
      "FUTURE_PROVIDER_FAILURE",
      "undefined",
      "<unexpected response>",
      "x".repeat(200),
    ]) {
      expect(localBatchReasonLabel(t, code)).toBe(
        messages["acquisition.localBatch.reason.unknown"],
      );
      expect(localBatchReasonLabel(t, code)).not.toContain("acquisition.");
    }
  },
);

it("skips a missing or untranslated first namespace instead of leaking its key", async () => {
  const key = "acquisition.localBatch.reason.RECOVERY_NODE_UNSUPPORTED";
  const t = await translator("cn", { ...cn, [key]: key });
  expect(localBatchReasonLabel(t, "RECOVERY_NODE_UNSUPPORTED")).toBe(
    cn["acquisition.recovery.reason.RECOVERY_NODE_UNSUPPORTED"],
  );
});

it.each(languages)("reuses the precise cloud download problem in $language", async ({language, messages, cloud}) => {
  const t = await translator(language, {...messages, ...cloud});
  expect(localBatchReasonLabel(t, "BAIDU_DOWNLOAD_CHECKPOINT_INVALID")).toBe(
    cloud["cloudDownload.problemCode.BAIDU_DOWNLOAD_CHECKPOINT_INVALID"],
  );
  expect(localBatchReasonLabel(t, "BAIDU_DOWNLOAD_CHECKPOINT_INVALID")).not.toBe(
    messages["acquisition.localBatch.reason.unknown"],
  );
});
