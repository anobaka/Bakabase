import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";

import {
  DownloadMethodAvailabilityNotice,
  DownloadMethodStatusChip,
  isDownloadMethodUnavailable,
} from "../DownloadMethodAvailability";

import cn from "@/locales/cn/pages/cloudDownload.json";
import en from "@/locales/en/pages/cloudDownload.json";

vi.mock("@/sdk/constants", () => ({
  CloudDownloadSources: [
    { providerId: "limited", methods: [{ id: "limited.cli" }] },
    { providerId: "mixed", methods: [{ id: "mixed.cli" }, { id: "mixed.http" }] },
    { providerId: "ready", methods: [{ id: "ready.http" }] },
  ],
  DownloadMethodAvailability: [
    {
      providerId: "limited",
      methodId: "limited.cli",
      status: "developing",
      descriptionKey: "limited.explanation",
    },
    {
      providerId: "mixed",
      methodId: "mixed.cli",
      status: "developing",
      descriptionKey: "mixed.explanation",
    },
  ],
}));
vi.mock("@/components/bakaui", () => ({
  Chip: ({ children }: any) => <span>{children}</span>,
  Tooltip: ({ children }: any) => <>{children}</>,
}));
afterEach(cleanup);

it("does not disable a provider that has another usable download method", () => {
  expect(isDownloadMethodUnavailable({ providerId: "limited" })).toBe(true);
  expect(isDownloadMethodUnavailable({ providerId: "mixed" })).toBe(false);
  expect(isDownloadMethodUnavailable({ methodId: "mixed.cli" })).toBe(true);
  expect(isDownloadMethodUnavailable({ methodId: "mixed.http" })).toBe(false);
  expect(isDownloadMethodUnavailable({ providerId: "ready" })).toBe(false);
  expect(isDownloadMethodUnavailable({ providerId: "unknown" })).toBe(false);
  render(<DownloadMethodStatusChip providerId="mixed" />);
  expect(screen.getByText("cloudDownload.integration.partial")).toBeInTheDocument();
});

it("displays the catalog explanation only for the affected method", () => {
  const { rerender } = render(<DownloadMethodAvailabilityNotice methodId="mixed.cli" />);

  expect(screen.getByRole("note")).toHaveTextContent("mixed.explanation");
  expect(screen.getByRole("note")).toHaveTextContent("cloudDownload.integration.status.developing");
  rerender(<DownloadMethodAvailabilityNotice methodId="mixed.http" />);
  expect(screen.queryByRole("note")).toBeNull();
});

it("has localized explanations separating authorization from download availability and share validity", () => {
  for (const translations of [cn, en]) {
    for (const key of [
      "cloudDownload.integration.unavailable",
      "cloudDownload.integration.partial",
      "cloudDownload.integration.status.developing",
      "cloudDownload.teraboxIntegrationUnavailable",
      "cloudDownload.problemCode.DOWNLOAD_METHOD_UNAVAILABLE",
    ])
      expect(translations[key as keyof typeof translations]).toBeTruthy();
  }
});
