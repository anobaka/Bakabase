import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import BtSettingsPanel from "../BtSettingsPanel";

const mocks = vi.hoisted(() => ({
  save: vi.fn(),
  options: {} as {
    btProxy?: { mode: number; customProxyId?: string };
    proxy?: { mode: number; customProxyId?: string };
    customProxies?: { id: string; name: string; address: string }[];
  },
}));
vi.mock("@/stores/options", () => ({
  useNetworkOptionsStore: (selector: (value: unknown) => unknown) =>
    selector({ data: mocks.options }),
}));
vi.mock("@/sdk/BApi", () => ({ default: { options: { patchNetworkOptions: vi.fn() } } }));
vi.mock("@/components/ThirdPartyConfig/base/useAutoSaveToast", () => ({
  default: () => mocks.save,
}));
vi.mock("@/components/NavigateButton", () => ({ default: () => null }));
vi.mock("@/components/bakaui", () => ({
  NumberInput: () => null,
  Select: ({
    label,
    dataSource,
    selectedKeys,
    disabledKeys,
    onSelectionChange,
  }: {
    label: string;
    dataSource: { value: string; label: string; description?: string }[];
    selectedKeys: string[];
    disabledKeys: string[];
    onSelectionChange: (value: Set<string>) => void;
  }) => (
    <select
      aria-label={label}
      value={selectedKeys[0]}
      onChange={(event) => onSelectionChange(new Set([event.target.value]))}
    >
      {dataSource.map((item) => (
        <option
          key={item.value}
          value={item.value}
          disabled={disabledKeys.includes(item.value)}
          title={item.description}
        >
          {item.label}
        </option>
      ))}
    </select>
  ),
}));
beforeEach(() => {
  vi.clearAllMocks();
  mocks.options = {};
});
describe("independent BT network settings", () => {
  it("defaults to direct even when the global proxy uses the system, and exposes supported HTTP/SOCKS5 choices", () => {
    mocks.options = {
      proxy: { mode: 1 },
      customProxies: [
        { id: "socks", name: "My SOCKS", address: "socks5://127.0.0.1:1080" },
        { id: "http", name: "HTTP proxy", address: "http://127.0.0.1:8080" },
        { id: "https", name: "HTTPS proxy", address: "https://127.0.0.1:8080" },
        { id: "credentials", name: "Authenticated SOCKS", address: "socks5://user:pass@host:1080" },
      ],
    };
    render(<BtSettingsPanel />);
    expect(screen.getByRole("combobox")).toHaveValue("0");
    expect(
      screen.getByRole("option", { name: "configuration.others.proxy.useSystem" }),
    ).toBeEnabled();
    expect(screen.getByRole("option", { name: /My SOCKS/ })).toBeEnabled();
    expect(screen.getByRole("option", { name: /^HTTP proxy/ })).toBeEnabled();
    expect(screen.getByRole("option", { name: /^HTTPS proxy/ })).toBeDisabled();
    expect(screen.getByRole("option", { name: /Authenticated SOCKS/ })).toBeDisabled();
    expect(screen.getByRole("option", { name: /^HTTPS proxy/ })).toHaveAttribute(
      "title",
      "bt.settings.proxy.unsupported",
    );
  });
  it("saves direct, system, and saved selections only to BT", () => {
    mocks.options = {
      customProxies: [{ id: "socks", name: "My SOCKS", address: "socks5://127.0.0.1:1080" }],
    };
    render(<BtSettingsPanel />);
    fireEvent.change(screen.getByRole("combobox"), { target: { value: "custom:socks" } });
    expect(mocks.save).toHaveBeenLastCalledWith({ btProxy: { mode: 2, customProxyId: "socks" } });
    fireEvent.change(screen.getByRole("combobox"), { target: { value: "1" } });
    expect(mocks.save).toHaveBeenLastCalledWith({ btProxy: { mode: 1, customProxyId: undefined } });
    fireEvent.change(screen.getByRole("combobox"), { target: { value: "0" } });
    expect(mocks.save).toHaveBeenLastCalledWith({ btProxy: { mode: 0, customProxyId: undefined } });
    expect(mocks.save.mock.calls.every(([patch]) => Object.keys(patch).join() === "btProxy")).toBe(
      true,
    );
  });
  it("preserves a configured system selection without reporting it unavailable", () => {
    mocks.options = { btProxy: { mode: 1 } };
    render(<BtSettingsPanel />);
    expect(screen.getByRole("combobox")).toHaveValue("1");
    expect(screen.queryByRole("alert")).toBeNull();
    expect(mocks.save).not.toHaveBeenCalled();
  });
  it("does not present a missing configured proxy as direct access", () => {
    mocks.options = { btProxy: { mode: 2, customProxyId: "removed" } };
    render(<BtSettingsPanel />);
    expect(screen.getByRole("combobox")).toHaveValue("custom:removed");
    expect(screen.getByRole("alert")).toHaveTextContent("bt.settings.proxy.unavailableDescription");
    expect(mocks.save).not.toHaveBeenCalled();
  });
});
