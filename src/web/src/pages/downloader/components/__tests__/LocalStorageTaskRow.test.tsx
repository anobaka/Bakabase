import { HeroUIProvider } from "@heroui/react";
import { render, screen } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import cn from "@/locales/cn/pages/downloader.json";
import en from "@/locales/en/pages/downloader.json";
import cnCloud from "@/locales/cn/pages/cloudDownload.json";
import enCloud from "@/locales/en/pages/cloudDownload.json";
import GeneralTaskRow from "../GeneralTaskRow";
import type { CenterTask } from "../../downloadCenter";

const state = vi.hoisted(() => ({ labels: {} as Record<string, string> }));
vi.mock("react-i18next", () => ({
  initReactI18next: { type: "3rdParty", init: vi.fn() },
  Trans: ({ children }: { children: React.ReactNode }) => children,
  useTranslation: () => ({
    t: (key: string, options?: { defaultValue?: string }) =>
      state.labels[key] ?? options?.defaultValue ?? key,
  }),
}));

const codes = [
  "WaitingForCloudStorageCapacity",
  "LOCAL_STORAGE_FULL",
  "LOCAL_STORAGE_RESERVE_LOW",
  "LOCAL_STORAGE_WRITE_FAILED",
  "LOCAL_STORAGE_ACCESS_DENIED",
  "LOCAL_STORAGE_UNAVAILABLE",
];
const task: CenterTask = {
  id: "site:9024",
  providerTaskId: "9024",
  category: "site",
  provider: "baidu",
  name: "Retained download",
  status: "queued",
  outputDirectory: "/downloads/retained",
  createdAt: "2026-10-29",
  updatedAt: "2026-10-29",
  availableActions: ["start"],
  originKind: "workflow",
  files: [],
};

for (const [locale, labels, cloud] of [
  ["cn", cn, cnCloud],
  ["en", en, enCloud],
] as const) {
  it.each(codes)(
    `shows a localized ${locale} storage queue reason for %s without starting it`,
    (stage) => {
      state.labels = { ...labels, ...cloud };
      const action = vi.fn();
      render(
        <HeroUIProvider disableAnimation>
          <GeneralTaskRow
            task={{ ...task, stage }}
            onSelect={vi.fn()}
            onAction={action}
            onSource={vi.fn()}
            onFolder={vi.fn()}
          />
        </HeroUIProvider>,
      );
      expect(screen.getByText(state.labels[`downloadCenter.stage.${stage}`])).toBeVisible();
      expect(screen.queryByText(stage)).not.toBeInTheDocument();
      expect(action).not.toHaveBeenCalled();
    },
  );

  it(`localizes a ${locale} failed task without claiming legacy errors are disk-full evidence`, () => {
    state.labels = { ...labels, ...cloud };
    render(
      <HeroUIProvider disableAnimation>
        <GeneralTaskRow
          task={{ ...task, status: "failed", error: "BAIDU_OUTPUT_DIRECTORY_FAILED" }}
          onSelect={vi.fn()}
          onAction={vi.fn()}
          onSource={vi.fn()}
          onFolder={vi.fn()}
        />
      </HeroUIProvider>,
    );
    expect(
      screen.getByText(state.labels["cloudDownload.problemCode.BAIDU_OUTPUT_DIRECTORY_FAILED"]),
    ).toBeVisible();
  });
}
