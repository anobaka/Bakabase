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
import downloaderCn from "@/locales/cn/pages/downloader.json";
import en from "@/locales/en/enums.json";

vi.unmock("react-i18next");
vi.mock("@/components/bakaui", async () => {
  const actual = await vi.importActual<typeof import("@/components/bakaui")>("@/components/bakaui");
  return { ...actual, Chip: (props: any) => <Chip {...props} /> };
});
vi.mock("@/i18n", () => ({ getEnumKey: (_name: string, label: string) => `enum.thirdPartyId.${label[0].toLowerCase()}${label.slice(1)}` }));

it("uses a flat source row with protocol and site icons and clears through All sources", async () => {
  const change = vi.fn();
  function Controlled() {
    const [value, setValue] = useState<string>();
    return <AcquisitionDownloadSourceFilter counts={{ all: 37, http: 15, bt: 2, "site:14": 28 }} value={value} onChange={next => { change(next); setValue(next); }} />;
  }
  const i18n = createInstance();
  await i18n.init({ lng: "en", resources: { en: { translation: {} } } });
  render(<I18nextProvider i18n={i18n}><HeroUIProvider disableAnimation><Controlled /></HeroUIProvider></I18nextProvider>);
  const http = screen.getByRole("button", { name: "downloadCenter.provider.http 15" });
  const bt = screen.getByRole("button", { name: "downloadCenter.provider.bt 2" });
  const baidu = screen.getByRole("button", { name: "enum.thirdPartyId.baiduNetdisk 28" });
  expect(screen.queryByRole("button", { name: /weiyun|微云/i })).toBeNull();
  expect(http.querySelector("svg")).not.toBeNull();
  expect(bt.querySelector("svg")).not.toBeNull();
  expect(baidu.querySelector("img")).not.toBeNull();
  await act(async () => baidu.click());
  expect(change).toHaveBeenLastCalledWith("site:14");
  expect(baidu).toHaveAttribute("aria-pressed", "true");
  expect(baidu.querySelector("img")).not.toBeNull();
  await act(async () => screen.getByRole("button", { name: "downloader.filter.allSources 37" }).click());
  expect(change).toHaveBeenLastCalledWith(undefined);
});

it.each(["cn", "en"] as const)("shares localized Download Center site labels in %s", async language => {
  const i18n = createInstance();
  await i18n.init({ lng: language, resources: { cn: { translation: cn }, en: { translation: en } } });
  expect(downloadSourceLabel("site:14", i18n.t)).toBe(language === "cn" ? "百度网盘" : "Baidu Netdisk");
});

it("retains only the selected source and All while waiting; both known zero and unknown can be cleared", async () => {
  const change = vi.fn();
  const i18n = createInstance();
  await i18n.init({ lng: "cn", resources: { cn: { translation: { ...downloaderCn, ...cn } } } });
  const row = (counts?: Record<string, number>) => <I18nextProvider i18n={i18n}>
    <HeroUIProvider disableAnimation>
      <AcquisitionDownloadSourceFilter value="site:14" onChange={change} counts={counts} />
    </HeroUIProvider>
  </I18nextProvider>;
  const { rerender } = render(row({ all: 0, bt: 0 }));
  const selected = screen.getByRole("button", { name: "百度网盘 0" });
  expect(selected).toHaveAttribute("aria-pressed", "true");
  expect(screen.getByRole("button", { name: "全部来源 0" })).toBeVisible();
  expect(screen.getAllByRole("button")).toHaveLength(2);
  await act(async () => selected.click());
  expect(change).toHaveBeenLastCalledWith(undefined);
  rerender(row());
  expect(screen.getAllByRole("button")).toHaveLength(2);
  expect(screen.getByRole("button", { name: "全部来源 —" })).toBeVisible();
  const pending = screen.getByRole("button", { name: "百度网盘 —" });
  expect(pending).toBe(selected);
  const user = userEvent.setup();
  act(() => pending.focus());
  await act(async () => user.keyboard("{Enter}"));
  expect(pending).toHaveFocus();
  expect(change).toHaveBeenCalledTimes(2);
  expect(change).toHaveBeenLastCalledWith(undefined);
});
