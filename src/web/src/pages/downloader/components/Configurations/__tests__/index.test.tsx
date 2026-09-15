import type { ReactElement, ReactNode } from "react";

import { Children, useState } from "react";
import { createRoot, type Root } from "react-dom/client";
import { act } from "react-dom/test-utils";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ConfigurationsModal from "..";

import { ThirdPartyId } from "@/sdk/constants";

const { getDefinitions, patch } = vi.hoisted(() => ({
  getDefinitions: vi.fn(),
  patch: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: { downloadTask: { getAllDownloaderDefinitions: getDefinitions } },
}));
vi.mock("@/stores/options", () => ({
  useDownloaderGlobalOptionsStore: (selector: (state: unknown) => unknown) =>
    selector({ data: { autoStartAfterCreation: false }, patch }),
}));
vi.mock("@/components/ThirdPartyIcon", () => ({ default: () => <span /> }));
vi.mock("@/components/Chips/DevelopingChip", () => ({ default: () => null }));
vi.mock("@/pages/downloader/models", () => ({ isThirdPartyDeveloping: () => false }));
vi.mock("@heroui/react", () => ({
  Switch: ({
    children,
    isSelected,
    onValueChange,
  }: {
    children: ReactNode;
    isSelected: boolean;
    onValueChange: (selected: boolean) => void;
  }) => (
    <label>
      <input
        checked={isSelected}
        type="checkbox"
        onChange={(event) => onValueChange(event.target.checked)}
      />
      {children}
    </label>
  ),
}));
vi.mock("@/components/ThirdPartyConfig", () => ({
  ExHentaiConfigField: { Accounts: "accounts", DataFetch: "dataFetch", Download: "download" },
  DLsiteConfigField: { Accounts: "accounts", DataFetch: "dataFetch", Download: "download" },
  SteamConfigField: { Accounts: "accounts" },
  ExHentaiConfigPanel: ({ fields }: { fields: string[] }) => (
    <div data-exhentai-fields={fields.join(",")}>ExHentai settings</div>
  ),
  DLsiteConfigPanel: () => <div>DLsite settings</div>,
  SteamConfigPanel: () => <div>Steam settings</div>,
  BilibiliConfigPanel: () => <div>Bilibili settings</div>,
  PixivConfigPanel: () => <div>Pixiv settings</div>,
  FanboxConfigPanel: () => <div>Fanbox settings</div>,
  FantiaConfigPanel: () => <div>Fantia settings</div>,
  CienConfigPanel: () => <div>Cien settings</div>,
  PatreonConfigPanel: () => <div>Patreon settings</div>,
  BangumiConfigPanel: () => <div>Bangumi settings</div>,
}));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ children, title }: { children: ReactNode; title: string }) => (
    <div aria-label={title} role="dialog">
      {children}
    </div>
  ),
  Spinner: () => <span>Loading</span>,
  Button: ({
    children,
    onPress,
    startContent,
    ...props
  }: {
    children: ReactNode;
    onPress: () => void;
    startContent?: ReactNode;
    "aria-pressed"?: boolean;
    "aria-controls"?: string;
  }) => (
    <button
      aria-controls={props["aria-controls"]}
      aria-pressed={props["aria-pressed"]}
      onClick={onPress}
    >
      {startContent}
      {children}
    </button>
  ),
  Tab: ({ children }: { children: ReactNode }) => <>{children}</>,
  Tabs: ({ children, defaultSelectedKey }: { children: ReactNode; defaultSelectedKey: string }) => {
    const [selected, setSelected] = useState(defaultSelectedKey);
    const tabs = Children.toArray(children) as ReactElement<{
      title: string;
      children: ReactNode;
    }>[];
    const tabKey = (tab: (typeof tabs)[number]) => String(tab.key).replace(/^\.\$/, "");

    return (
      <div>
        <div role="tablist">
          {tabs.map((tab) => (
            <button
              key={tab.key}
              aria-selected={selected === tabKey(tab)}
              role="tab"
              onClick={() => setSelected(tabKey(tab))}
            >
              {tab.props.title}
            </button>
          ))}
        </div>
        {tabs.find((tab) => selected === tabKey(tab))?.props.children}
      </div>
    );
  },
}));

let container: HTMLDivElement;
let root: Root;
const button = (name: string) =>
  Array.from(container.querySelectorAll("button")).find((item) => item.textContent === name)!;
const choose = async (name: string) => {
  await act(async () => button(name).click());
};

beforeEach(() => {
  vi.resetAllMocks();
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

describe("downloader settings navigation", () => {
  it("opens ExHentai downloads first and switches between its shared configuration sections", async () => {
    getDefinitions.mockResolvedValue({
      data: [
        { thirdPartyId: ThirdPartyId.ExHentai },
        { thirdPartyId: ThirdPartyId.ExHentai },
        { thirdPartyId: ThirdPartyId.Steam },
      ],
    });
    await act(async () => root.render(<ConfigurationsModal />));
    expect(container.querySelectorAll("nav button")).toHaveLength(3);
    await choose("ExHentai");
    expect(button("ExHentai")).toHaveAttribute("aria-pressed", "true");
    expect(container.querySelector("[data-exhentai-fields]")).toHaveAttribute(
      "data-exhentai-fields",
      "download",
    );
    await choose("resourceSource.config.tab.accounts");
    expect(container.querySelector("[data-exhentai-fields]")).toHaveAttribute(
      "data-exhentai-fields",
      "accounts",
    );
    await choose("downloader.config.exHentai.requests");
    expect(container.querySelector("[data-exhentai-fields]")).toHaveAttribute(
      "data-exhentai-fields",
      "dataFetch",
    );
    await choose("Steam");
    expect(container.querySelector("[data-exhentai-fields]")).toBeNull();
    expect(container).toHaveTextContent("Steam settings");
    await choose("ExHentai");
    expect(container.querySelector("[data-exhentai-fields]")).toHaveAttribute(
      "data-exhentai-fields",
      "download",
    );
  });

  it("retains the global auto-start setting and saves changes through the existing options store", async () => {
    getDefinitions.mockResolvedValue({ data: [] });
    await act(async () => root.render(<ConfigurationsModal />));
    const checkbox = container.querySelector<HTMLInputElement>("input[type=checkbox]")!;

    expect(checkbox).not.toBeChecked();
    await act(async () => checkbox.click());
    expect(patch).toHaveBeenCalledWith({ autoStartAfterCreation: true });
  });

  it("keeps general settings usable after a platform-load error and supports retry", async () => {
    getDefinitions
      .mockRejectedValueOnce(new Error("offline"))
      .mockResolvedValueOnce({ data: [{ thirdPartyId: ThirdPartyId.ExHentai }] });
    await act(async () => root.render(<ConfigurationsModal />));
    expect(container.querySelector('[role="alert"]')).toHaveTextContent(
      "downloader.config.platforms.loadFailed",
    );
    expect(container.querySelector("input[type=checkbox]")).toBeInTheDocument();
    await choose("downloader.config.platforms.retry");
    expect(container.querySelector('[role="alert"]')).toBeNull();
    await choose("ExHentai");
    expect(container.querySelector("[data-exhentai-fields]")).toHaveAttribute(
      "data-exhentai-fields",
      "download",
    );
  });
});
