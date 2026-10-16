import type { ComponentProps } from "react";
import type { ManagementSource } from "@/features/management/api";

import { HeroUIProvider } from "@heroui/react";
import { createRoot } from "react-dom/client";
import { act } from "react-dom/test-utils";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import DeviceFilter from "../DeviceFilter";

const state = vi.hoisted(() => ({ createPortal: vi.fn(), optionsThreshold: 30 }));

vi.mock("@/components/bakaui", async () => ({
  ...(await vi.importActual("@heroui/react")),
  Button: (await import("@/components/bakaui/components/Button")).Button,
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: state.createPortal }),
}));
vi.mock("@/hooks/useFilterOptionsThreshold", () => ({
  DEFAULT_FILTER_OPTIONS_THRESHOLD: 30,
  useFilterOptionsThreshold: () => [state.optionsThreshold],
}));

const local: ManagementSource = {
  serverId: "local",
  libraryEpoch: "local-epoch",
  name: "Local",
  isLocal: true,
  available: true,
  contractVersion: 1,
};
const remote: ManagementSource = {
  ...local,
  serverId: "remote",
  libraryEpoch: "remote-epoch",
  name: "Remote",
  isLocal: false,
};
const sources = [local, remote];
let host: HTMLDivElement;
let root: ReturnType<typeof createRoot>;
let onChange: ReturnType<typeof vi.fn>;

beforeEach(() => {
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new Error("Unexpected API call")));
  host = document.createElement("div");
  document.body.append(host);
  root = createRoot(host);
  onChange = vi.fn();
  state.createPortal.mockClear();
  state.optionsThreshold = 30;
});
afterEach(async () => {
  await act(async () => root.unmount());
  host.remove();
  vi.unstubAllGlobals();
});

async function render(props: Partial<ComponentProps<typeof DeviceFilter>> = {}) {
  await act(async () => {
    root.render(
      <HeroUIProvider disableAnimation>
        <DeviceFilter sources={sources} onChange={onChange} {...props} />
      </HeroUIProvider>,
    );
  });
}

function option(label: string) {
  const found = [...host.querySelectorAll<HTMLElement>("[data-value-option]")].find((item) =>
    item.textContent?.startsWith(label),
  );

  if (!found) throw new Error(`Missing device option: ${label}`);

  return found;
}

async function click(label: string) {
  await act(async () => option(label).click());
}

describe("device resource filter", () => {
  it.each([undefined, null])(
    "renders %s as all selected using the shared filter and choice UI",
    async (deviceIds) => {
      await render({ deviceIds });
      expect(host.textContent).toContain("management.deviceFilter");
      expect(host.querySelector(".rounded-xl.bg-default-100\\/60")).not.toBeNull();
      expect(host.querySelector("select")).toBeNull();
      expect(option("Remote").className).not.toContain("opacity-50");
      expect(option("management.thisDevice").className).not.toContain("opacity-50");
      await click("Remote");
      expect(onChange).toHaveBeenCalledExactlyOnceWith(["local"]);
      expect(fetch).not.toHaveBeenCalled();
    },
  );

  it("preserves an explicit empty selection and emits only device IDs", async () => {
    await render({ deviceIds: [] });
    expect(option("Remote").className).toContain("opacity-50");
    expect(option("management.thisDevice").className).toContain("opacity-50");
    await click("Remote");
    expect(onChange).toHaveBeenLastCalledWith(["remote"]);
    await render({ deviceIds: ["remote"] });
    await click("Remote");
    expect(onChange).toHaveBeenLastCalledWith([]);
    expect(fetch).not.toHaveBeenCalled();
  });

  it("returns to all-device mode when the user explicitly selects every current device", async () => {
    await render({ deviceIds: ["remote"] });
    await click("management.thisDevice");
    expect(onChange).toHaveBeenCalledExactlyOnceWith(undefined);
  });

  it("refreshes device names and options without expanding an explicit selection", async () => {
    await render({ deviceIds: ["remote"] });
    await render({
      deviceIds: ["remote"],
      sources: [
        local,
        { ...remote, name: "Renamed", available: false },
        { ...remote, serverId: "new", name: "New" },
      ],
    });
    expect(option("Renamed").textContent).toContain("management.offline");
    expect(option("Renamed").className).not.toContain("opacity-50");
    expect(option("New").className).toContain("opacity-50");
    expect(onChange).not.toHaveBeenCalled();
    await click("New");
    expect(onChange).toHaveBeenLastCalledWith(["remote", "new"]);
  });

  it("keeps missing selected devices removable instead of silently selecting the remaining local device", async () => {
    await render({ deviceIds: ["gone"], sources: [local] });
    expect(option("management.unavailableDevice").textContent).toContain("management.offline");
    expect(option("management.thisDevice").className).toContain("opacity-50");
    expect(onChange).not.toHaveBeenCalled();
    await click("management.thisDevice");
    expect(onChange).toHaveBeenLastCalledWith(["gone", "local"]);
    await render({ deviceIds: ["gone"], sources: [local] });
    await click("management.unavailableDevice");
    expect(onChange).toHaveBeenLastCalledWith([]);
  });

  it("represents a restored owner without replacing its epoch until the user changes the filter", async () => {
    await render({ owner: { serverId: "remote", libraryEpoch: "old-epoch" } });
    expect(option("Remote").className).not.toContain("opacity-50");
    expect(option("Remote").textContent).toContain("management.offline");
    expect(option("management.thisDevice").className).toContain("opacity-50");
    expect(onChange).not.toHaveBeenCalled();
    await click("Remote");
    expect(onChange).toHaveBeenLastCalledWith([]);
  });

  it("keeps the original single-device page free of a redundant device filter", async () => {
    await render({ sources: [local] });
    expect(host.textContent).toBe("");
    expect(fetch).not.toHaveBeenCalled();
  });

  it("keeps an explicit empty filter recoverable after the last remote device is removed", async () => {
    await render({ sources: [local], deviceIds: [] });
    expect(option("management.thisDevice").className).toContain("opacity-50");
    await click("management.thisDevice");
    expect(onChange).toHaveBeenCalledExactlyOnceWith(undefined);
  });

  it("does not include newly added devices when applying an already open full choice editor", async () => {
    state.optionsThreshold = 1;
    await render({ deviceIds: [] });
    const more = [...host.querySelectorAll<HTMLButtonElement>("button")].find((button) =>
      button.textContent?.startsWith("common.action.more"),
    );

    expect(more).toBeDefined();
    await act(async () => more!.click());
    const editor = state.createPortal.mock.calls[0][1];

    await render({
      deviceIds: [],
      sources: [...sources, { ...remote, serverId: "new", name: "New" }],
    });
    await act(async () => editor.onValueChange(["local", "remote"]));
    expect(onChange).toHaveBeenCalledExactlyOnceWith(["local", "remote"]);
  });
});
