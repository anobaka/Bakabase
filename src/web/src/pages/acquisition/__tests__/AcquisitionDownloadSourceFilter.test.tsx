import type * as BakaUI from "@/components/bakaui";
import { useState } from "react";
import { HeroUIProvider, Chip } from "@heroui/react";
import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { expect, it, vi } from "vitest";
import { createInstance } from "i18next";
import { I18nextProvider } from "react-i18next";
import AcquisitionDownloadSourceFilter from "../components/AcquisitionDownloadSourceFilter";
import { downloadSourceLabel } from "@/components/DownloadSource";
import cn from "@/locales/cn/enums.json";
import acquisitionCn from "@/locales/cn/pages/acquisition.json";
import downloaderCn from "@/locales/cn/pages/downloader.json";
import en from "@/locales/en/enums.json";
import acquisitionEn from "@/locales/en/pages/acquisition.json";

vi.unmock("react-i18next");
vi.mock("@/components/bakaui", async () => {
  const actual = await vi.importActual<typeof BakaUI>("@/components/bakaui");
  return { ...actual, Chip: (props: any) => <Chip {...props} /> };
});
vi.mock("@/i18n", () => ({ getEnumKey: (_name: string, label: string) => `enum.thirdPartyId.${label[0].toLowerCase()}${label.slice(1)}` }));

it("uses a flat row with protocol, site and unsupported source icons and clears through All sources", async () => {
  const change = vi.fn();
  function Controlled() {
    const [value, setValue] = useState<string>();
    return <AcquisitionDownloadSourceFilter counts={{ all: 37, http: 15, bt: 2, "site:14": 28, unsupported: 4, unknown: 3 }} value={value} onChange={next => { change(next); setValue(next); }} />;
  }
  const i18n = createInstance();
  await i18n.init({ lng: "en", resources: { en: { translation: acquisitionEn } } });
  render(<I18nextProvider i18n={i18n}><HeroUIProvider disableAnimation><Controlled /></HeroUIProvider></I18nextProvider>);
  const http = screen.getByRole("button", { name: "downloadCenter.provider.http 15" });
  const bt = screen.getByRole("button", { name: "downloadCenter.provider.bt 2" });
  const baidu = screen.getByRole("button", { name: "enum.thirdPartyId.baiduNetdisk 28" });
  const unsupported = screen.getByRole("button", { name: "Unsupported sources 4" });
  expect(screen.getAllByRole("button").at(-1)).toBe(unsupported);
  expect(screen.queryByRole("button", { name: /weiyun|微云|unknown/i })).toBeNull();
  expect(http.querySelector("svg")).not.toBeNull();
  expect(bt.querySelector("svg")).not.toBeNull();
  expect(baidu.querySelector("img")).not.toBeNull();
  await act(async () => baidu.click());
  expect(change).toHaveBeenLastCalledWith("site:14");
  expect(baidu).toHaveAttribute("aria-pressed", "true");
  expect(baidu.querySelector("img")).not.toBeNull();
  expect(unsupported.querySelector("svg")).not.toBeNull();
  await act(async () => unsupported.click());
  expect(change).toHaveBeenLastCalledWith("unsupported");
  expect(unsupported).toHaveAttribute("aria-pressed", "true");
  expect(baidu).toHaveAttribute("aria-pressed", "false");
  await act(async () => screen.getByRole("button", { name: "downloader.filter.allSources 37" }).click());
  expect(change).toHaveBeenLastCalledWith(undefined);
});

it.each(["cn", "en"] as const)("shares localized Download Center site labels in %s", async language => {
  const i18n = createInstance();
  await i18n.init({ lng: language, resources: { cn: { translation: cn }, en: { translation: en } } });
  expect(downloadSourceLabel("site:14", i18n.t)).toBe(language === "cn" ? "百度网盘" : "Baidu Netdisk");
});

it.each([
  { source: "site:14", label: "百度网盘" },
  { source: "unsupported", label: "不支持的来源" },
])("keeps selected $source while waiting or at zero and lets the user clear it", async ({ source, label }) => {
  const change = vi.fn();
  const i18n = createInstance();
  await i18n.init({ lng: "cn", resources: { cn: { translation: { ...acquisitionCn, ...downloaderCn, ...cn } } } });
  const row = (counts?: Record<string, number>) => <I18nextProvider i18n={i18n}>
    <HeroUIProvider disableAnimation>
      <AcquisitionDownloadSourceFilter value={source} onChange={change} counts={counts} />
    </HeroUIProvider>
  </I18nextProvider>;
  const { rerender } = render(row({ all: 0, bt: 0 }));
  const selected = screen.getByRole("button", { name: `${label} 0` });
  expect(selected).toHaveAttribute("aria-pressed", "true");
  expect(screen.getByRole("button", { name: "全部来源 0" })).toBeVisible();
  expect(screen.getAllByRole("button")).toHaveLength(2);
  await act(async () => selected.click());
  expect(change).toHaveBeenLastCalledWith(undefined);
  rerender(row());
  expect(screen.getAllByRole("button")).toHaveLength(2);
  expect(screen.getByRole("button", { name: "全部来源 —" })).toBeVisible();
  const pending = screen.getByRole("button", { name: `${label} —` });
  expect(pending).toBe(selected);
  const user = userEvent.setup();
  act(() => pending.focus());
  await user.keyboard("{Enter}");
  expect(pending).toHaveFocus();
  expect(change).toHaveBeenCalledTimes(2);
  expect(change).toHaveBeenLastCalledWith(undefined);
});

it("has no unsupported option when its count is zero and nothing is selected", async () => {
  const i18n = createInstance();
  await i18n.init({ lng: "en", resources: { en: { translation: acquisitionEn } } });
  render(<I18nextProvider i18n={i18n}><HeroUIProvider disableAnimation>
    <AcquisitionDownloadSourceFilter counts={{ all: 5, http: 2, unsupported: 0 }} onChange={vi.fn()} />
  </HeroUIProvider></I18nextProvider>);
  expect(screen.getByRole("button", { name: "downloadCenter.provider.http 2" })).toBeVisible();
  expect(screen.queryByRole("button", { name: /Unsupported sources/ })).toBeNull();
});
