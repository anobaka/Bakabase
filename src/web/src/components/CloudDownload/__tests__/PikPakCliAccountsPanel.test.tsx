import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import PikPakCliAccountsPanel from "../PikPakCliAccountsPanel";

const api = vi.hoisted(() => ({
  getPikPakCliAccount: vi.fn(),
  getActivePikPakCliAuthorization: vi.fn(),
  startPikPakCliAuthorization: vi.fn(),
  startPikPakCliManageFilesAuthorization: vi.fn(),
  startPikPakCliPurgeFilesAuthorization: vi.fn(),
  setPikPakCliCleanupSettings: vi.fn(),
  pollPikPakCliAuthorization: vi.fn(),
  cancelPikPakCliAuthorization: vi.fn(),
  logoutPikPakCliAccount: vi.fn(),
}));
const openExternalUrl = vi.hoisted(() => vi.fn());
vi.mock("@/sdk/BApi", () => ({ default: { pikpakCli: api } }));
vi.mock("@/utils/openExternalUrl", () => ({ openExternalUrl }));
vi.mock("@/components/ExternalLink", () => ({
  default: ({ children, href }: any) => <a href={href}>{children}</a>,
}));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/components/bakaui", async () => {
  const { Radio, RadioGroup } = await import("@/components/bakaui/components/Radio");
  return {
    Radio, RadioGroup,
    Button: ({ children, onPress, isDisabled, isLoading }: any) => (
      <button disabled={isDisabled || isLoading} onClick={onPress}>
        {children}
      </button>
    ),
    Chip: ({ children }: any) => <span>{children}</span>,
    Spinner: () => <span>loading</span>,
    Modal: ({ title, children, footer }: any) => (
      <div role="dialog" aria-label={title}>
        {children}
        {footer}
      </div>
    ),
  };
});

const pending = {
  id: "auth-1",
  status: "pending",
  verificationUri: "https://mypikpak.com/device",
  userCode: "ABCD-EFGH",
  expiresAt: "2999-10-27T20:00:00Z",
  intervalSeconds: 5,
};
const quiet = { showErrorToast: false, sensitiveResponse: true };
beforeEach(() => {
  vi.resetAllMocks();
  api.getPikPakCliAccount.mockResolvedValue({ code: 0, data: { isConfigured: false } });
  api.getActivePikPakCliAuthorization.mockResolvedValue({ code: 0, data: null });
  api.startPikPakCliAuthorization.mockResolvedValue({ code: 0, data: pending });
  api.pollPikPakCliAuthorization.mockResolvedValue({ code: 0, data: pending });
  api.cancelPikPakCliAuthorization.mockResolvedValue({ code: 0 });
  api.logoutPikPakCliAccount.mockResolvedValue({ code: 0 });
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

it("requires a user click to open the official page and does not ask for passwords or device tokens", async () => {
  render(<PikPakCliAccountsPanel />);
  await screen.findByText("pikpakCli.account.notConnected");
  fireEvent.click(screen.getByText("pikpakCli.account.authorize"));
  await screen.findByText("ABCD-EFGH");
  expect(api.startPikPakCliAuthorization).toHaveBeenCalledWith(quiet);
  expect(openExternalUrl).not.toHaveBeenCalled();
  expect(screen.queryByRole("textbox")).toBeNull();
  fireEvent.click(screen.getByText("pikpakCli.authorization.open"));
  expect(openExternalUrl).toHaveBeenCalledWith(pending.verificationUri);
});

it("refreshes account state and notifies callers only after confirmed authorization", async () => {
  const onChanged = vi.fn();
  render(<PikPakCliAccountsPanel onChanged={onChanged} />);
  await screen.findByText("pikpakCli.account.notConnected");
  fireEvent.click(screen.getByText("pikpakCli.account.authorize"));
  await screen.findByText("ABCD-EFGH");
  expect(onChanged).not.toHaveBeenCalled();
  api.getPikPakCliAccount.mockResolvedValue({
    code: 0,
    data: { isConfigured: true, displayName: "My PikPak" },
  });
  api.pollPikPakCliAuthorization.mockResolvedValue({
    code: 0,
    data: { ...pending, status: "authorized" },
  });
  fireEvent.click(screen.getByText("pikpakCli.authorization.refresh"));
  await screen.findByText("My PikPak");
  expect(api.pollPikPakCliAuthorization).toHaveBeenCalledWith("auth-1", quiet);
  expect(onChanged).toHaveBeenCalledTimes(1);
  expect(screen.queryByText("ABCD-EFGH")).toBeNull();
});

it("does not resurrect an authorization session when an earlier poll returns after cancellation", async () => {
  let resolvePoll!: (value: unknown) => void;
  api.pollPikPakCliAuthorization.mockReturnValue(
    new Promise((resolve) => {
      resolvePoll = resolve;
    }),
  );
  const onChanged = vi.fn();
  render(<PikPakCliAccountsPanel onChanged={onChanged} />);
  await screen.findByText("pikpakCli.account.notConnected");
  vi.useFakeTimers();
  await act(async () => {
    fireEvent.click(screen.getByText("pikpakCli.account.authorize"));
  });
  expect(screen.getByText("ABCD-EFGH")).toBeTruthy();
  await act(async () => {
    await vi.advanceTimersByTimeAsync(5100);
  });
  expect(api.pollPikPakCliAuthorization).toHaveBeenCalledOnce();
  fireEvent.click(screen.getByText("pikpakCli.authorization.cancel"));
  await act(async () => {
    await Promise.resolve();
  });
  await act(async () => {
    resolvePoll({ code: 0, data: { ...pending, status: "authorized" } });
    await Promise.resolve();
  });
  expect(screen.queryByText("ABCD-EFGH")).toBeNull();
  expect(screen.queryByText("pikpakCli.authorization.status.authorized")).toBeNull();
  expect(onChanged).not.toHaveBeenCalled();
});

it("keeps a failed cancellation visible for retry and never logs out implicitly", async () => {
  render(<PikPakCliAccountsPanel />);
  await screen.findByText("pikpakCli.account.notConnected");
  fireEvent.click(screen.getByText("pikpakCli.account.authorize"));
  await screen.findByText("ABCD-EFGH");
  api.cancelPikPakCliAuthorization.mockResolvedValue({ code: 1, message: "Connection failed" });
  fireEvent.click(screen.getByText("pikpakCli.authorization.cancel"));
  await screen.findByRole("alert");
  expect(screen.getByText("ABCD-EFGH")).toBeTruthy();
  expect(api.logoutPikPakCliAccount).not.toHaveBeenCalled();
});

it("shows structured HTTP problems and resumes the same session after a polling failure", async () => {
  render(<PikPakCliAccountsPanel />);
  await screen.findByText("pikpakCli.account.notConnected");
  vi.useFakeTimers();
  await act(async () => {
    fireEvent.click(screen.getByText("pikpakCli.account.authorize"));
  });
  api.pollPikPakCliAuthorization.mockRejectedValueOnce({
    status: 503,
    error: { message: "PIKPAK_NETWORK_UNAVAILABLE" },
  });
  // The SDK can reject the automatic check with a structured Response object.
  await act(async () => {
    await vi.advanceTimersByTimeAsync(5000);
  });
  expect(screen.getByRole("alert").textContent).toBe("PIKPAK_NETWORK_UNAVAILABLE");
  expect(screen.getByText("pikpakCli.authorization.pollingPaused")).toBeTruthy();
  await act(async () => {
    await vi.advanceTimersByTimeAsync(20000);
  });
  expect(api.pollPikPakCliAuthorization).toHaveBeenCalledOnce();
  api.getPikPakCliAccount.mockResolvedValue({ code: 0, data: { isConfigured: true } });
  api.pollPikPakCliAuthorization.mockResolvedValue({
    code: 0,
    data: { ...pending, status: "authorized" },
  });
  await act(async () => {
    fireEvent.click(screen.getByText("pikpakCli.authorization.refresh"));
  });
  expect(screen.getByText("pikpakCli.account.connected")).toBeTruthy();
  expect(screen.queryByRole("alert")).toBeNull();
  expect(api.startPikPakCliAuthorization).toHaveBeenCalledOnce();
  expect(api.pollPikPakCliAuthorization).toHaveBeenLastCalledWith(pending.id, quiet);
  expect(api.cancelPikPakCliAuthorization).not.toHaveBeenCalled();
});

it("automatically completes a pending authorization without a manual refresh", async () => {
  const onChanged = vi.fn();
  render(<PikPakCliAccountsPanel onChanged={onChanged} />);
  await screen.findByText("pikpakCli.account.notConnected");
  vi.useFakeTimers();
  await act(async () => {
    fireEvent.click(screen.getByText("pikpakCli.account.authorize"));
  });
  api.getPikPakCliAccount.mockResolvedValue({ code: 0, data: { isConfigured: true } });
  api.pollPikPakCliAuthorization.mockResolvedValue({
    code: 0,
    data: { ...pending, status: "authorized" },
  });
  await act(async () => {
    await vi.advanceTimersByTimeAsync(5000);
  });
  expect(screen.getByText("pikpakCli.account.connected")).toBeTruthy();
  expect(onChanged).toHaveBeenCalledOnce();
  await act(async () => {
    await vi.advanceTimersByTimeAsync(20000);
  });
  expect(api.pollPikPakCliAuthorization).toHaveBeenCalledOnce();
});

it("restores a pending server session after navigation and completes it without a new authorization", async () => {
  api.getPikPakCliAccount.mockResolvedValue({
    code: 0,
    data: { isConfigured: false, problem: "PIKPAK_ACCOUNT_REQUIRED" },
  });
  api.getActivePikPakCliAuthorization.mockResolvedValue({ code: 0, data: pending });
  const onChanged = vi.fn();
  vi.useFakeTimers();
  await act(async () => {
    render(<PikPakCliAccountsPanel onChanged={onChanged} />);
  });
  expect(screen.getByText("ABCD-EFGH")).toBeTruthy();
  expect(screen.queryByRole("alert")).toBeNull();
  expect(api.getActivePikPakCliAuthorization).toHaveBeenCalledWith(quiet);
  expect(openExternalUrl).not.toHaveBeenCalled();
  api.getPikPakCliAccount.mockResolvedValue({ code: 0, data: { isConfigured: true } });
  api.pollPikPakCliAuthorization.mockResolvedValue({
    code: 0,
    data: { ...pending, status: "authorized" },
  });
  await act(async () => {
    await vi.advanceTimersByTimeAsync(5000);
  });
  expect(screen.getByText("pikpakCli.account.connected")).toBeTruthy();
  expect(api.pollPikPakCliAuthorization).toHaveBeenCalledWith(pending.id, quiet);
  expect(onChanged).toHaveBeenCalledOnce();
  expect(api.startPikPakCliAuthorization).not.toHaveBeenCalled();
  expect(api.cancelPikPakCliAuthorization).not.toHaveBeenCalled();
});

it("does not let an unread active session be replaced and can retry its initial read", async () => {
  let rejectRead!: (value: unknown) => void;
  api.getActivePikPakCliAuthorization.mockImplementationOnce(
    () =>
      new Promise((_resolve, reject) => {
        rejectRead = reject;
      }),
  );
  render(<PikPakCliAccountsPanel />);
  const authorize = screen.getByRole("button", { name: "pikpakCli.account.authorize" });
  expect(authorize).toBeDisabled();
  fireEvent.click(authorize);
  expect(api.startPikPakCliAuthorization).not.toHaveBeenCalled();
  await act(async () => {
    rejectRead({ error: { message: "PIKPAK_NETWORK_UNAVAILABLE" } });
  });
  expect(screen.getByRole("alert").textContent).toBe("PIKPAK_NETWORK_UNAVAILABLE");
  expect(authorize).toBeDisabled();
  api.getActivePikPakCliAuthorization.mockResolvedValue({ code: 0, data: pending });
  fireEvent.click(screen.getByText("cloudDownload.account.refresh"));
  await screen.findByText("ABCD-EFGH");
  expect(screen.queryByRole("alert")).toBeNull();
  expect(api.startPikPakCliAuthorization).not.toHaveBeenCalled();
  expect(api.cancelPikPakCliAuthorization).not.toHaveBeenCalled();
});

it("restores a failed session with its problem instead of silently replacing it", async () => {
  api.getActivePikPakCliAuthorization.mockResolvedValue({
    code: 0,
    data: {
      ...pending,
      status: "failed",
      userCode: null,
      verificationUri: null,
      problem: "PIKPAK_PERMISSION_REQUIRED",
    },
  });
  render(<PikPakCliAccountsPanel />);
  await screen.findByText("pikpakCli.authorization.status.failed");
  expect(screen.getByRole("alert").textContent).toBe("PIKPAK_PERMISSION_REQUIRED");
  expect(api.startPikPakCliAuthorization).not.toHaveBeenCalled();
  expect(api.pollPikPakCliAuthorization).not.toHaveBeenCalled();
  expect(api.cancelPikPakCliAuthorization).not.toHaveBeenCalled();
});

it("does not report completion until the account is configured", async () => {
  const onChanged = vi.fn();
  render(<PikPakCliAccountsPanel onChanged={onChanged} />);
  await screen.findByText("pikpakCli.account.notConnected");
  fireEvent.click(screen.getByText("pikpakCli.account.authorize"));
  await screen.findByText("ABCD-EFGH");
  api.pollPikPakCliAuthorization.mockResolvedValue({
    code: 0,
    data: { ...pending, status: "authorized" },
  });
  fireEvent.click(screen.getByText("pikpakCli.authorization.refresh"));
  await screen.findByRole("alert");
  expect(screen.getByRole("alert").textContent).toBe("PIKPAK_ACCOUNT_REQUIRED");
  expect(screen.queryByText("pikpakCli.authorization.status.authorized")).toBeNull();
  expect(onChanged).not.toHaveBeenCalled();
});

it("confirms disconnect and preserves the connected state if it fails", async () => {
  api.getPikPakCliAccount.mockResolvedValue({ code: 0, data: { isConfigured: true } });
  api.logoutPikPakCliAccount.mockResolvedValue({ code: 1, message: "Account in use" });
  render(<PikPakCliAccountsPanel />);
  await screen.findByText("pikpakCli.account.connected");
  fireEvent.click(screen.getByText("pikpakCli.account.disconnect"));
  const dialog = await screen.findByRole("dialog");
  expect(api.logoutPikPakCliAccount).not.toHaveBeenCalled();
  fireEvent.click(within(dialog).getByRole("button", { name: "pikpakCli.account.disconnect" }));
  await waitFor(() => expect(api.logoutPikPakCliAccount).toHaveBeenCalledWith(quiet));
  expect(screen.getByText("pikpakCli.account.connected")).toBeTruthy();
  expect(screen.getByRole("dialog")).toBeTruthy();
});

const bound = {
  isConfigured: true,
  accountId: "example-account",
  accountVersion: "generation-1",
  trashTemporaryCopiesAfterDownload: false,
};

it("keeps the account policy off until granted and explicitly enabled", async () => {
  api.getPikPakCliAccount.mockResolvedValue({ code: 0, data: { ...bound, canManageFiles: false } });
  api.startPikPakCliManageFilesAuthorization.mockResolvedValue({ code: 0, data: pending });
  render(<PikPakCliAccountsPanel />);
  const trash = await screen.findByRole("radio", { name: "pikpakCli.cleanup.trash" });
  expect(trash).toBeDisabled();
  expect(screen.getByRole("radio", { name: "pikpakCli.cleanup.keep" })).toBeChecked();
  fireEvent.click(screen.getByText("pikpakCli.cleanup.authorize"));
  await screen.findByText(pending.userCode);
  expect(api.startPikPakCliManageFilesAuthorization).toHaveBeenCalledWith(quiet);
  expect(api.startPikPakCliAuthorization).not.toHaveBeenCalled();
  api.getPikPakCliAccount.mockResolvedValue({ code: 0, data: { ...bound, canManageFiles: true } });
  api.pollPikPakCliAuthorization.mockResolvedValue({
    code: 0,
    data: { ...pending, status: "authorized" },
  });
  fireEvent.click(screen.getByText("pikpakCli.authorization.refresh"));
  expect(await screen.findByRole("radio", { name: "pikpakCli.cleanup.trash" })).toBeEnabled();
  expect(screen.getByRole("radio", { name: "pikpakCli.cleanup.keep" })).toBeChecked();
  expect(api.setPikPakCliCleanupSettings).not.toHaveBeenCalled();
  expect(screen.getByRole("radio", { name: "pikpakCli.cleanup.delete" })).toBeDisabled();
});

it("writes the setting for the displayed account generation and keeps the previous value on error", async () => {
  const account = { ...bound, canManageFiles: true };
  api.getPikPakCliAccount.mockResolvedValue({ code: 0, data: account });
  api.setPikPakCliCleanupSettings
    .mockResolvedValueOnce({ code: 400, message: "PIKPAK_ACCOUNT_IDENTITY_UNVERIFIED" })
    .mockResolvedValueOnce({
      code: 0,
      data: { ...account, trashTemporaryCopiesAfterDownload: true },
    })
    .mockResolvedValueOnce({ code: 0, data: account });
  render(<PikPakCliAccountsPanel />);
  const trash = await screen.findByRole("radio", { name: "pikpakCli.cleanup.trash" });
  fireEvent.click(trash);
  await screen.findByRole("alert");
  expect(trash).not.toBeChecked();
  fireEvent.click(trash);
  await waitFor(() => expect(trash).toBeChecked());
  expect(api.setPikPakCliCleanupSettings).toHaveBeenLastCalledWith(
    {
      expectedAccountId: bound.accountId,
      expectedAccountVersion: bound.accountVersion,
      trashTemporaryCopiesAfterDownload: true,
      permanentlyDeleteTemporaryCopiesAfterDownload: false,
    },
    quiet,
  );
  fireEvent.click(screen.getByRole("radio", { name: "pikpakCli.cleanup.keep" }));
  await waitFor(() => expect(trash).not.toBeChecked());
  expect(api.setPikPakCliCleanupSettings).toHaveBeenLastCalledWith(
    {
      expectedAccountId: bound.accountId,
      expectedAccountVersion: bound.accountVersion,
      trashTemporaryCopiesAfterDownload: false,
      permanentlyDeleteTemporaryCopiesAfterDownload: false,
    },
    quiet,
  );
});

it("requires a separate grant and explicit selection before deleting future transfers permanently", async () => {
  const account = { ...bound, canManageFiles: true, trashTemporaryCopiesAfterDownload: true };
  const authorized = { ...account, canPermanentlyDeleteFiles: true };
  api.getPikPakCliAccount.mockResolvedValue({ code: 0, data: account });
  api.startPikPakCliPurgeFilesAuthorization.mockResolvedValue({ code: 0, data: pending });
  render(<PikPakCliAccountsPanel />);
  expect(await screen.findByRole("radio", { name: "pikpakCli.cleanup.trash" })).toBeChecked();
  expect(screen.getByRole("radio", { name: "pikpakCli.cleanup.delete" })).toBeDisabled();

  fireEvent.click(screen.getByRole("button", { name: "pikpakCli.cleanup.authorizeDeletion" }));
  await screen.findByText(pending.userCode);
  expect(api.startPikPakCliPurgeFilesAuthorization).toHaveBeenCalledWith(quiet);
  expect(api.startPikPakCliManageFilesAuthorization).not.toHaveBeenCalled();
  api.getPikPakCliAccount.mockResolvedValue({ code: 0, data: authorized });
  api.pollPikPakCliAuthorization.mockResolvedValue({ code: 0, data: { ...pending, status: "authorized" } });
  fireEvent.click(screen.getByText("pikpakCli.authorization.refresh"));
  const remove = await screen.findByRole("radio", { name: "pikpakCli.cleanup.delete" });
  expect(remove).not.toBeChecked();
  expect(api.setPikPakCliCleanupSettings).not.toHaveBeenCalled();

  api.setPikPakCliCleanupSettings.mockResolvedValueOnce({
    code: 0, data: { ...authorized, permanentlyDeleteTemporaryCopiesAfterDownload: true },
  });
  fireEvent.click(remove);
  await waitFor(() => expect(remove).toBeChecked());
  expect(api.setPikPakCliCleanupSettings).toHaveBeenCalledWith({
    expectedAccountId: account.accountId,
    expectedAccountVersion: account.accountVersion,
    trashTemporaryCopiesAfterDownload: true,
    permanentlyDeleteTemporaryCopiesAfterDownload: true,
  }, quiet);
});
