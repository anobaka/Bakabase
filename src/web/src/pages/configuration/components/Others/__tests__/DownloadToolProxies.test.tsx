import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import DownloadToolProxies from "../DownloadToolProxies";

const mocks = vi.hoisted(() => ({
  network: {} as any,
  patch: vi.fn(),
  update: vi.fn(),
  success: vi.fn(),
  returnMissingKey: false,
}));

vi.mock("@/sdk/BApi", () => ({ default: { options: { patchNetworkOptions: mocks.patch } } }));
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: { defaultValue?: string }) =>
      key === "configuration.downloadTools.name.sourceResolver"
        ? "Localized source resolver"
        : mocks.returnMissingKey
          ? key
          : (options?.defaultValue ?? key),
  }),
}));
vi.mock("react-hot-toast", () => ({ default: { success: mocks.success } }));
vi.mock("@/sdk/constants", () => ({
  DownloadToolProxyTargets: [
    { id: "baidu-pcs-go", name: "BaiduPCS-Go", supportedProxySchemes: ["http", "socks5"] },
    { id: "gdown", name: "gdown", supportedProxySchemes: ["http", "socks5", "socks5h"] },
    { id: "gallery-dl", name: "gallery-dl", supportedProxySchemes: ["http", "socks5"] },
    { id: "sourceResolver", name: "Source link resolver", supportedProxySchemes: ["http"] },
  ],
}));
vi.mock("@/stores/options", () => ({
  useNetworkOptionsStore: Object.assign((select: any) => select({ data: mocks.network }), {
    getState: () => ({ data: mocks.network, update: mocks.update }),
  }),
}));
vi.mock("@/components/bakaui", () => ({
  Select: ({
    label,
    description,
    dataSource,
    selectedKeys,
    disabledKeys,
    onSelectionChange,
    isDisabled,
  }: any) => (
    <label>
      {label}
      <select
        aria-label={label}
        value={selectedKeys[0]}
        disabled={isDisabled}
        onChange={(event) => onSelectionChange(new Set([event.target.value]))}
      >
        {dataSource.map((item: any) => (
          <option key={item.value} value={item.value} disabled={disabledKeys.includes(item.value)}>
            {item.label}
          </option>
        ))}
      </select>
      <span>{description}</span>
    </label>
  ),
}));

beforeEach(() => {
  mocks.returnMissingKey = false;
  mocks.network = {
    proxy: { mode: 1 },
    btProxy: { mode: 0 },
    thirdPartyProxies: { "1": { mode: 2, customProxyId: "http" } },
    customProxies: [
      { id: "http", name: "HTTP proxy", address: "http://proxy.test:8080" },
      { id: "socks", name: "SOCKS proxy", address: "socks5://proxy.test:1080" },
      { id: "https", name: "HTTPS proxy", address: "https://proxy.test:443" },
    ],
    downloadToolProxies: { "gallery-dl": { mode: 0 } },
  };
  mocks.patch.mockReset().mockResolvedValue({ code: 0 });
  mocks.update.mockReset().mockImplementation((patch: any) => {
    mocks.network = { ...mocks.network, ...patch };
  });
  mocks.success.mockReset();
});
afterEach(cleanup);

describe("per-tool network proxy settings", () => {
  it("renders metadata tools and distinguishes inherited settings from explicit direct connections", () => {
    render(<DownloadToolProxies />);
    expect(screen.getAllByRole("combobox")).toHaveLength(4);
    expect(screen.getByRole("combobox", { name: "BaiduPCS-Go" })).toHaveValue("inherit");
    expect(screen.getByRole("combobox", { name: "gdown" })).toHaveValue("inherit");
    expect(screen.getByRole("combobox", { name: "gallery-dl" })).toHaveValue("0");
    expect(mocks.patch).not.toHaveBeenCalled();
  });

  it("falls back to brand names when the missing-key handler ignores defaultValue while preserving translations", () => {
    mocks.returnMissingKey = true;
    render(<DownloadToolProxies />);
    expect(screen.getByRole("combobox", { name: "BaiduPCS-Go" })).toHaveValue("inherit");
    expect(screen.getByRole("combobox", { name: "gdown" })).toHaveValue("inherit");
    expect(screen.getByRole("combobox", { name: "Localized source resolver" })).toHaveValue(
      "inherit",
    );
    expect(screen.queryByText("configuration.downloadTools.name.baidu-pcs-go")).toBeNull();
    expect(mocks.patch).not.toHaveBeenCalled();
  });

  it("saves one tool while preserving other overrides and all site/global/BT settings", async () => {
    render(<DownloadToolProxies />);
    fireEvent.change(screen.getByRole("combobox", { name: "gdown" }), {
      target: { value: "custom:socks" },
    });
    await waitFor(() => expect(mocks.update).toHaveBeenCalled());
    expect(mocks.patch).toHaveBeenCalledWith({
      downloadToolProxies: {
        "gallery-dl": { mode: 0 },
        gdown: { mode: 2, customProxyId: "socks" },
      },
    });
    expect(mocks.network.proxy).toEqual({ mode: 1 });
    expect(mocks.network.btProxy).toEqual({ mode: 0 });
    expect(mocks.network.thirdPartyProxies).toEqual({ "1": { mode: 2, customProxyId: "http" } });
    fireEvent.change(screen.getByRole("combobox", { name: "BaiduPCS-Go" }), {
      target: { value: "1" },
    });
    await waitFor(() => expect(mocks.patch).toHaveBeenCalledTimes(2));
    expect(mocks.patch).toHaveBeenLastCalledWith({
      downloadToolProxies: {
        "gallery-dl": { mode: 0 },
        gdown: { mode: 2, customProxyId: "socks" },
        "baidu-pcs-go": { mode: 1, customProxyId: undefined },
      },
    });
  });

  it("removes only the chosen override when reverting to inherited settings", async () => {
    render(<DownloadToolProxies />);
    fireEvent.change(screen.getByRole("combobox", { name: "gallery-dl" }), {
      target: { value: "inherit" },
    });
    await waitFor(() => expect(mocks.patch).toHaveBeenCalledWith({ downloadToolProxies: {} }));
  });

  it("keeps an explicit direct override instead of deleting it", async () => {
    render(<DownloadToolProxies />);
    fireEvent.change(screen.getByRole("combobox", { name: "gdown" }), { target: { value: "0" } });
    await waitFor(() =>
      expect(mocks.patch).toHaveBeenCalledWith({
        downloadToolProxies: {
          "gallery-dl": { mode: 0 },
          gdown: { mode: 0, customProxyId: undefined },
        },
      }),
    );
  });

  it("keeps missing or incompatible saved proxies visible without saving or falling back silently", () => {
    mocks.network.downloadToolProxies.gdown = { mode: 2, customProxyId: "removed" };
    render(<DownloadToolProxies />);
    const select = screen.getByRole("combobox", { name: "gdown" });
    expect(select).toHaveValue("custom:removed");
    expect(
      within(select).getByRole("option", { name: "configuration.others.proxy.unavailable" }),
    ).toBeDisabled();
    expect(
      within(select).getByRole("option", { name: "HTTPS proxy (https://proxy.test:443)" }),
    ).toBeDisabled();
    expect(screen.getByRole("alert")).toHaveTextContent(
      "configuration.downloadTools.proxy.unavailable",
    );
    fireEvent.change(select, { target: { value: "custom:https" } });
    expect(mocks.patch).not.toHaveBeenCalled();
    expect(mocks.update).not.toHaveBeenCalled();
  });

  it("handles a fresh server's null proxy list and leaves inherited values untouched", () => {
    mocks.network.customProxies = null;
    mocks.network.downloadToolProxies = null;
    render(<DownloadToolProxies />);
    expect(
      screen
        .getAllByRole("combobox")
        .every((select) => (select as HTMLSelectElement).value === "inherit"),
    ).toBe(true);
    expect(mocks.patch).not.toHaveBeenCalled();
  });

  it("does not update confirmed state on a rejected save and allows retry", async () => {
    mocks.patch.mockResolvedValueOnce({ code: 400 });
    render(<DownloadToolProxies />);
    fireEvent.change(screen.getByRole("combobox", { name: "gdown" }), { target: { value: "0" } });
    await screen.findByText("configuration.downloadTools.proxy.saveFailed");
    expect(mocks.update).not.toHaveBeenCalled();
    expect(mocks.success).not.toHaveBeenCalled();
    expect(screen.getByRole("combobox", { name: "gdown" })).toHaveValue("inherit");
    fireEvent.change(screen.getByRole("combobox", { name: "gdown" }), { target: { value: "0" } });
    await waitFor(() => expect(mocks.update).toHaveBeenCalledTimes(1));
    expect(screen.queryByText("configuration.downloadTools.proxy.saveFailed")).toBeNull();
  });

  it("serializes dictionary saves while a request is pending", async () => {
    let complete!: (value: { code: number }) => void;
    mocks.patch.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          complete = resolve;
        }),
    );
    render(<DownloadToolProxies />);
    fireEvent.change(screen.getByRole("combobox", { name: "gdown" }), { target: { value: "0" } });
    expect(
      screen.getAllByRole("combobox").every((select) => (select as HTMLSelectElement).disabled),
    ).toBe(true);
    fireEvent.change(screen.getByRole("combobox", { name: "BaiduPCS-Go" }), {
      target: { value: "0" },
    });
    expect(mocks.patch).toHaveBeenCalledTimes(1);
    complete({ code: 0 });
    await waitFor(() => expect(screen.getByRole("combobox", { name: "gdown" })).not.toBeDisabled());
  });
});
