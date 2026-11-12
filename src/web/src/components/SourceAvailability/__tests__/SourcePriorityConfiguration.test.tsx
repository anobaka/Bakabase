import { HeroUIProvider } from "@heroui/react";
import { act, cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import SourcePriorityConfiguration from "../SourcePriorityConfiguration";
const api = vi.hoisted(() => ({ options: vi.fn(), providerIds: vi.fn(), saveOptions: vi.fn() }));
vi.mock("../api", () => ({ sourceAvailabilityApi: api }));
vi.mock("@/components/bakaui", async () => await import("@heroui/react"));
vi.mock("@/components/ThirdPartyIcon", () => ({ default: () => <svg aria-hidden /> }));
beforeEach(() => {
  vi.clearAllMocks();
  api.options.mockResolvedValue({ providerPriority: ["baidu", "http"] });
  api.providerIds.mockResolvedValue(["http", "baidu", "pixeldrain"]);
  api.saveOptions.mockImplementation(async (value) => value);
});
afterEach(cleanup);
const show = (onSavingChange?: (value: boolean) => void) => {
  render(
    <HeroUIProvider disableAnimation>
      <SourcePriorityConfiguration onSavingChange={onSavingChange} />
    </HeroUIProvider>,
  );
  return userEvent.setup();
};
const saveButton = () => screen.getByRole("button", { name: /sourceAvailability.priority.save/ });
describe("shared source priority", () => {
  it("shows an integration restriction without removing the provider or preventing reorder", async () => {
    api.options.mockResolvedValue({ providerPriority: ["terabox", "baidu"] });
    api.providerIds.mockResolvedValue(["baidu", "terabox"]);
    show();
    const rows = await screen.findAllByRole("listitem");
    expect(within(rows[0]).getByText("cloudDownload.integration.unavailable")).toBeInTheDocument();
    expect(
      within(rows[0]).getByRole("button", { name: "sourceAvailability.priority.moveDown" }),
    ).toBeEnabled();
    expect(api.saveOptions).not.toHaveBeenCalled();
  });

  it("uses registered providers and allows accessible reorder without saving implicitly", async () => {
    const user = show();
    await screen.findAllByRole("listitem");
    expect(saveButton()).toBeDisabled();
    await user.click(
      within(screen.getAllByRole("listitem")[0]).getByRole("button", {
        name: "sourceAvailability.priority.moveDown",
      }),
    );
    expect(saveButton()).not.toBeDisabled();
    expect(api.saveOptions).not.toHaveBeenCalled();
    await user.click(saveButton());
    await waitFor(() =>
      expect(api.saveOptions).toHaveBeenCalledExactlyOnceWith({
        providerPriority: ["http", "baidu", "pixeldrain"],
      }),
    );
    expect(await screen.findByText("sourceAvailability.priority.saved")).toBeInTheDocument();
  });
  it("offers Baidu first and never removes other providers", async () => {
    api.options.mockResolvedValue({ providerPriority: ["http", "pixeldrain", "baidu"] });
    const user = show();
    await screen.findAllByRole("listitem");
    await user.click(
      screen.getByRole("button", { name: "sourceAvailability.priority.baiduFirst" }),
    );
    await user.click(saveButton());
    expect(api.saveOptions).toHaveBeenCalledWith({
      providerPriority: ["baidu", "http", "pixeldrain"],
    });
  });
  it("retains edits and permits retry after save failure", async () => {
    api.saveOptions.mockRejectedValueOnce(new Error("save unavailable"));
    const user = show();
    await screen.findAllByRole("listitem");
    await user.click(
      within(screen.getAllByRole("listitem")[0]).getByRole("button", {
        name: "sourceAvailability.priority.moveDown",
      }),
    );
    await user.click(saveButton());
    expect(await screen.findByRole("alert")).toHaveTextContent("save unavailable");
    expect(saveButton()).not.toBeDisabled();
    await user.click(saveButton());
    expect(api.saveOptions).toHaveBeenCalledTimes(2);
    expect(api.saveOptions).toHaveBeenLastCalledWith({
      providerPriority: ["http", "baidu", "pixeldrain"],
    });
  });
  it("locks all reorder controls and repeated submits while saving", async () => {
    let complete!: (value: { providerPriority: string[] }) => void;
    api.saveOptions.mockReturnValueOnce(
      new Promise((resolve) => {
        complete = resolve;
      }),
    );
    const onSavingChange = vi.fn();
    const user = show(onSavingChange);
    await screen.findAllByRole("listitem");
    await user.click(
      within(screen.getAllByRole("listitem")[0]).getByRole("button", {
        name: "sourceAvailability.priority.moveDown",
      }),
    );
    await user.click(saveButton());
    expect(onSavingChange).toHaveBeenLastCalledWith(true);
    screen.getAllByRole("listitem").forEach((row) =>
      within(row)
        .getAllByRole("button")
        .forEach((button) => expect(button).toBeDisabled()),
    );
    await user.click(saveButton());
    expect(api.saveOptions).toHaveBeenCalledTimes(1);
    await act(async () => complete({ providerPriority: ["http", "baidu", "pixeldrain"] }));
    expect(onSavingChange.mock.calls).toEqual([[true], [false]]);
    expect(saveButton()).toBeDisabled();
  });
  it("does not enable saving on a failed configuration read", async () => {
    api.options.mockRejectedValue(new Error("read failed"));
    show();
    expect(await screen.findByRole("alert")).toHaveTextContent("read failed");
    expect(saveButton()).toBeDisabled();
    expect(api.saveOptions).not.toHaveBeenCalled();
  });
});
