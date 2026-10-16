import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import DeviceMetadataEditor from "../../management/DeviceMetadataEditor";
import { managedServerApi } from "../serverApi";
import { FederationError } from "../transport";

import BApi from "@/sdk/BApi";
import { useUiOptionsStore } from "@/stores/options";

vi.mock("react-i18next", () => ({
  useTranslation: () => ({ t: (key: string) => key, i18n: { exists: () => true } }),
  initReactI18next: { type: "3rdParty", init: vi.fn() },
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    options: { setDeviceColor: vi.fn() },
    management: { getManagedDeviceName: vi.fn(), setManagedDeviceName: vi.fn() },
  },
}));
vi.mock("../serverApi", () => ({
  managedServerApi: { getDeviceName: vi.fn(), setDeviceName: vi.fn() },
}));
const snapshot = { name: "NAS", libraryEpoch: "epoch-one", environmentControlled: false };
const onSaved = vi.fn();
const open = async () => {
  render(<DeviceMetadataEditor busy={false} name="NAS" serverId="server-one" onSaved={onSaved} />);
  fireEvent.click(screen.getByRole("button", { name: "deviceAppearance.editMetadata" }));

  return screen.findByRole("textbox");
};

beforeEach(() => {
  vi.resetAllMocks();
  useUiOptionsStore.getState().update({ deviceColors: {} });
  vi.mocked(BApi.options.setDeviceColor).mockResolvedValue({ code: 0 } as never);
  vi.mocked(managedServerApi.getDeviceName).mockResolvedValue(snapshot);
  vi.mocked(managedServerApi.setDeviceName).mockImplementation(async (_id, current, name) => ({
    ...current,
    name: name ?? "Computer",
  }));
});
afterEach(cleanup);

describe("device information editor", () => {
  it("reads the live owner name, sends that name and epoch as the precondition, and reports the committed name", async () => {
    const input = await open();

    expect(input).toHaveValue("NAS");
    expect(managedServerApi.getDeviceName).toHaveBeenCalledWith(
      "server-one",
      expect.any(AbortSignal),
    );
    fireEvent.change(input, { target: { value: "  Living room  " } });
    fireEvent.click(screen.getByRole("button", { name: "deviceAppearance.saveName" }));
    await waitFor(() => expect(onSaved).toHaveBeenCalledWith("Living room"));
    expect(managedServerApi.setDeviceName).toHaveBeenCalledWith(
      "server-one",
      snapshot,
      "Living room",
    );
    expect(screen.getByRole("textbox")).toHaveValue("Living room");
    expect(screen.getByText("deviceAppearance.nameSaved")).toBeInTheDocument();
    expect(BApi.options.setDeviceColor).not.toHaveBeenCalled();
    expect(useUiOptionsStore.getState().data.deviceColors).toEqual({});
  });
  it("does not hide a rejected concurrent change or automatically retry it", async () => {
    const input = await open();

    vi.mocked(managedServerApi.setDeviceName).mockRejectedValueOnce(
      new FederationError("DeviceNameChanged", "Changed", 409),
    );
    fireEvent.change(input, { target: { value: "My edit" } });
    fireEvent.click(screen.getByRole("button", { name: "deviceAppearance.saveName" }));
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "federation.error.DeviceNameChanged",
    );
    expect(input).toHaveValue("My edit");
    expect(onSaved).not.toHaveBeenCalled();
    expect(managedServerApi.setDeviceName).toHaveBeenCalledTimes(1);
    vi.mocked(managedServerApi.getDeviceName).mockResolvedValue({
      ...snapshot,
      name: "Someone else's name",
    });
    fireEvent.click(screen.getByRole("button", { name: "federation.servers.name.reload" }));
    await waitFor(() => expect(screen.getByRole("textbox")).toHaveValue("Someone else's name"));
    fireEvent.change(screen.getByRole("textbox"), { target: { value: "My edit" } });
    fireEvent.click(screen.getByRole("button", { name: "deviceAppearance.saveName" }));
    await waitFor(() => expect(onSaved).toHaveBeenCalledWith("My edit"));
    expect(managedServerApi.setDeviceName).toHaveBeenLastCalledWith(
      "server-one",
      { ...snapshot, name: "Someone else's name" },
      "My edit",
    );
  });
  it("explains an environment-controlled name and disables writes", async () => {
    vi.mocked(managedServerApi.getDeviceName).mockResolvedValue({
      ...snapshot,
      environmentControlled: true,
    });
    const input = await open();

    expect(input).toBeDisabled();
    expect(screen.getByRole("status")).toHaveTextContent(
      "federation.error.DeviceNameEnvironmentControlled",
    );
    expect(screen.getByRole("button", { name: "deviceAppearance.saveName" })).toBeDisabled();
    expect(screen.queryByRole("button", { name: "federation.name.reset" })).not.toBeInTheDocument();
    expect(managedServerApi.setDeviceName).not.toHaveBeenCalled();
    fireEvent.change(screen.getByLabelText("deviceAppearance.editFor"), {
      target: { value: "#112233" },
    });
    fireEvent.click(screen.getByRole("button", { name: "deviceAppearance.saveColor" }));
    await waitFor(() =>
      expect(BApi.options.setDeviceColor).toHaveBeenCalledWith("server-one", { color: "#112233" }),
    );
  });
  it("resets through the same guarded owner mutation", async () => {
    await open();
    fireEvent.click(screen.getByRole("button", { name: "federation.name.reset" }));
    await waitFor(() => expect(onSaved).toHaveBeenCalledWith("Computer"));
    expect(managedServerApi.setDeviceName).toHaveBeenCalledWith("server-one", snapshot, null);
  });
  it("never offers a write when the current owner could not be read", async () => {
    vi.mocked(managedServerApi.getDeviceName).mockRejectedValue(
      new FederationError("ManagedSourceUnavailable", "Offline", 503),
    );
    render(
      <DeviceMetadataEditor busy={false} name="NAS" serverId="server-one" onSaved={onSaved} />,
    );
    fireEvent.click(screen.getByRole("button", { name: "deviceAppearance.editMetadata" }));
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "federation.error.ManagedSourceUnavailable",
    );
    expect(screen.getByRole("button", { name: "deviceAppearance.saveName" })).toBeDisabled();
    expect(managedServerApi.setDeviceName).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "deviceAppearance.saveColor" })).toBeEnabled();
    fireEvent.change(screen.getByLabelText("deviceAppearance.editFor"), {
      target: { value: "#445566" },
    });
    fireEvent.click(screen.getByRole("button", { name: "deviceAppearance.saveColor" }));
    expect(await screen.findByText("deviceAppearance.colorSaved")).toBeInTheDocument();
    expect(screen.getByRole("alert")).toHaveTextContent(
      "federation.error.ManagedSourceUnavailable",
    );
  });
  it("aborts its read on cancel and ignores a late answer", async () => {
    let resolve!: (value: typeof snapshot) => void;

    vi.mocked(managedServerApi.getDeviceName).mockImplementation(
      () =>
        new Promise((done) => {
          resolve = done;
        }),
    );
    render(
      <DeviceMetadataEditor busy={false} name="NAS" serverId="server-one" onSaved={onSaved} />,
    );
    fireEvent.click(screen.getByRole("button", { name: "deviceAppearance.editMetadata" }));
    const signal = vi.mocked(managedServerApi.getDeviceName).mock.calls[0][1]!;

    fireEvent.click(screen.getByRole("button", { name: "deviceAppearance.close" }));
    expect(signal.aborted).toBe(true);
    resolve(snapshot);
    await waitFor(() => expect(screen.queryByRole("textbox")).not.toBeInTheDocument());
    expect(onSaved).not.toHaveBeenCalled();
  });

  it("uses the local owner API with the same name and epoch precondition", async () => {
    vi.mocked(BApi.management.getManagedDeviceName).mockResolvedValue(snapshot);
    vi.mocked(BApi.management.setManagedDeviceName).mockResolvedValue({
      ...snapshot,
      name: "Studio",
    });
    render(<DeviceMetadataEditor local name="NAS" serverId="server-one" onSaved={onSaved} />);
    fireEvent.click(screen.getByRole("button", { name: "deviceAppearance.editMetadata" }));
    fireEvent.change(await screen.findByRole("textbox"), { target: { value: "Studio" } });
    fireEvent.click(screen.getByRole("button", { name: "deviceAppearance.saveName" }));
    await waitFor(() => expect(onSaved).toHaveBeenCalledWith("Studio"));
    expect(BApi.management.setManagedDeviceName).toHaveBeenCalledWith(
      { name: "Studio", expectedName: "NAS", libraryEpoch: "epoch-one" },
      { showErrorToast: false },
    );
    expect(managedServerApi.getDeviceName).not.toHaveBeenCalled();
    expect(BApi.options.setDeviceColor).not.toHaveBeenCalled();
  });

  it("localizes local owner conflicts and keeps both drafts until explicitly closed", async () => {
    vi.mocked(BApi.management.getManagedDeviceName).mockResolvedValue(snapshot);
    vi.mocked(BApi.management.setManagedDeviceName).mockRejectedValue({
      status: 409,
      error: { code: "DeviceNameChanged", message: "Changed elsewhere" },
    });
    render(<DeviceMetadataEditor local name="NAS" serverId="server-one" onSaved={onSaved} />);
    fireEvent.click(screen.getByRole("button", { name: "deviceAppearance.editMetadata" }));
    fireEvent.change(await screen.findByRole("textbox"), { target: { value: "Unsaved name" } });
    fireEvent.change(screen.getByLabelText("deviceAppearance.editFor"), {
      target: { value: "#abcdef" },
    });
    fireEvent.click(screen.getByRole("button", { name: "deviceAppearance.saveName" }));
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "federation.error.DeviceNameChanged",
    );
    expect(screen.getByRole("textbox")).toHaveValue("Unsaved name");
    expect(screen.getByLabelText("deviceAppearance.editFor")).toHaveValue("#abcdef");
    fireEvent.click(screen.getByRole("button", { name: "deviceAppearance.close" }));
    fireEvent.click(screen.getByRole("button", { name: "deviceAppearance.editMetadata" }));
    expect(await screen.findByRole("textbox")).toHaveValue("NAS");
    expect(screen.getByLabelText("deviceAppearance.editFor")).not.toHaveValue("#abcdef");
    expect(BApi.options.setDeviceColor).not.toHaveBeenCalled();
  });

  it.each(["name", "color"] as const)("keeps the panel open while saving %s", async (field) => {
    let finish!: () => void;

    if (field === "name")
      vi.mocked(managedServerApi.setDeviceName).mockImplementationOnce(
        () =>
          new Promise((resolve) => {
            finish = () => resolve(snapshot);
          }),
      );
    else
      vi.mocked(BApi.options.setDeviceColor).mockImplementationOnce(
        () =>
          new Promise((resolve) => {
            finish = () => resolve({ code: 0 } as never);
          }),
      );
    await open();
    fireEvent.click(
      screen.getByRole("button", {
        name: field === "name" ? "deviceAppearance.saveName" : "deviceAppearance.saveColor",
      }),
    );
    const close = screen.getByRole("button", { name: "deviceAppearance.close" });

    expect(close).toBeDisabled();
    fireEvent.click(close);
    expect(screen.getByLabelText("federation.name.label")).toBeInTheDocument();
    finish();
    await waitFor(() => expect(close).toBeEnabled());
    fireEvent.click(close);
    expect(screen.queryByLabelText("federation.name.label")).not.toBeInTheDocument();
  });

  it("keeps the unsaved name and color when saving the color fails", async () => {
    vi.mocked(BApi.options.setDeviceColor).mockResolvedValueOnce({
      code: 500,
      message: "Color save failed",
    } as never);
    await open();
    fireEvent.change(screen.getByRole("textbox"), { target: { value: "Unsaved name" } });
    fireEvent.change(screen.getByLabelText("deviceAppearance.editFor"), {
      target: { value: "#abcdef" },
    });
    fireEvent.click(screen.getByRole("button", { name: "deviceAppearance.saveColor" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Color save failed");
    expect(screen.getByRole("textbox")).toHaveValue("Unsaved name");
    expect(screen.getByLabelText("deviceAppearance.editFor")).toHaveValue("#abcdef");
    expect(managedServerApi.setDeviceName).not.toHaveBeenCalled();
    expect(useUiOptionsStore.getState().data.deviceColors).toEqual({});
  });
});
