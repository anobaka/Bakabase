import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import BtSettingsPanel from "../BtSettingsPanel";

const mocks = vi.hoisted(() => ({
  save: vi.fn(),
  options: {} as {
    btProxy?: { mode: number; customProxyId?: string };
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
    onSelectionChange,
  }: {
    label: string;
    dataSource: { value: string; label: string }[];
    selectedKeys: string[];
    onSelectionChange: (v: Set<string>) => void;
  }) => (
    <select
      aria-label={label}
      value={selectedKeys[0]}
      onChange={(e) => onSelectionChange(new Set([e.target.value]))}
    >
      {dataSource.map((x) => (
        <option key={x.value} value={x.value}>
          {x.label}
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
  it("defaults to explicit direct access and only offers supported saved SOCKS5 proxies", () => {
    mocks.options = {
      customProxies: [
        { id: "socks", name: "My SOCKS", address: "socks5://127.0.0.1:1080" },
        { id: "http", name: "HTTP proxy", address: "http://127.0.0.1:8080" },
        { id: "credentials", name: "Authenticated SOCKS", address: "socks5://user:pass@host:1080" },
      ],
    };
    render(<BtSettingsPanel />);
    expect(screen.getByRole("combobox")).toHaveValue("direct");
    expect(screen.getAllByRole("option")).toHaveLength(2);
    expect(screen.getByText("My SOCKS")).toBeInTheDocument();
    expect(screen.queryByText("HTTP proxy")).not.toBeInTheDocument();
    expect(screen.queryByText("Authenticated SOCKS")).not.toBeInTheDocument();
  });

  it("saves only the BT proxy and does not change the global proxy", () => {
    mocks.options = {
      customProxies: [{ id: "socks", name: "My SOCKS", address: "socks5://127.0.0.1:1080" }],
    };
    render(<BtSettingsPanel />);
    fireEvent.change(screen.getByRole("combobox"), { target: { value: "socks" } });
    expect(mocks.save).toHaveBeenLastCalledWith({ btProxy: { mode: 2, customProxyId: "socks" } });
    fireEvent.change(screen.getByRole("combobox"), { target: { value: "direct" } });
    expect(mocks.save).toHaveBeenLastCalledWith({ btProxy: { mode: 0 } });
  });

  it("does not present a missing or invalid configured proxy as direct access", () => {
    mocks.options = { btProxy: { mode: 2, customProxyId: "removed" } };
    render(<BtSettingsPanel />);
    expect(screen.getByRole("combobox")).toHaveValue("removed");
    expect(screen.getByRole("alert")).toHaveTextContent("bt.settings.proxy.unavailableDescription");
    expect(mocks.save).not.toHaveBeenCalled();
  });
});
