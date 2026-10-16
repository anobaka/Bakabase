import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const save = vi.hoisted(() => vi.fn());

vi.mock("@/sdk/BApi", () => ({ default: { options: { setDeviceColor: save } } }));
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: { name?: string }) =>
      key === "deviceAppearance.source"
        ? `Source device: ${options?.name}`
        : key === "deviceAppearance.device"
          ? `Device: ${options?.name}`
          : key,
  }),
}));

import DeviceDot from "../DeviceDot";
import DeviceColorEditor from "../DeviceColorEditor";
import { deviceColor } from "../resourceIdentity";

import { useUiOptionsStore } from "@/stores/options";

describe("device appearance", () => {
  beforeEach(() => {
    act(() => useUiOptionsStore.getState().update({ deviceColors: {} }));
    save.mockReset().mockResolvedValue({ code: 0 });
  });

  it("keeps automatic colors stable and ignores invalid saved values", () => {
    const automatic = deviceColor("owner-a");

    expect(deviceColor("owner-a", { "owner-b": "#ffffff" })).toBe(automatic);
    expect(deviceColor("owner-a", { "owner-a": "url(invalid)" })).toBe(automatic);
    expect(deviceColor("owner-a", { "owner-a": "#abcdef" })).toBe("#abcdef");
  });

  it("updates every dot when this server's shared options change", () => {
    render(
      <>
        <DeviceDot isSource name="NAS" serverId="owner-a" />
        <DeviceDot name="NAS renamed" serverId="owner-a" />
      </>,
    );
    act(() => useUiOptionsStore.getState().update({ deviceColors: { "owner-a": "#112233" } }));
    expect(screen.getByTitle("Source device: NAS").style.backgroundColor).toBe("rgb(17, 34, 51)");
    expect(screen.getByTitle("Device: NAS renamed").style.backgroundColor).toBe("rgb(17, 34, 51)");
  });

  it("opens the picker with the same automatic purple as its dot", () => {
    render(<DeviceColorEditor name="NAS" serverId="device-11" />);
    const dot = screen.getByTitle("Device: NAS");
    const expected = deviceColor("device-11");

    expect(dot).toHaveStyle({ backgroundColor: expected });
    fireEvent.click(screen.getByRole("button", { name: "deviceAppearance.editFor" }));
    expect(screen.getByLabelText("deviceAppearance.editFor")).toHaveValue(expected);
    expect(save).not.toHaveBeenCalled();
  });

  it("tracks loaded options until edited, preserves a draft on hub updates and discards it on cancel", () => {
    render(<DeviceColorEditor name="NAS" serverId="device-11" />);
    fireEvent.click(screen.getByRole("button", { name: "deviceAppearance.editFor" }));
    const input = screen.getByLabelText("deviceAppearance.editFor");

    act(() => useUiOptionsStore.getState().update({ deviceColors: { "device-11": "#112233" } }));
    expect(input).toHaveValue("#112233");
    fireEvent.change(input, { target: { value: "#abcdef" } });
    act(() => useUiOptionsStore.getState().update({ deviceColors: { "device-11": "#445566" } }));
    expect(input).toHaveValue("#abcdef");
    fireEvent.click(screen.getByRole("button", { name: "federation.cancel" }));
    fireEvent.click(screen.getByRole("button", { name: "deviceAppearance.editFor" }));
    expect(screen.getByLabelText("deviceAppearance.editFor")).toHaveValue("#445566");
    expect(save).not.toHaveBeenCalled();
  });

  it("restores the automatic preview in embedded mode and still follows later preferences", async () => {
    act(() => useUiOptionsStore.getState().update({ deviceColors: { "device-11": "#123456" } }));
    render(<DeviceColorEditor embedded name="NAS" serverId="device-11" />);
    expect(screen.getByLabelText("deviceAppearance.editFor")).toHaveValue("#123456");
    fireEvent.click(screen.getByRole("button", { name: "deviceAppearance.automatic" }));
    await waitFor(() =>
      expect(screen.getByLabelText("deviceAppearance.editFor")).toHaveValue(
        deviceColor("device-11"),
      ),
    );
    act(() => useUiOptionsStore.getState().update({ deviceColors: { "device-11": "#789abc" } }));
    expect(screen.getByLabelText("deviceAppearance.editFor")).toHaveValue("#789abc");
    expect(save).toHaveBeenCalledWith("device-11", { color: undefined });
  });

  it("saves one device without replacing another's preference and can restore automatic", async () => {
    act(() => useUiOptionsStore.getState().update({ deviceColors: { "owner-b": "#123456" } }));
    render(<DeviceColorEditor name="NAS" serverId="owner-a" />);
    fireEvent.click(screen.getByRole("button", { name: "deviceAppearance.editFor" }));
    fireEvent.change(screen.getByLabelText("deviceAppearance.editFor"), {
      target: { value: "#aabbcc" },
    });
    fireEvent.click(screen.getByRole("button", { name: "federation.save" }));
    await waitFor(() => expect(save).toHaveBeenCalledWith("owner-a", { color: "#aabbcc" }));
    await waitFor(() =>
      expect(useUiOptionsStore.getState().data.deviceColors).toEqual({
        "owner-a": "#aabbcc",
        "owner-b": "#123456",
      }),
    );
    fireEvent.click(screen.getByRole("button", { name: "deviceAppearance.editFor" }));
    fireEvent.click(screen.getByRole("button", { name: "deviceAppearance.automatic" }));
    await waitFor(() =>
      expect(useUiOptionsStore.getState().data.deviceColors).toEqual({ "owner-b": "#123456" }),
    );
  });

  it("keeps the saved color when the server refuses a change", async () => {
    save.mockResolvedValue({ code: 400, message: "Please try again" });
    render(<DeviceColorEditor name="NAS" serverId="owner-a" />);
    fireEvent.click(screen.getByRole("button", { name: "deviceAppearance.editFor" }));
    fireEvent.click(screen.getByRole("button", { name: "federation.save" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Please try again");
    expect(useUiOptionsStore.getState().data.deviceColors).toEqual({});
  });
});
