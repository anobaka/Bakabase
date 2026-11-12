import { createInstance } from "i18next";
import { beforeEach, expect, it } from "vitest";
import { CloudDownloadSources } from "@/sdk/constants";
import cn from "@/locales/cn/components/acquire.json";
import en from "@/locales/en/components/acquire.json";
import { downloadMethodLabel } from "../methodLabels";

const i18n = createInstance();
beforeEach(async () => {
  await i18n.init({
    lng: "cn",
    resources: { cn: { translation: cn }, en: { translation: en } },
    parseMissingKeyHandler: (key) => key,
    interpolation: { escapeValue: false },
  });
});

it.each(CloudDownloadSources.flatMap((source) => source.methods))(
  "provides readable labels for catalog method $id in both languages",
  async (method) => {
    for (const language of ["cn", "en"]) {
      await i18n.changeLanguage(language);
      const label = downloadMethodLabel(method, i18n.t);
      expect(label).toBeTruthy();
      expect(label).not.toMatch(/^(?:acquire|cloudDownload)\./);
      expect(label).not.toBe(method.id);
    }
  },
);

it.each([
  ["baidu.pcs", "BaiduPCS-Go"],
  ["gofile.galleryDl", "gallery-dl"],
  ["custom.engine", "Custom download engine"],
])("preserves the runtime name when the method translation is absent: %s", (id, displayName) => {
  expect(downloadMethodLabel({ id, displayName }, i18n.t)).toBe(displayName);
});

it("accepts a translated catalog name and hides an unknown catalog translation token", () => {
  i18n.addResource("cn", "translation", "cloudDownload.method.fixture", "Fixture engine");
  expect(downloadMethodLabel({ id: "fixture", displayName: "cloudDownload.method.fixture" }, i18n.t))
    .toBe("Fixture engine");
  expect(downloadMethodLabel({ id: "fixture", displayName: "cloudDownload.method.missing" }, i18n.t))
    .toBe(cn["acquire.method.label"]);
});

it("uses the localized method name before the ordinary runtime display name", () => {
  expect(downloadMethodLabel({ id: "pikpak.share", displayName: "PikPak public share" }, i18n.t))
    .toBe(cn["acquire.methodName.pikpak.share"]);
});
