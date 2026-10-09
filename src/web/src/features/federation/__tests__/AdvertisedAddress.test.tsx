import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import AdvertisedAddress from "../components/AdvertisedAddress";

const { save, access } = vi.hoisted(() => ({
  save: vi.fn(),
  access: { context: "known", isLocal: true, mode: 0, paired: false },
}));

vi.mock("@/sdk/BApi", () => ({
  default: { remoteAccess: { setRemoteAccessAdvertisedAddress: save } },
}));
vi.mock("@/stores/remoteAccess", () => ({
  useRemoteAccessStore: (selector: (state: typeof access) => unknown) => selector(access),
}));
vi.mock("../components/common", () => ({ buttonClass: "", fieldClass: "", primaryClass: "" }));

beforeEach(() => {
  save.mockReset().mockResolvedValue({ code: 0 });
  Object.assign(access, { context: "known", isLocal: true, mode: 0, paired: false });
});
afterEach(cleanup);

describe("optional external address", () => {
  it("defaults to automatic, validates before saving, and refreshes only after a successful normalized save", async () => {
    const refresh = vi.fn().mockResolvedValue(undefined);

    render(<AdvertisedAddress onSaved={refresh} />);
    const input = screen.getByRole("textbox", {
      name: "federation.devices.advertisedAddress.label",
    });

    expect(input).toHaveValue("");
    expect(input).toHaveAttribute("placeholder", "federation.devices.advertisedAddress.automatic");
    fireEvent.change(input, { target: { value: "https://user:secret@nas.example/path" } });
    fireEvent.click(screen.getByRole("button", { name: "federation.save" }));
    expect(save).not.toHaveBeenCalled();
    expect(screen.getByRole("alert")).toHaveTextContent(
      "federation.devices.advertisedAddress.invalid",
    );
    fireEvent.change(input, { target: { value: "HTTPS://NAS.EXAMPLE:443/" } });
    fireEvent.click(screen.getByRole("button", { name: "federation.save" }));
    await screen.findByRole("status");
    expect(save).toHaveBeenCalledWith(
      { address: "https://nas.example" },
      { showErrorToast: false },
    );
    expect(refresh).toHaveBeenCalledTimes(1);
  });

  it("clears the optional preference and preserves the value after a refused save for retry", async () => {
    const refresh = vi.fn().mockResolvedValue(undefined);

    save.mockResolvedValueOnce({ code: 400, message: "Address refused" });
    render(<AdvertisedAddress value="https://nas.example:8443" onSaved={refresh} />);
    const reset = screen.getByRole("button", {
      name: "federation.devices.advertisedAddress.reset",
    });

    fireEvent.click(reset);
    expect(await screen.findByRole("alert")).toHaveTextContent("Address refused");
    expect(refresh).not.toHaveBeenCalled();
    expect(screen.getByRole("textbox")).toHaveValue("https://nas.example:8443");
    fireEvent.click(reset);
    await screen.findByRole("status");
    expect(save).toHaveBeenLastCalledWith({ address: undefined }, { showErrorToast: false });
    expect(screen.getByRole("textbox")).toHaveValue("");
  });

  it("prevents duplicate requests and keeps a network failure inline without losing the edit", async () => {
    let reject!: (cause: Error) => void;

    save.mockImplementationOnce(
      () =>
        new Promise((_, fail) => {
          reject = fail;
        }),
    );
    render(<AdvertisedAddress onSaved={vi.fn()} />);
    fireEvent.change(screen.getByRole("textbox"), { target: { value: "https://nas.example" } });
    const button = screen.getByRole("button", { name: "federation.save" });

    fireEvent.click(button);
    fireEvent.click(button);
    expect(save).toHaveBeenCalledTimes(1);
    expect(button).toBeDisabled();
    reject(new Error("Offline"));
    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("Offline"));
    expect(screen.getByRole("textbox")).toHaveValue("https://nas.example");
    expect(screen.getByRole("button", { name: "federation.save" })).toBeEnabled();
    expect(screen.queryByRole("status")).toBeNull();
  });

  it("does not offer changes without known management access", () => {
    access.context = "unknown";
    const view = render(<AdvertisedAddress onSaved={vi.fn()} />);

    expect(screen.queryByRole("textbox")).toBeNull();
    Object.assign(access, { context: "known", isLocal: false, mode: 1 });
    view.rerender(<AdvertisedAddress onSaved={vi.fn()} />);
    expect(screen.queryByRole("textbox")).toBeNull();
  });
});
