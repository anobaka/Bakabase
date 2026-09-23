import type { ComponentProps } from "react";

import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import EstimatedRemainingTime from "../EstimatedRemainingTime";

import { DownloadTaskStatus } from "@/sdk/constants";
import enDateTime from "@/locales/en/datetime.json";
import cnDateTime from "@/locales/cn/datetime.json";
import enDownloader from "@/locales/en/pages/downloader.json";
import cnDownloader from "@/locales/cn/pages/downloader.json";

let translations: Record<string, string> = {};

vi.mock("react-i18next", () => ({
  useTranslation: () => ({ t: (key: string) => translations[key] ?? key }),
}));

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});
afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

async function show(task: ComponentProps<typeof EstimatedRemainingTime>["task"]) {
  await act(async () => root.render(<EstimatedRemainingTime task={task} />));
}

describe.each([
  ["English", { ...enDateTime, ...enDownloader }, "Estimated remaining", "1d 1h 1m 1s"],
  ["Chinese", { ...cnDateTime, ...cnDownloader }, "预计剩余", "1天 1时 1分 1秒"],
] as const)("estimated remaining time in %s", (_language, locale, label, duration) => {
  it("shows localized days without wrapping at 24 hours", async () => {
    translations = locale;
    await show({ status: DownloadTaskStatus.Downloading, estimatedRemainingSeconds: 90061 });

    expect(container).toHaveTextContent(`${label} ${duration}`);
  });
});

describe("estimated remaining time", () => {
  it.each([
    [0, "0s"],
    [0.1, "1s"],
    [59.1, "1m"],
    [61, "1m 1s"],
    [3600, "1h"],
  ])("rounds %s seconds up to whole seconds (%s)", async (seconds, duration) => {
    translations = { ...enDateTime, ...enDownloader };
    await show({ status: DownloadTaskStatus.Downloading, estimatedRemainingSeconds: seconds });

    expect(container).toHaveTextContent(`Estimated remaining ${duration}`);
  });

  it.each([undefined, null, -1, Number.NaN, Number.POSITIVE_INFINITY])(
    "hides an unavailable or invalid estimate (%s)",
    async (seconds) => {
      await show({ status: DownloadTaskStatus.Downloading, estimatedRemainingSeconds: seconds });

      expect(container).toBeEmptyDOMElement();
    },
  );

  it.each([
    DownloadTaskStatus.Idle,
    DownloadTaskStatus.InQueue,
    DownloadTaskStatus.Starting,
    DownloadTaskStatus.Stopping,
    DownloadTaskStatus.Complete,
    DownloadTaskStatus.Failed,
    DownloadTaskStatus.Disabled,
  ])("removes a stale estimate when status changes to %s", async (status) => {
    translations = { ...enDateTime, ...enDownloader };
    await show({ status: DownloadTaskStatus.Downloading, estimatedRemainingSeconds: 60 });

    expect(container).toHaveTextContent("Estimated remaining 1m");
    await show({ status, estimatedRemainingSeconds: 60 });
    expect(container).toBeEmptyDOMElement();
  });
});
