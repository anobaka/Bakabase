import type { ComponentProps } from "react";
import type { DownloadTaskFilter } from "../DownloadTaskFilters";

import { useState } from "react";
import { HeroUIProvider } from "@heroui/react";
import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import DownloadTaskFilters from "../DownloadTaskFilters";

import ActualSelect from "@/components/bakaui/components/Select";
import { DownloadTaskStatus, ThirdPartyId } from "@/sdk/constants";

vi.mock("@/i18n", () => ({ getEnumKey: (name: string, value: string) => `${name}.${value}` }));
// Keep the production HeroUI inputs, press handling, collection and selection behavior.
vi.mock("@/components/bakaui", async () => ({
  ...(await import("@heroui/react")),
  Button: (await import("@/components/bakaui/components/Button")).Button,
  Select: (props: ComponentProps<typeof ActualSelect>) => <ActualSelect {...props} />,
}));

const countsByThirdParty = new Map([
  [ThirdPartyId.ExHentai, 8],
  [ThirdPartyId.Bilibili, 4],
]);
const countsByStatus = new Map([
  [DownloadTaskStatus.Downloading, 3],
  [DownloadTaskStatus.Complete, 9],
]);
const sources = [
  { value: ThirdPartyId.ExHentai, label: "ExHentai" },
  { value: ThirdPartyId.Bilibili, label: "Bilibili" },
  { value: ThirdPartyId.Fantia, label: "Fantia" },
];

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

async function mount(initialValue: DownloadTaskFilter = {}) {
  const changed = vi.fn();

  function ControlledFilters() {
    const [value, setValue] = useState(initialValue);

    return (
      <DownloadTaskFilters
        countsByStatus={countsByStatus}
        countsByThirdParty={countsByThirdParty}
        sources={sources}
        total={12}
        value={value}
        onChange={(next) => {
          changed(next);
          setValue(next);
        }}
      />
    );
  }

  await act(async () => {
    root.render(
      <HeroUIProvider disableAnimation>
        <ControlledFilters />
      </HeroUIProvider>,
    );
  });

  return { changed };
}

function button(name: string, scope: ParentNode = document) {
  return Array.from(scope.querySelectorAll<HTMLButtonElement>("button")).find(
    (element) =>
      element.getAttribute("aria-label")?.includes(name) || element.textContent?.includes(name),
  );
}
const statusButton = (status: string) =>
  button(`downloader.filter.${status}`, container.querySelector('[role="group"]')!)!;
const sourceTrigger = () =>
  container.querySelector<HTMLButtonElement>('button[aria-haspopup="listbox"]')!;
const option = (name: string) =>
  document.querySelector<HTMLElement>(`[role="option"][aria-label="${name}"]`)!;
const searchInput = () =>
  container.querySelector<HTMLInputElement>('input[aria-label="downloader.filter.keyword"]')!;

async function click(element: HTMLElement) {
  expect(element).toBeTruthy();
  await act(async () => element.click());
}
async function setKeyword(value: string) {
  await act(async () => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(
      searchInput(),
      value,
    );
    searchInput().dispatchEvent(new Event("input", { bubbles: true }));
  });
}

describe("download task filters", () => {
  it("updates the controlled keyword immediately and clears it without dropping source or status", async () => {
    const initial = {
      status: DownloadTaskStatus.Downloading,
      thirdPartyId: ThirdPartyId.ExHentai,
    };
    const { changed } = await mount(initial);
    const search = searchInput();

    await setKeyword("gallery");
    expect(search).toHaveValue("gallery");
    expect(changed).toHaveBeenLastCalledWith({ ...initial, keyword: "gallery" });

    await click(button("clear input")!);
    expect(search).toHaveValue("");
    expect(changed).toHaveBeenLastCalledWith({ ...initial, keyword: undefined });
    expect(statusButton("status.Downloading")).toHaveAttribute("aria-pressed", "true");
    expect(sourceTrigger()).toHaveTextContent("ExHentai");
  });

  it("offers all sources and retains zero-count developing sources, with counts and accessible names", async () => {
    const initial = { status: DownloadTaskStatus.Complete, keyword: "example" };
    const { changed } = await mount(initial);

    await click(sourceTrigger());
    expect(option("ExHentai")).toHaveTextContent("8");
    expect(option("Fantia")).toHaveTextContent("0");
    expect(option("Fantia")).toHaveTextContent("downloader.filter.developing");
    await click(option("Bilibili"));
    expect(changed).toHaveBeenLastCalledWith({ ...initial, thirdPartyId: ThirdPartyId.Bilibili });
    expect(sourceTrigger()).toHaveTextContent("Bilibili");

    await click(sourceTrigger());
    await click(option("downloader.filter.allSources"));
    expect(changed).toHaveBeenLastCalledWith({ ...initial, thirdPartyId: undefined });
    expect(sourceTrigger()).toHaveTextContent("downloader.filter.allSources");
  });

  it("switches status accessibly and uses All to clear only the status", async () => {
    const initial = { keyword: "example", thirdPartyId: ThirdPartyId.ExHentai };
    const { changed } = await mount(initial);
    const all = statusButton("allStatuses");
    const downloading = statusButton("status.Downloading");

    expect(all).toHaveAttribute("aria-pressed", "true");
    expect(all).toHaveTextContent("12");
    expect(downloading).toHaveTextContent("3");
    await click(downloading);
    expect(changed).toHaveBeenLastCalledWith({
      ...initial,
      status: DownloadTaskStatus.Downloading,
    });
    expect(downloading).toHaveAttribute("aria-pressed", "true");
    expect(all).toHaveAttribute("aria-pressed", "false");

    await click(statusButton("status.Failed"));
    expect(changed).toHaveBeenLastCalledWith({ ...initial, status: DownloadTaskStatus.Failed });
    expect(downloading).toHaveAttribute("aria-pressed", "false");
    await click(all);
    expect(changed).toHaveBeenLastCalledWith({ ...initial, status: undefined });
    expect(all).toHaveAttribute("aria-pressed", "true");
    expect(searchInput()).toHaveValue("example");
  });

  it("shows reset only for active filters and resets every controlled field", async () => {
    const { changed } = await mount();

    expect(button("downloader.filter.reset")).toBeUndefined();
    await setKeyword("example");
    await click(statusButton("status.Complete"));
    await click(sourceTrigger());
    await click(option("ExHentai"));
    await click(button("downloader.filter.reset")!);

    expect(changed).toHaveBeenLastCalledWith({});
    expect(searchInput()).toHaveValue("");
    expect(sourceTrigger()).toHaveTextContent("downloader.filter.allSources");
    expect(statusButton("allStatuses")).toHaveAttribute("aria-pressed", "true");
    expect(button("downloader.filter.reset")).toBeUndefined();
  });
});
