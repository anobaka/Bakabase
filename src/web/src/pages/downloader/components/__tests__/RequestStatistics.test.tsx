import type { ReactNode } from "react";

import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
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
  Tab: ({ children, title }: { children: ReactNode; title: ReactNode }) => (
    <section>
      <h2>{title}</h2>
      {children}
    </section>
  ),
  Tabs: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  Tooltip: ({ children }: { children: ReactNode }) => <>{children}</>,
}));
vi.mock("react-chartjs-2", () => ({
  Bar: ({ data }: { data: unknown }) => <pre data-testid="chart-data">{JSON.stringify(data)}</pre>,
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
  it("shows per-site read traffic and keeps an open overview current", () => {
    render(<RequestStatistics compact />);
    fireEvent.click(screen.getByRole("button"));

    const modal = createPortal.mock.calls[0][1];
    render(modal.children);

    expect(screen.getByText("downloader.label.responseTraffic")).toBeInTheDocument();
    const trafficData = () => JSON.parse(screen.getAllByTestId("chart-data")[1].textContent!);
    expect(trafficData().labels).toEqual(["Pixiv", "ExHentai"]);
    expect(trafficData().datasets[0].data).toEqual([4096, 1536]);

    act(() =>
      useThirdPartyRequestStatisticsStore.getState().updateStatistics([
        {
          id: ThirdPartyId.ExHentai,
          counts: { [ThirdPartyRequestResultType.Succeed]: 1 },
          receivedBytes: 8192,
        },
        {
          id: ThirdPartyId.Pixiv,
          counts: { [ThirdPartyRequestResultType.Succeed]: 1 },
          receivedBytes: 4096,
        },
      ]),
    );
    expect(trafficData().labels).toEqual(["ExHentai", "Pixiv"]);
    expect(trafficData().datasets[0].data).toEqual([8192, 4096]);
  });
});
