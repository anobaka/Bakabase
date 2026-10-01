import { HeroUIProvider } from "@heroui/react";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import Backup from "..";

const { state, patchAppOptions, updateAppOptions, successToast, errorToast } = vi.hoisted(() => ({
  state: {
    options: {} as { enableAutomaticBackup?: boolean; maxBackupVersions?: number },
  },
  patchAppOptions: vi.fn(),
  updateAppOptions: vi.fn(),
  successToast: vi.fn(),
  errorToast: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: { options: { patchAppOptions } },
}));
vi.mock("@/stores/options", () => ({
  useAppOptionsStore: (selector: (store: unknown) => unknown) =>
    selector({ data: state.options, update: updateAppOptions }),
}));
vi.mock("react-hot-toast", () => ({
  default: { success: successToast, error: errorToast },
}));

const keys = {
  enabled: "configuration.backup.enabled",
  versions: "configuration.backup.maxVersions",
  cancel: "configuration.backup.disable.cancel",
  confirm: "configuration.backup.disable.confirm",
};
const panel = () => (
  <HeroUIProvider disableAnimation>
    <Backup />
  </HeroUIProvider>
);
const enabledSwitch = () => screen.getByRole("switch", { name: keys.enabled });
const versionsInput = () =>
  screen.getByRole<HTMLInputElement>("spinbutton", { name: keys.versions });

beforeEach(() => {
  vi.clearAllMocks();
  state.options = {};
  patchAppOptions.mockResolvedValue({ code: 0 });
  updateAppOptions.mockImplementation((patch) => {
    state.options = { ...state.options, ...patch };
  });
});

describe("automatic version backup settings", () => {
  it("defaults to enabled with 7 retained versions without writing options", () => {
    render(panel());
    expect(enabledSwitch()).toBeChecked();
    expect(versionsInput()).toHaveValue(7);
    expect(screen.getByText("configuration.backup.description")).toBeVisible();
    expect(screen.getByRole("button", { name: "common.action.save" })).toBeDisabled();
    expect(patchAppOptions).not.toHaveBeenCalled();
  });

  it("requires a second risk confirmation and preserves backups when cancelled", async () => {
    const user = userEvent.setup();

    render(panel());
    const toggle = enabledSwitch();

    await user.click(toggle);
    expect(screen.getByRole("dialog")).toBeVisible();
    expect(screen.getByText("configuration.backup.disable.warning")).toBeVisible();
    expect(screen.getByText("configuration.backup.disable.preserved")).toBeVisible();
    expect(toggle).toBeChecked();
    expect(patchAppOptions).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: keys.cancel }));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(enabledSwitch()).toBeChecked();
    expect(patchAppOptions).not.toHaveBeenCalled();
  });

  it("preserves the enabled setting when the risk dialog is dismissed with Escape", async () => {
    const user = userEvent.setup();

    render(panel());
    await user.click(enabledSwitch());
    await user.keyboard("{Escape}");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(enabledSwitch()).toBeChecked();
    expect(patchAppOptions).not.toHaveBeenCalled();
  });

  it("saves disabling only after the explicit confirmation", async () => {
    const user = userEvent.setup();

    render(panel());
    await user.click(enabledSwitch());
    await user.click(screen.getByRole("button", { name: keys.confirm }));
    expect(patchAppOptions).toHaveBeenCalledExactlyOnceWith({ enableAutomaticBackup: false });
    expect(updateAppOptions).toHaveBeenCalledWith({ enableAutomaticBackup: false });
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(enabledSwitch()).not.toBeChecked();
    expect(versionsInput()).toBeDisabled();
  });

  it("keeps backups enabled and the confirmation open if saving fails", async () => {
    const user = userEvent.setup();

    patchAppOptions.mockResolvedValue({ code: 1 });
    render(panel());
    const toggle = enabledSwitch();

    await user.click(toggle);
    await user.click(screen.getByRole("button", { name: keys.confirm }));
    expect(screen.getByRole("dialog")).toBeVisible();
    expect(toggle).toBeChecked();
    expect(updateAppOptions).not.toHaveBeenCalled();
    expect(successToast).not.toHaveBeenCalled();
  });

  it("can re-enable backups directly while preserving the saved retention limit", async () => {
    const user = userEvent.setup();

    state.options = { enableAutomaticBackup: false, maxBackupVersions: 3 };
    render(panel());
    expect(versionsInput()).toHaveValue(3);
    expect(versionsInput()).toBeDisabled();

    await user.click(enabledSwitch());
    expect(patchAppOptions).toHaveBeenCalledExactlyOnceWith({ enableAutomaticBackup: true });
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(enabledSwitch()).toBeChecked();
    expect(versionsInput()).toHaveValue(3);
    expect(versionsInput()).not.toBeDisabled();
  });

  it("saves the retention limit once after editing and pressing Save", async () => {
    const user = userEvent.setup();

    render(panel());
    await user.clear(versionsInput());
    await user.type(versionsInput(), "12");
    expect(patchAppOptions).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: "common.action.save" }));
    expect(patchAppOptions).toHaveBeenCalledExactlyOnceWith({ maxBackupVersions: 12 });
    expect(updateAppOptions).toHaveBeenCalledWith({ maxBackupVersions: 12 });
    expect(versionsInput()).toHaveValue(12);
    expect(screen.getByRole("button", { name: "common.action.save" })).toBeDisabled();
  });

  it.each(["", "0", "-1", "1.5", "1e2", "2147483648"])(
    "rejects the invalid retention value %s without saving",
    (value) => {
      render(panel());
      fireEvent.change(versionsInput(), { target: { value } });
      expect(versionsInput()).toHaveAttribute("aria-invalid", "true");
      expect(screen.getByRole("button", { name: "common.action.save" })).toBeDisabled();
      expect(patchAppOptions).not.toHaveBeenCalled();
    },
  );

  it("keeps the edited retention value available for retry after a network error", async () => {
    const user = userEvent.setup();

    patchAppOptions.mockRejectedValue(new Error("network failure"));
    render(panel());
    fireEvent.change(versionsInput(), { target: { value: "4" } });
    await user.click(screen.getByRole("button", { name: "common.action.save" }));
    expect(versionsInput()).toHaveValue(4);
    expect(screen.getByRole("button", { name: "common.action.save" })).not.toBeDisabled();
    expect(updateAppOptions).not.toHaveBeenCalled();
    expect(errorToast).toHaveBeenCalledWith("common.error.failedToProcessData");
  });
});
