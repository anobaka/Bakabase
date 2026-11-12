import type { ReactNode } from "react";

import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import Others from "../index";
import ProxyTestModal from "../ProxyTestModal";

const { state, patchNetwork, patchApp, portal, successToast, errorToast, applyPatches } =
  vi.hoisted(() => ({
    state: {
      network: {
        proxy: { mode: 2, customProxyId: "proxy-one" },
        customProxies: [
          {
            id: "proxy-one",
            name: "Primary",
            address: "http://127.0.0.1:8080",
            credentials: { username: "keep-user", password: "keep-secret" },
          },
        ],
        selectedPresetTestSiteIds: ["google"],
        customTestSites: ["https://example.test"],
      },
      app: { enableAnonymousDataTracking: false, maxParallelism: 4, effectiveMaxParallelism: 8 },
    },
    patchNetwork: vi.fn(),
    patchApp: vi.fn(),
    portal: vi.fn(),
    successToast: vi.fn(),
    errorToast: vi.fn(),
    applyPatches: vi.fn(),
  }));

vi.mock("@/sdk/BApi", () => ({
  default: { options: { patchNetworkOptions: patchNetwork, patchAppOptions: patchApp } },
}));
vi.mock("react-hot-toast", () => ({ default: { success: successToast, error: errorToast } }));
vi.mock("@/stores/options", () => ({
  useNetworkOptionsStore: (select: any) => select({ data: state.network }),
  useAppOptionsStore: (select: any) => select({ data: state.app }),
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: portal }),
}));
vi.mock("../ProxyTestModal", () => ({ default: () => null }));
vi.mock("../DownloadToolProxies", () => ({ default: () => <div>tool-proxy-selectors</div> }));
vi.mock("@/sdk/constants", () => ({ DownloadToolProxyTargets: [{ id: "gdown", name: "gdown" }] }));
vi.mock("@/components/ProxySelector", () => ({
  ProxyMode: { DoNotUse: 0, UseSystem: 1, UseCustom: 2 },
  default: ({ onChange }: { onChange: (value: unknown) => void }) => (
    <button onClick={() => onChange({ mode: 1 })}>choose-system-proxy</button>
  ),
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({
    children,
    onClick,
    onPress,
    "aria-label": label,
  }: {
    children: ReactNode;
    onClick?: () => void;
    onPress?: () => void;
    "aria-label"?: string;
  }) => (
    <button aria-label={label} onClick={onClick ?? onPress}>
      {children}
    </button>
  ),
  Input: ({ label, onValueChange, value, defaultValue, type, className }: any) => (
    <input
      aria-label={label ?? "parallelism"}
      className={className}
      defaultValue={defaultValue}
      type={type}
      value={value}
      onChange={(event) => onValueChange(event.target.value)}
    />
  ),
  Switch: ({ isSelected, onValueChange }: any) => (
    <button aria-checked={isSelected} role="switch" onClick={() => onValueChange(!isSelected)}>
      tracking-switch
    </button>
  ),
  Tooltip: ({ children }: { children: ReactNode }) => <>{children}</>,
  Modal: () => null,
}));

beforeEach(() => {
  delete (state.network as any).downloadToolProxies;
  patchNetwork.mockReset().mockResolvedValue({ code: 0 });
  patchApp.mockReset().mockResolvedValue({ code: 0 });
  portal.mockReset();
  applyPatches.mockReset();
  successToast.mockReset();
  errorToast.mockReset();
});
afterEach(cleanup);

describe("configuration network and advanced groups", () => {
  it("renders every existing field once when no group is supplied", () => {
    render(<Others applyPatches={applyPatches} />);
    expect(screen.getByRole("heading", { name: "configuration.others.title" })).toBeInTheDocument();
    expect(screen.getAllByText("configuration.others.proxy")).toHaveLength(1);
    expect(screen.getAllByRole("switch")).toHaveLength(1);
    expect(screen.getAllByRole("spinbutton")).toHaveLength(1);
    expect(screen.getAllByText("tool-proxy-selectors")).toHaveLength(1);
  });

  it("assigns proxy only to network and tracking/parallelism only to advanced", () => {
    render(
      <>
        <Others applyPatches={applyPatches} group="network" />
        <Others applyPatches={applyPatches} group="advanced" />
      </>,
    );
    const network = screen
      .getByRole("heading", { name: "configuration.others.networkTitle" })
      .closest("section")!;
    const advanced = screen
      .getByRole("heading", { name: "configuration.others.advancedTitle" })
      .closest("section")!;

    expect(within(network).getByText("configuration.others.proxy")).toBeInTheDocument();
    expect(within(network).queryByRole("switch")).toBeNull();
    expect(within(network).queryByRole("spinbutton")).toBeNull();
    expect(within(advanced).queryByText("configuration.others.proxy")).toBeNull();
    expect(within(network).getByText("tool-proxy-selectors")).toBeInTheDocument();
    expect(within(advanced).queryByText("tool-proxy-selectors")).toBeNull();
    expect(within(advanced).getByRole("switch")).toBeInTheDocument();
    expect(within(advanced).getByRole("spinbutton")).toHaveValue(4);
    expect(screen.getAllByText("configuration.others.proxy")).toHaveLength(1);
  });

  it("uses the existing patch wrapper for tracking and accepts only valid parallelism input", () => {
    render(<Others applyPatches={applyPatches} group="advanced" />);
    fireEvent.click(screen.getByRole("switch"));
    expect(applyPatches).toHaveBeenCalledWith(patchApp, { enableAnonymousDataTracking: true });
    const input = screen.getByRole("spinbutton");

    fireEvent.change(input, { target: { value: "12" } });
    expect(applyPatches).toHaveBeenCalledWith(patchApp, { maxParallelism: 12 });
    fireEvent.change(input, { target: { value: "0" } });
    expect(applyPatches).toHaveBeenCalledTimes(2);
    fireEvent.change(input, { target: { value: "" } });
    expect(applyPatches).toHaveBeenCalledWith(patchApp, { maxParallelism: undefined });
    expect(patchNetwork).not.toHaveBeenCalled();
  });

  it("keeps proxy selection and testing wired to the same network API and portal", async () => {
    render(<Others applyPatches={applyPatches} group="network" />);
    fireEvent.click(screen.getByRole("button", { name: "choose-system-proxy" }));
    expect(patchNetwork).toHaveBeenCalledWith({ proxy: { mode: 1 } });
    fireEvent.click(screen.getByRole("button", { name: "configuration.others.proxy.test" }));
    expect(portal).toHaveBeenCalledWith(
      ProxyTestModal,
      expect.objectContaining({
        customProxyId: "proxy-one",
        useSystemProxy: false,
        proxyLabel: "Primary",
        initialPresetIds: ["google"],
        initialCustomSites: ["https://example.test"],
      }),
    );
    const options = portal.mock.calls.at(-1)![1];

    options.onSelectionPersist(["github"], ["https://test.example"]);
    expect(patchNetwork).toHaveBeenCalledWith({
      selectedPresetTestSiteIds: ["github"],
      customTestSites: ["https://test.example"],
    });
  });

  it("keeps proxy editing in place and preserves unrelated credentials", async () => {
    render(<Others applyPatches={applyPatches} group="network" />);
    fireEvent.click(screen.getByRole("button", { name: "common.action.edit" }));
    const modal = portal.mock.calls.at(-1)![1];

    expect(modal.title).toBe("configuration.others.proxy.editModal.title");
    render(<>{modal.children}</>);
    fireEvent.change(screen.getByLabelText("configuration.others.proxy.name"), {
      target: { value: " Renamed " },
    });
    fireEvent.change(screen.getByLabelText("configuration.others.proxy.address"), {
      target: { value: "http://new.example:8080" },
    });
    await modal.onOk();
    expect(patchNetwork).toHaveBeenCalledWith({
      customProxies: [
        {
          ...state.network.customProxies[0],
          name: "Renamed",
          address: "http://new.example:8080",
        },
      ],
    });
    expect(successToast).toHaveBeenCalledWith("common.success.saved");
  });

  it("keeps add validation and deletes the selected proxy while switching to direct mode", async () => {
    render(<Others applyPatches={applyPatches} group="network" />);
    fireEvent.click(screen.getByRole("button", { name: "common.action.add" }));
    const modal = portal.mock.calls.at(-1)![1];

    await expect(modal.onOk()).rejects.toThrow("Invalid data");
    expect(errorToast).toHaveBeenCalledWith("common.error.invalidData");
    expect(patchNetwork).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "common.action.delete" }));
    expect(patchNetwork).toHaveBeenCalledWith({
      customProxies: [],
      proxy: { mode: 0, customProxyId: undefined },
    });
  });

  it("lets the selector shrink on small screens and keeps action buttons in a wrapping row", () => {
    render(<Others applyPatches={applyPatches} group="network" />);
    const selector = screen.getByRole("button", { name: "choose-system-proxy" }).parentElement!;

    expect(selector).toHaveClass("w-full", "min-w-0", "sm:max-w-[300px]");
    expect(selector).not.toHaveAttribute("style");
    expect(selector.parentElement).toHaveClass("flex-wrap", "min-w-0");
  });
  it("removes deleted custom proxy references from tool overrides while preserving other tools", async () => {
    (state.network as any).downloadToolProxies = {
      gdown: { mode: 2, customProxyId: "proxy-one" },
      "baidu-pcs-go": { mode: 2, customProxyId: "another-proxy" },
      "gallery-dl": { mode: 0 },
    };
    render(<Others applyPatches={applyPatches} group="network" />);
    fireEvent.click(screen.getByRole("button", { name: "common.action.delete" }));
    expect(patchNetwork).toHaveBeenCalledWith({
      customProxies: [],
      proxy: { mode: 0, customProxyId: undefined },
      downloadToolProxies: {
        "baidu-pcs-go": { mode: 2, customProxyId: "another-proxy" },
        "gallery-dl": { mode: 0 },
      },
    });
  });

  it("retains section search filtering within each selected group", () => {
    render(
      <>
        <Others applyPatches={applyPatches} group="network" query="telemetry" />
        <Others applyPatches={applyPatches} group="advanced" query="telemetry" />
      </>,
    );
    expect(screen.queryByRole("heading", { name: "configuration.others.networkTitle" })).toBeNull();
    expect(
      screen.getByRole("heading", { name: "configuration.others.advancedTitle" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("switch")).toBeInTheDocument();
    expect(screen.queryByRole("spinbutton")).toBeNull();
    expect(screen.queryByText("configuration.others.proxy")).toBeNull();
  });
  it("finds tool proxy settings by tool name without showing advanced fields", () => {
    render(<Others applyPatches={applyPatches} group="network" query="gdown" />);
    expect(screen.getByText("tool-proxy-selectors")).toBeInTheDocument();
    expect(screen.queryByRole("switch")).toBeNull();
    expect(screen.queryByRole("spinbutton")).toBeNull();
    expect(screen.queryByText("configuration.others.proxy")).toBeNull();
  });
});
