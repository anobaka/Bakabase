import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ManagedDeviceNameEditor from "../components/ManagedDeviceNameEditor";
import { managedServerApi } from "../serverApi";
import { FederationError } from "../transport";

vi.mock("react-i18next", () => ({
  useTranslation: () => ({ t: (key: string) => key, i18n: { exists: () => true } }),
  initReactI18next: { type: "3rdParty", init: vi.fn() },
}));
vi.mock("../serverApi", () => ({
  managedServerApi: { getDeviceName: vi.fn(), setDeviceName: vi.fn() },
}));
const snapshot = { name: "NAS", libraryEpoch: "epoch-one", environmentControlled: false };
const onSaved = vi.fn();
const open = async () => {
  render(<ManagedDeviceNameEditor busy={false} serverId="server-one" onSaved={onSaved} />);
  fireEvent.click(screen.getByRole("button", { name: "federation.servers.name.edit" }));

  return screen.findByRole("textbox");
};

beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(managedServerApi.getDeviceName).mockResolvedValue(snapshot);
  vi.mocked(managedServerApi.setDeviceName).mockImplementation(async (_id, current, name) => ({
    ...current,
    name: name ?? "Computer",
  }));
});
afterEach(cleanup);

describe("managed device global name", () => {
  it("reads the live owner name, sends that name and epoch as the precondition, and reports the committed name", async () => {
    const input = await open();

    expect(input).toHaveValue("NAS");
    expect(managedServerApi.getDeviceName).toHaveBeenCalledWith(
      "server-one",
      expect.any(AbortSignal),
    );
    fireEvent.change(input, { target: { value: "  Living room  " } });
    fireEvent.click(screen.getByRole("button", { name: "federation.save" }));
    await waitFor(() => expect(onSaved).toHaveBeenCalledWith("Living room"));
    expect(managedServerApi.setDeviceName).toHaveBeenCalledWith(
      "server-one",
      snapshot,
      "Living room",
    );
    expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
  });
  it("does not hide a rejected concurrent change or automatically retry it", async () => {
    const input = await open();

    vi.mocked(managedServerApi.setDeviceName).mockRejectedValueOnce(
      new FederationError("DeviceNameChanged", "Changed", 409),
    );
    fireEvent.change(input, { target: { value: "My edit" } });
    fireEvent.click(screen.getByRole("button", { name: "federation.save" }));
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
    fireEvent.click(screen.getByRole("button", { name: "federation.save" }));
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
    expect(screen.getByRole("button", { name: "federation.save" })).toBeDisabled();
    expect(screen.queryByRole("button", { name: "federation.name.reset" })).not.toBeInTheDocument();
    expect(managedServerApi.setDeviceName).not.toHaveBeenCalled();
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
    render(<ManagedDeviceNameEditor busy={false} serverId="server-one" onSaved={onSaved} />);
    fireEvent.click(screen.getByRole("button", { name: "federation.servers.name.edit" }));
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "federation.error.ManagedSourceUnavailable",
    );
    expect(screen.getByRole("button", { name: "federation.save" })).toBeDisabled();
    expect(managedServerApi.setDeviceName).not.toHaveBeenCalled();
  });
  it("aborts its read on cancel and ignores a late answer", async () => {
    let resolve!: (value: typeof snapshot) => void;

    vi.mocked(managedServerApi.getDeviceName).mockImplementation(
      () =>
        new Promise((done) => {
          resolve = done;
        }),
    );
    render(<ManagedDeviceNameEditor busy={false} serverId="server-one" onSaved={onSaved} />);
    fireEvent.click(screen.getByRole("button", { name: "federation.servers.name.edit" }));
    const signal = vi.mocked(managedServerApi.getDeviceName).mock.calls[0][1]!;

    fireEvent.click(screen.getByRole("button", { name: "federation.cancel" }));
    expect(signal.aborted).toBe(true);
    resolve(snapshot);
    await waitFor(() => expect(screen.queryByRole("textbox")).not.toBeInTheDocument());
    expect(onSaved).not.toHaveBeenCalled();
  });
});
