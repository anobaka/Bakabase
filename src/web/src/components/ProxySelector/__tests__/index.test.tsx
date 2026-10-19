import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import ProxySelector from "..";
const change = vi.fn();
vi.mock("@/components/bakaui", () => ({
  Select: ({ dataSource, selectedKeys, disabledKeys, onSelectionChange }: any) => (
    <select
      value={selectedKeys[0]}
      onChange={(event) => onSelectionChange(new Set([event.target.value]))}
    >
      {dataSource.map((item: any) => (
        <option value={item.value} key={item.value} disabled={disabledKeys.includes(item.value)}>
          {item.label}
        </option>
      ))}
    </select>
  ),
}));
beforeEach(() => change.mockReset());
describe("shared proxy selector", () => {
  it("offers direct and system choices without inheriting global settings by default", () => {
    render(<ProxySelector onChange={change} />);
    expect(screen.getByRole("combobox")).toHaveValue("0");
    expect(screen.getAllByRole("option")).toHaveLength(2);
    fireEvent.change(screen.getByRole("combobox"), { target: { value: "1" } });
    expect(change).toHaveBeenLastCalledWith({ mode: 1, customProxyId: undefined });
  });
  it("supports explicit global inheritance for site overrides", () => {
    const view = render(<ProxySelector allowInherit onChange={change} />);
    expect(screen.getByRole("combobox")).toHaveValue("inherit");
    view.rerender(<ProxySelector allowInherit value={{ mode: 0 }} onChange={change} />);
    fireEvent.change(screen.getByRole("combobox"), { target: { value: "inherit" } });
    expect(change).toHaveBeenLastCalledWith(undefined);
  });
  it("keeps custom identifiers distinct from mode values", () => {
    render(
      <ProxySelector
        value={{ mode: 2, customProxyId: "1" }}
        customProxies={[{ id: "1", name: "Proxy one", address: "http://proxy:80" }]}
        onChange={change}
      />,
    );
    expect(screen.getByRole("combobox")).toHaveValue("custom:1");
    fireEvent.change(screen.getByRole("combobox"), { target: { value: "custom:1" } });
    expect(change).toHaveBeenLastCalledWith({ mode: 2, customProxyId: "1" });
  });
  it("keeps unsupported and missing choices visible but never saves them", () => {
    render(
      <ProxySelector
        value={{ mode: 2, customProxyId: "missing" }}
        customProxies={[{ id: "unsupported", address: "https://proxy:443" }]}
        isCustomProxySupported={() => false}
        unsupportedDescription="Unsupported protocol"
        onChange={change}
      />,
    );
    expect(screen.getByRole("combobox")).toHaveValue("custom:missing");
    expect(
      screen.getByRole("option", { name: "configuration.others.proxy.unavailable" }),
    ).toBeDisabled();
    fireEvent.change(screen.getByRole("combobox"), { target: { value: "custom:unsupported" } });
    expect(change).not.toHaveBeenCalled();
  });
});
