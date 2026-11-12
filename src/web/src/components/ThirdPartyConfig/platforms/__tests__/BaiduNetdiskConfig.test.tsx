import type { ReactNode } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import BaiduNetdiskConfig from "../BaiduNetdiskConfig";
import { ThirdPartyId } from "@/sdk/constants";

const api = vi.hoisted(() => ({ getBaiduPcsAccounts: vi.fn(), loginBaiduPcsAccount: vi.fn() }));
const network = vi.hoisted(() => ({ data: {} as any, patch: vi.fn(), update: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({
  default: { baiduPcs: api, options: { patchNetworkOptions: network.patch } },
}));
vi.mock("@/stores/options", () => ({
  useNetworkOptionsStore: Object.assign((select: any) => select({ data: network.data }), {
    getState: () => ({ data: network.data, update: network.update }),
  }),
}));
vi.mock("react-hot-toast", () => ({ default: { success: vi.fn() } }));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/components/Dependency/DependencyManager", () => ({
  default: () => <span>Dependency</span>,
}));
vi.mock("@/components/ExternalLink", () => ({
  default: ({ children }: { children: ReactNode }) => <span>{children}</span>,
}));
vi.mock("@/components/bakaui", () => ({
  Select: ({
    label,
    dataSource,
    selectedKeys,
    disabledKeys,
    onSelectionChange,
    isDisabled,
  }: any) => (
    <select
      aria-label={label || "Source proxy"}
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
  ),
  Button: ({
    children,
    onPress,
    isDisabled,
  }: {
    children: ReactNode;
    onPress: () => void;
    isDisabled?: boolean;
  }) => (
    <button disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Input: ({
    label,
    value,
    onValueChange,
    type,
  }: {
    label: string;
    value: string;
    onValueChange: (value: string) => void;
    type?: string;
  }) => (
    <label>
      {label}
      <input type={type} value={value} onChange={(event) => onValueChange(event.target.value)} />
    </label>
  ),
  Switch: ({
    children,
    isSelected,
    onValueChange,
  }: {
    children: ReactNode;
    isSelected: boolean;
    onValueChange: (value: boolean) => void;
  }) => (
    <label>
      <input
        type="checkbox"
        checked={isSelected}
        onChange={(event) => onValueChange(event.target.checked)}
      />
      {children}
    </label>
  ),
  Spinner: () => null,
}));

beforeEach(() => {
  api.getBaiduPcsAccounts.mockReset().mockResolvedValue({ code: 0, data: [] });
  api.loginBaiduPcsAccount.mockReset().mockResolvedValue({ code: 0, data: { id: "account-1" } });
  network.data = {
    proxy: { mode: 1 },
    btProxy: { mode: 0 },
    thirdPartyProxies: { [ThirdPartyId.GoogleDrive]: { mode: 0 } },
    downloadToolProxies: { gdown: { mode: 0 } },
    customProxies: [{ id: "selected", name: "Selected", address: "socks5://proxy.invalid:1080" }],
  };
  network.patch.mockReset().mockResolvedValue({ code: 0 });
  network.update.mockReset().mockImplementation((patch: any) => {
    network.data = { ...network.data, ...patch };
  });
});
afterEach(cleanup);

describe("Baidu credentials", () => {
  it("offers inherited source and Baidu-only tool proxies without mutating saved configuration", async () => {
    await act(async () => {
      render(<BaiduNetdiskConfig />);
    });

    expect(screen.getAllByRole("combobox")).toHaveLength(2);
    expect(screen.getByRole("combobox", { name: "Source proxy" })).toHaveValue("inherit");
    expect(screen.getByRole("combobox", { name: "BaiduPCS-Go" })).toHaveValue("inherit");
    expect(network.patch).not.toHaveBeenCalled();
  });

  it("saves the Baidu source override through the shared field and preserves unrelated settings", async () => {
    render(<BaiduNetdiskConfig />);
    fireEvent.change(screen.getByRole("combobox", { name: "Source proxy" }), {
      target: { value: "custom:selected" },
    });

    await waitFor(() =>
      expect(network.patch).toHaveBeenCalledWith({
        thirdPartyProxies: {
          [ThirdPartyId.GoogleDrive]: { mode: 0 },
          [ThirdPartyId.BaiduNetdisk]: { mode: 2, customProxyId: "selected" },
        },
      }),
    );
    expect(network.data.downloadToolProxies).toEqual({ gdown: { mode: 0 } });
    expect(network.data.proxy).toEqual({ mode: 1 });
    expect(network.data.btProxy).toEqual({ mode: 0 });
    expect(api.loginBaiduPcsAccount).not.toHaveBeenCalled();
  });

  it("saves explicit Baidu tool direct mode then restores inheritance without dropping other tools", async () => {
    const view = render(<BaiduNetdiskConfig />);
    const name = "BaiduPCS-Go";
    fireEvent.change(screen.getByRole("combobox", { name }), { target: { value: "0" } });
    await waitFor(() => expect(network.update).toHaveBeenCalledTimes(1));
    expect(network.patch).toHaveBeenLastCalledWith({
      downloadToolProxies: {
        gdown: { mode: 0 },
        "baidu-pcs-go": { mode: 0, customProxyId: undefined },
      },
    });
    view.rerender(<BaiduNetdiskConfig />);
    expect(screen.getByRole("combobox", { name })).toHaveValue("0");
    fireEvent.change(screen.getByRole("combobox", { name }), { target: { value: "inherit" } });
    await waitFor(() => expect(network.update).toHaveBeenCalledTimes(2));
    expect(network.patch).toHaveBeenLastCalledWith({ downloadToolProxies: { gdown: { mode: 0 } } });
    expect(network.data.thirdPartyProxies).toEqual({ [ThirdPartyId.GoogleDrive]: { mode: 0 } });
  });

  it("imports a cookie through the account API and clears the credential fields after success", async () => {
    render(<BaiduNetdiskConfig />);
    fireEvent.change(screen.getByLabelText("cloudDownload.account.name"), {
      target: { value: "Primary" },
    });
    fireEvent.change(screen.getByLabelText("Cookie"), {
      target: { value: "BDUSS=test-value; STOKEN=another-value" },
    });
    fireEvent.click(screen.getByRole("button", { name: "cloudDownload.account.login" }));
    await waitFor(() =>
      expect(api.loginBaiduPcsAccount).toHaveBeenCalledWith({
        name: "Primary",
        cookies: "BDUSS=test-value; STOKEN=another-value",
      }),
    );
    await waitFor(() => expect(screen.getByLabelText("Cookie")).toHaveValue(""));
    expect(screen.getByLabelText("Cookie")).toHaveAttribute("type", "password");
  });

  it("requires STOKEN as well as BDUSS when entering separate credentials", async () => {
    render(<BaiduNetdiskConfig />);
    fireEvent.click(screen.getByLabelText("cloudDownload.account.cookieMode"));
    fireEvent.change(screen.getByLabelText("cloudDownload.account.name"), {
      target: { value: "Primary" },
    });
    fireEvent.change(screen.getByLabelText("BDUSS"), { target: { value: "identity" } });
    expect(screen.getByRole("button", { name: "cloudDownload.account.login" })).toBeDisabled();
    fireEvent.change(screen.getByLabelText("STOKEN"), { target: { value: "share-session" } });
    expect(screen.getByRole("button", { name: "cloudDownload.account.login" })).not.toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "cloudDownload.account.login" }));
    await waitFor(() =>
      expect(api.loginBaiduPcsAccount).toHaveBeenCalledWith({
        name: "Primary",
        bduss: "identity",
        stoken: "share-session",
      }),
    );
  });
});
