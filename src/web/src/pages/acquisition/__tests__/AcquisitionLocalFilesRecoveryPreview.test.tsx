import { cleanup, render, screen } from "@testing-library/react";
import { createInstance } from "i18next";
import { afterEach, beforeAll, expect, it, vi } from "vitest";
import AcquisitionLocalFilesRecoveryPreview from "../components/AcquisitionLocalFilesRecoveryPreview";
import cn from "@/locales/cn/pages/acquisition.json";
import en from "@/locales/en/pages/acquisition.json";

const i18n = createInstance();
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: i18n.t }) }));
beforeAll(async () => {
  await i18n.init({
    lng: "cn",
    fallbackLng: false,
    keySeparator: false,
    interpolation: { escapeValue: false },
    resources: { cn: { translation: cn }, en: { translation: en } },
  });
});
afterEach(cleanup);

it.each([
  [
    "cn",
    "原下载 #301 · 2 个文件",
    "原方案: /original/work",
    "当前默认: /current/work",
    "原方案: 未记录",
  ],
  [
    "en",
    "Original download #301 · 2 files",
    "Original plan: /original/work",
    "Current default: /current/work",
    "Original plan: Not recorded",
  ],
])(
  "shows translated source evidence and original/current directories in %s",
  async (language, evidence, oldWork, newWork, missing) => {
    await i18n.changeLanguage(language);
    const { container } = render(
      <AcquisitionLocalFilesRecoveryPreview
        preview={{
          supportsPrepareOnly: true,
          parts: [
            {
              partId: "main",
              downloadTaskId: 301,
              downloadResultId: 401,
              fingerprint: "private-verification-fingerprint",
              fileCount: 2,
              sourceDirectory: "/original/download",
              originalWorkingDirectory: "/original/work",
              workingDirectory: "/current/work",
              originalDeliveryDirectory: null,
              deliveryDirectory: "/current/delivery",
            },
          ],
        }}
      />,
    );
    expect(screen.getByText(evidence)).toBeVisible();
    expect(screen.getByText(oldWork)).toBeVisible();
    expect(screen.getByText(newWork)).toBeVisible();
    expect(screen.getByText(missing)).toBeVisible();
    expect(screen.getByText("/original/download")).toBeVisible();
    expect(container.textContent).toContain("/current/delivery");
    expect(container.textContent).not.toContain("private-verification-fingerprint");
    expect(container.textContent).not.toContain("acquisition.recovery.localFiles.");
    expect(container.querySelector("input,select,textarea,button")).toBeNull();
  },
);
