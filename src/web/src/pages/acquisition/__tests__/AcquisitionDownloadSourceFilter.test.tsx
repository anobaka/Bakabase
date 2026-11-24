import { useState } from "react";
import { HeroUIProvider, Chip } from "@heroui/react";
import { act, render, screen } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import { createInstance } from "i18next";
import AcquisitionDownloadSourceFilter from "../components/AcquisitionDownloadSourceFilter";
import { downloadSourceLabel } from "@/components/DownloadSource";
import cn from "@/locales/cn/enums.json";
import en from "@/locales/en/enums.json";

vi.mock("@/components/bakaui", async () => {
  const actual = await vi.importActual<typeof import("@/components/bakaui")>("@/components/bakaui");
  return { ...actual, Chip: (props: any) => <Chip {...props} /> };
});
vi.mock("@/i18n", () => ({ getEnumKey: (_name: string, label: string) => `enum.thirdPartyId.${label[0].toLowerCase()}${label.slice(1)}` }));

it("uses a flat source row with protocol and site icons and clears through All sources", async () => {
  const change = vi.fn();
  function Controlled() {
    const [value, setValue] = useState<string>();
    return <AcquisitionDownloadSourceFilter value={value} onChange={next => { change(next); setValue(next); }} />;
  }
  render(<HeroUIProvider disableAnimation><Controlled /></HeroUIProvider>);
  const http = screen.getByRole("button", { name: "downloadCenter.provider.http" });
  const bt = screen.getByRole("button", { name: "downloadCenter.provider.bt" });
  const baidu = screen.getByRole("button", { name: "enum.thirdPartyId.baiduNetdisk" });
  expect(screen.queryByRole("button", { name: /weiyun|微云/i })).toBeNull();
  expect(http.querySelector("svg")).not.toBeNull();
  expect(bt.querySelector("svg")).not.toBeNull();
  expect(baidu.querySelector("img")).not.toBeNull();
  await act(async () => baidu.click());
  expect(change).toHaveBeenLastCalledWith("site:14");
  expect(baidu).toHaveAttribute("aria-pressed", "true");
  expect(baidu.querySelector("img")).not.toBeNull();
  await act(async () => screen.getByRole("button", { name: "downloader.filter.allSources" }).click());
  expect(change).toHaveBeenLastCalledWith(undefined);
});

it.each(["cn", "en"] as const)("shares localized Download Center site labels in %s", async language => {
  const i18n = createInstance();
  await i18n.init({ lng: language, resources: { cn: { translation: cn }, en: { translation: en } } });
  expect(downloadSourceLabel("site:14", i18n.t)).toBe(language === "cn" ? "百度网盘" : "Baidu Netdisk");
});
