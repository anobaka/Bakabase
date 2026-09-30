import type { ReactNode } from "react";

import { HeroUIProvider } from "@heroui/react";
import { act, fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { ExHentaiConfigField, ExHentaiConfigPanel } from "../ExHentaiConfig";

type OriginalImageOptions = {
  preferTorrent?: boolean;
  preferOriginalImages?: boolean;
  allowOriginalImageGpSpending?: boolean;
  originalImageMinimumGpBalance?: number | null;
  originalImageMaximumGpCostPerTask?: number | null;
};

const { state, patchExHentai, getDefinitions } = vi.hoisted(() => ({
  state: { options: {} as OriginalImageOptions },
  patchExHentai: vi.fn(),
  getDefinitions: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: {
    options: { patchExHentaiOptions: patchExHentai },
    downloadTask: { getAllDownloaderDefinitions: getDefinitions },
  },
}));
vi.mock("@/stores/options", () => ({
  useExHentaiOptionsStore: (selector: (state: unknown) => unknown) =>
    selector({ data: state.options, patch: patchExHentai }),
}));
vi.mock("../../base/useAutoSaveToast", () => ({
  default: (patch: (value: unknown) => unknown) => patch,
}));
vi.mock("../../base/ConfigurableThirdPartyPanel", () => ({
  default: ({
    fields,
    tabs,
  }: {
    fields: string[] | "all";
    tabs: { field: string; key: string; content: ReactNode }[];
  }) => (
    <div>
      {tabs
        .filter((tab) => fields === "all" || fields.includes(tab.field))
        .map((tab) => (
          <div key={tab.key}>{tab.content}</div>
        ))}
    </div>
  ),
}));
// Exercise the real number-field commit, clamping and clear behavior.
vi.mock("@/components/bakaui", async () => ({
  ...(await import("@heroui/react")),
  toast: { success: vi.fn() },
}));
vi.mock("../../base/AccountsPanel", () => ({ default: () => null }));
vi.mock("../../base/MetadataMappingPanel", () => ({ default: () => null }));
vi.mock("../../base/AutoSyncPanel", () => ({ default: () => null }));
vi.mock("../../base/TampermonkeyInstallButton", () => ({ default: () => null }));
vi.mock("../../base/ThirdPartyConfigModal", () => ({ default: () => null }));
vi.mock("../../base/ProxyField", () => ({ default: () => null }));
vi.mock("../../base/DownloadResultWorkflowField", () => ({ default: () => null }));
vi.mock("@/components/FileSystemSelector", () => ({ FileSystemSelectorButton: () => null }));
vi.mock("@/pages/downloader/components/TaskDetailModal/components/PreferTorrentField", () => ({
  default: () => null,
}));

const keys = {
  prefer: "thirdPartyConfig.exHentai.originalImages.prefer",
  allowGp: "thirdPartyConfig.exHentai.originalImages.allowGp",
  minimum: "thirdPartyConfig.exHentai.originalImages.minimumGp",
  maximum: "thirdPartyConfig.exHentai.originalImages.maximumGp",
};
const panel = () => (
  <HeroUIProvider disableAnimation>
    <ExHentaiConfigPanel fields={[ExHentaiConfigField.Download]} />
  </HeroUIProvider>
);
const input = (key: string) => screen.getByRole<HTMLInputElement>("textbox", { name: key });
const enableGp = () => {
  state.options = { preferOriginalImages: true, allowOriginalImageGpSpending: true };
};

beforeEach(() => {
  vi.clearAllMocks();
  state.options = {};
  getDefinitions.mockResolvedValue({ data: [] });
});
afterEach(() => {
  vi.clearAllMocks();
});

describe("ExHentai original image configuration", () => {
  it("defaults to normal images and makes the spending warning visible before opt-in", async () => {
    render(panel());
    expect(screen.getByRole("checkbox", { name: keys.prefer })).not.toBeChecked();
    expect(screen.getByRole("note")).toHaveTextContent(
      "thirdPartyConfig.exHentai.originalImages.warning",
    );
    expect(screen.queryByRole("checkbox", { name: keys.allowGp })).toBeNull();
    expect(screen.queryByRole("textbox", { name: keys.minimum })).toBeNull();
    expect(patchExHentai).not.toHaveBeenCalled();
  });

  it("enables originals without silently allowing GP or changing the torrent preference", async () => {
    const user = userEvent.setup();
    const view = render(panel());

    await user.click(screen.getByRole("checkbox", { name: keys.prefer }));
    expect(patchExHentai).toHaveBeenCalledOnce();
    expect(patchExHentai).toHaveBeenLastCalledWith({ preferOriginalImages: true });

    state.options = { preferOriginalImages: true };
    view.rerender(panel());
    expect(screen.getByRole("checkbox", { name: keys.allowGp })).not.toBeChecked();
    expect(screen.queryByRole("textbox", { name: keys.minimum })).toBeNull();
    expect(screen.getByText("thirdPartyConfig.exHentai.originalImages.freeOnly")).toBeVisible();
    expect(screen.getByText("thirdPartyConfig.exHentai.originalImages.unavailable")).toBeVisible();
  });

  it("only exposes limits after GP opt-in and shows conservative default reservations", async () => {
    const user = userEvent.setup();

    state.options = { preferOriginalImages: true };
    const view = render(panel());

    await user.click(screen.getByRole("checkbox", { name: keys.allowGp }));
    expect(patchExHentai).toHaveBeenLastCalledWith({ allowOriginalImageGpSpending: true });

    enableGp();
    view.rerender(panel());
    expect(input(keys.minimum)).toHaveValue("10,000");
    expect(input(keys.maximum)).toHaveValue("100,000");
    expect(screen.getByText("thirdPartyConfig.exHentai.originalImages.gpPolicy")).toBeVisible();
  });

  it("keeps zero limits and enables GP inputs independently from torrent downloading", async () => {
    state.options = {
      preferTorrent: false,
      preferOriginalImages: true,
      allowOriginalImageGpSpending: true,
      originalImageMinimumGpBalance: 0,
      originalImageMaximumGpCostPerTask: 0,
    };
    render(panel());
    expect(input(keys.minimum)).toHaveValue("0");
    expect(input(keys.maximum)).toHaveValue("0");
    expect(input(keys.minimum)).not.toBeDisabled();
    expect(input(keys.maximum)).not.toBeDisabled();
  });

  it("saves each integer limit through its own option patch after committing the input", async () => {
    const user = userEvent.setup();

    enableGp();
    render(panel());
    await user.clear(input(keys.minimum));
    await user.type(input(keys.minimum), "12345");
    await user.tab();
    expect(patchExHentai).toHaveBeenLastCalledWith({ originalImageMinimumGpBalance: 12345 });

    await user.clear(input(keys.maximum));
    await user.type(input(keys.maximum), "54321");
    await user.tab();
    expect(patchExHentai).toHaveBeenLastCalledWith({ originalImageMaximumGpCostPerTask: 54321 });
  });

  it.each([
    [keys.minimum, "originalImageMinimumGpBalance", 10000],
    [keys.maximum, "originalImageMaximumGpCostPerTask", 100000],
  ] as const)("restores the safe default when %s is cleared", async (key, field, value) => {
    const user = userEvent.setup();

    enableGp();
    state.options = { ...state.options, [field]: 0 };
    render(panel());
    await user.clear(input(key));
    await user.tab();
    expect(patchExHentai).toHaveBeenLastCalledWith({ [field]: value });
  });

  it("hides stored GP permission and limits when original downloading is disabled", async () => {
    enableGp();
    const view = render(panel());

    state.options = { ...state.options, preferOriginalImages: false };
    await act(async () => view.rerender(panel()));
    expect(screen.queryByRole("checkbox", { name: keys.allowGp })).toBeNull();
    expect(screen.queryByRole("textbox", { name: keys.minimum })).toBeNull();
    expect(patchExHentai).not.toHaveBeenCalled();
  });

  it.each(["1.5", "-10"])("rejects the invalid GP amount %s without saving", async (value) => {
    enableGp();
    render(panel());
    const field = input(keys.minimum);

    await act(async () => {
      field.focus();
      fireEvent.change(field, { target: { value } });
      fireEvent.blur(field);
    });
    expect(patchExHentai).not.toHaveBeenCalled();
    expect(field).toHaveValue("10,000");
  });
});
