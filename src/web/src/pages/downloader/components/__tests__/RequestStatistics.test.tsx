import type { ReactNode } from "react";

import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import RequestStatistics from "../RequestStatistics";

import { ThirdPartyId, ThirdPartyRequestResultType } from "@/sdk/constants";
import { useThirdPartyRequestStatisticsStore } from "@/stores/thirdPartyRequestStatistics";

const { createPortal } = vi.hoisted(() => ({ createPortal: vi.fn() }));

vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal, isDarkMode: false }),
}));
vi.mock("@/components/ThirdPartyIcon", () => ({ default: () => <span>Site icon</span> }));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress }: { children: ReactNode; onPress: () => void }) => (
    <button onClick={onPress}>{children}</button>
  ),
  Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
  Modal: () => null,
  Tooltip: ({ children }: { children: ReactNode }) => <>{children}</>,
}));
vi.mock("react-chartjs-2", () => ({
  Doughnut: ({ data }: { data: unknown }) => (
    <pre data-testid="traffic-chart-data">{JSON.stringify(data)}</pre>
  ),
}));

beforeEach(() => {
  createPortal.mockClear();
  useThirdPartyRequestStatisticsStore.getState().setStatistics([
    {
      id: ThirdPartyId.ExHentai,
      counts: { [ThirdPartyRequestResultType.Succeed]: 1 },
      receivedBytes: 1536,
    },
    {
      id: ThirdPartyId.Pixiv,
      counts: { [ThirdPartyRequestResultType.Succeed]: 1 },
      receivedBytes: 4096,
    },
  ]);
});

afterEach(() => {
  cleanup();
  useThirdPartyRequestStatisticsStore.getState().setStatistics([]);
});

describe("request statistics", () => {
  it("shows request counts and response traffic together without tabs or hover", () => {
    render(<RequestStatistics compact />);
    fireEvent.click(screen.getByRole("button"));

    const modal = createPortal.mock.calls[0][1];
    render(modal.children);

    expect(
      screen.getByRole("heading", { name: "downloader.label.requestCounts" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { name: "downloader.label.responseTraffic" }),
    ).toBeInTheDocument();
    expect(screen.queryByRole("tablist")).not.toBeInTheDocument();
    expect(
      within(screen.getByTestId(`request-source-${ThirdPartyId.ExHentai}`)).getByText("1"),
    ).toBeInTheDocument();
    expect(
      within(screen.getByTestId(`traffic-source-${ThirdPartyId.ExHentai}`)).getByText("1.50 KiB"),
    ).toBeInTheDocument();
    const trafficData = () => JSON.parse(screen.getByTestId("traffic-chart-data").textContent!);
    expect(trafficData().labels).toEqual(["Pixiv", "ExHentai"]);
    expect(trafficData().datasets[0].data).toEqual([4096, 1536]);

    act(() =>
      useThirdPartyRequestStatisticsStore.getState().updateStatistics([
        {
          id: ThirdPartyId.ExHentai,
          counts: {
            [ThirdPartyRequestResultType.Succeed]: 2,
            [ThirdPartyRequestResultType.Failed]: 1,
          },
          receivedBytes: 8192,
        },
        {
          id: ThirdPartyId.Pixiv,
          counts: {},
          receivedBytes: 0,
        },
      ]),
    );
    const exHentaiCounts = within(screen.getByTestId(`request-source-${ThirdPartyId.ExHentai}`));

    expect(exHentaiCounts.getByText("3")).toBeInTheDocument();
    expect(exHentaiCounts.getByText("Succeed: 2")).toBeInTheDocument();
    expect(exHentaiCounts.getByText("Failed: 1")).toBeInTheDocument();
    expect(
      within(screen.getByTestId(`request-source-${ThirdPartyId.Pixiv}`)).getByText("0"),
    ).toBeInTheDocument();
    expect(
      within(screen.getByTestId(`traffic-source-${ThirdPartyId.ExHentai}`)).getByText("8.00 KiB"),
    ).toBeInTheDocument();
    expect(
      within(screen.getByTestId(`traffic-source-${ThirdPartyId.Pixiv}`)).getByText("0 B"),
    ).toBeInTheDocument();
    expect(trafficData().labels).toEqual(["ExHentai", "Pixiv"]);
    expect(trafficData().datasets[0].data).toEqual([8192, 0]);
  });
});
