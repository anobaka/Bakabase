import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import TeraBoxCliAccountsPanel from "../TeraBoxCliAccountsPanel";

const api = vi.hoisted(() => ({
  getTeraBoxAccount: vi.fn(),
  getActiveTeraBoxAuthorization: vi.fn(),
  startTeraBoxAuthorization: vi.fn(),
  completeTeraBoxAuthorization: vi.fn(),
  cancelTeraBoxAuthorization: vi.fn(),
  logoutTeraBoxAccount: vi.fn(),
}));
const openExternalUrl = vi.hoisted(() => vi.fn());
vi.mock("@/sdk/BApi", () => ({ default: { terabox: api } }));
vi.mock("@/utils/openExternalUrl", () => ({ openExternalUrl }));
vi.mock("@/components/ExternalLink", () => ({
  default: ({ children, href }: any) => <a href={href}>{children}</a>,
}));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, isDisabled, isLoading, color }: any) => (
    <button data-color={color} disabled={isDisabled || isLoading} onClick={onPress}>
      {children}
    </button>
  ),
  Input: ({ label, value, onValueChange, type, autoComplete, isDisabled }: any) => (
    <label>
      {label}
      <input
        value={value}
        onChange={(e) => onValueChange(e.target.value)}
        type={type}
        autoComplete={autoComplete}
        disabled={isDisabled}
      />
    </label>
  ),
  Chip: ({ children }: any) => <span>{children}</span>,
  Spinner: () => <span>loading</span>,
  Modal: ({ title, children, footer }: any) => (
    <div role="dialog" aria-label={title}>
      {children}
      {footer}
    </div>
  ),
}));
const pending = {
  id: "auth-1",
  status: "pending",
  authorizationUri: "https://www.terabox.com/oauth/authorize?state=opaque",
  expiresAt: "2999-10-27T20:00:00Z",
};
const quiet = { showErrorToast: false, sensitiveResponse: true };
beforeEach(() => {
  vi.resetAllMocks();
  api.getTeraBoxAccount.mockResolvedValue({ code: 0, data: { isConfigured: false } });
  api.getActiveTeraBoxAuthorization.mockResolvedValue({ code: 0, data: null });
  api.startTeraBoxAuthorization.mockResolvedValue({ code: 0, data: pending });
  api.completeTeraBoxAuthorization.mockResolvedValue({
    code: 0,
    data: { ...pending, status: "authorized" },
  });
  api.cancelTeraBoxAuthorization.mockResolvedValue({ code: 0 });
  api.logoutTeraBoxAccount.mockResolvedValue({ code: 0 });
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
});
const start = async () => {
  await screen.findByText("terabox.account.notConnected");
  fireEvent.click(screen.getByText("terabox.account.authorize"));
  return screen.findByLabelText("terabox.authorization.code");
};

it("loads account/session only, opens the official page on click, and never polls", async () => {
  render(<TeraBoxCliAccountsPanel />);
  const input = await start();
  expect(api.startTeraBoxAuthorization).toHaveBeenCalledWith(quiet);
  expect(openExternalUrl).not.toHaveBeenCalled();
  expect(input).toHaveAttribute("type", "password");
  expect(input).toHaveAttribute("autoComplete", "off");
  fireEvent.click(screen.getByText("terabox.authorization.open"));
  expect(openExternalUrl).toHaveBeenCalledWith(pending.authorizationUri);
  vi.useFakeTimers();
  await act(async () => {
    await vi.advanceTimersByTimeAsync(120000);
  });
  expect(api.getTeraBoxAccount).toHaveBeenCalledTimes(1);
  expect(api.getActiveTeraBoxAuthorization).toHaveBeenCalledTimes(1);
  expect(api.completeTeraBoxAuthorization).not.toHaveBeenCalled();
});

it("keeps account management available while the download integration is unavailable", async () => {
  api.getTeraBoxAccount.mockResolvedValue({
    code: 0,
    data: { isConfigured: true, displayName: "Connected account" },
  });
  render(<TeraBoxCliAccountsPanel />);
  expect(await screen.findByText("terabox.account.connected")).toBeInTheDocument();
  expect(screen.getByRole("note")).toHaveTextContent("cloudDownload.teraboxIntegrationUnavailable");
  expect(screen.getByRole("button", { name: "terabox.account.reauthorize" })).toBeEnabled();
  expect(screen.getByRole("button", { name: "terabox.account.disconnect" })).toBeEnabled();
  expect(api.startTeraBoxAuthorization).not.toHaveBeenCalled();
});

it("submits the one-time code privately once, clears it, and confirms the account before notifying", async () => {
  const onChanged = vi.fn();
  render(<TeraBoxCliAccountsPanel onChanged={onChanged} />);
  const input = await start();
  expect(screen.getByText("terabox.authorization.complete")).toBeDisabled();
  fireEvent.change(input, { target: { value: "  one-time-code  " } });
  api.getTeraBoxAccount.mockResolvedValue({
    code: 0,
    data: { isConfigured: true, displayName: "My account" },
  });
  fireEvent.click(screen.getByText("terabox.authorization.complete"));
  await screen.findByText("My account");
  expect(api.completeTeraBoxAuthorization).toHaveBeenCalledWith(
    pending.id,
    { code: "one-time-code" },
    quiet,
  );
  expect(screen.queryByLabelText("terabox.authorization.code")).toBeNull();
  expect(onChanged).toHaveBeenCalledTimes(1);
});

it("does not accept whitespace-only codes", async () => {
  render(<TeraBoxCliAccountsPanel />);
  fireEvent.change(await start(), { target: { value: "  " } });
  fireEvent.click(screen.getByText("terabox.authorization.complete"));
  expect(api.completeTeraBoxAuthorization).not.toHaveBeenCalled();
});

it("does not replay an uncertain completion and recovers via GET only", async () => {
  render(<TeraBoxCliAccountsPanel />);
  const input = await start();
  fireEvent.change(input, { target: { value: "used-code" } });
  api.completeTeraBoxAuthorization.mockRejectedValueOnce({
    error: { message: "TERABOX_NETWORK_UNAVAILABLE" },
  });
  fireEvent.click(screen.getByText("terabox.authorization.complete"));
  await screen.findByText("terabox.authorization.refreshRequired");
  expect(input).toHaveValue("");
  expect(input).toBeDisabled();
  expect(screen.getByText("terabox.authorization.cancel")).toBeDisabled();
  api.getTeraBoxAccount.mockResolvedValue({ code: 0, data: { isConfigured: true } });
  api.getActiveTeraBoxAuthorization.mockResolvedValue({
    code: 0,
    data: { ...pending, status: "authorized" },
  });
  fireEvent.click(screen.getByText("terabox.account.refresh"));
  await screen.findByText("terabox.authorization.status.authorized");
  expect(api.completeTeraBoxAuthorization).toHaveBeenCalledTimes(1);
  expect(api.startTeraBoxAuthorization).toHaveBeenCalledTimes(1);
  expect(screen.queryByRole("alert")).toBeNull();
});

it("locks a submitted pending challenge but allows explicit refresh or cancellation", async () => {
  api.getActiveTeraBoxAuthorization.mockResolvedValue({
    code: 0,
    data: { ...pending, problem: "TERABOX_AUTHORIZATION_AWAITING_CREDENTIALS" },
  });
  render(<TeraBoxCliAccountsPanel />);
  expect(await screen.findByLabelText("terabox.authorization.code")).toBeDisabled();
  expect(screen.getByText("terabox.authorization.complete")).toBeDisabled();
  expect(screen.getByText("terabox.authorization.cancel")).toBeEnabled();
  expect(screen.getByText("terabox.account.refresh")).toBeEnabled();
  expect(api.completeTeraBoxAuthorization).not.toHaveBeenCalled();
});

it("reads the account after the saved authorization has reconciled", async () => {
  let activeRead = false;
  api.getActiveTeraBoxAuthorization.mockImplementation(async () => {
    await Promise.resolve();
    activeRead = true;
    return { code: 0, data: { ...pending, status: "authorized" } };
  });
  api.getTeraBoxAccount.mockImplementation(async () => ({
    code: 0,
    data: { isConfigured: activeRead },
  }));
  render(<TeraBoxCliAccountsPanel />);
  await screen.findByText("terabox.authorization.status.authorized");
  expect(screen.getByText("terabox.account.connected")).toBeTruthy();
  expect(screen.queryByRole("alert")).toBeNull();
});

it("restores a pending server session without creating a new challenge or opening a page", async () => {
  api.getActiveTeraBoxAuthorization.mockResolvedValue({ code: 0, data: pending });
  api.getTeraBoxAccount.mockResolvedValue({
    code: 0,
    data: { isConfigured: false, problem: "TERABOX_ACCOUNT_REQUIRED" },
  });
  render(<TeraBoxCliAccountsPanel />);
  expect(await screen.findByLabelText("terabox.authorization.code")).toHaveValue("");
  expect(api.startTeraBoxAuthorization).not.toHaveBeenCalled();
  expect(screen.queryByRole("alert")).toBeNull();
  expect(openExternalUrl).not.toHaveBeenCalled();
});

it("requires successful initial state loading before starting a new authorization", async () => {
  api.getActiveTeraBoxAuthorization.mockRejectedValueOnce(new Error("TERABOX_REQUEST_FAILED"));
  render(<TeraBoxCliAccountsPanel />);
  await screen.findByRole("alert");
  expect(screen.getByText("terabox.account.authorize")).toBeDisabled();
  fireEvent.click(screen.getByText("terabox.account.refresh"));
  await screen.findByText("terabox.account.notConnected");
  await waitFor(() => expect(screen.getByText("terabox.account.authorize")).toBeEnabled());
  expect(api.startTeraBoxAuthorization).not.toHaveBeenCalled();
});

it("keeps failed and expired sessions visible without automatic restarts", async () => {
  api.getActiveTeraBoxAuthorization.mockResolvedValue({
    code: 0,
    data: { ...pending, status: "expired", authorizationUri: null },
  });
  render(<TeraBoxCliAccountsPanel />);
  await screen.findByText("terabox.authorization.status.expired");
  expect(screen.queryByLabelText("terabox.authorization.code")).toBeNull();
  expect(api.startTeraBoxAuthorization).not.toHaveBeenCalled();
});

it("does not announce success when token exchange has not produced a configured account", async () => {
  const onChanged = vi.fn();
  render(<TeraBoxCliAccountsPanel onChanged={onChanged} />);
  fireEvent.change(await start(), { target: { value: "one-time-code" } });
  fireEvent.click(screen.getByText("terabox.authorization.complete"));
  await screen.findByRole("alert");
  expect(screen.queryByText("terabox.authorization.status.authorized")).toBeNull();
  expect(onChanged).not.toHaveBeenCalled();
  expect(screen.getByText("terabox.authorization.complete")).toBeDisabled();
});

it("does not present a saved authorized session as connected if the account is missing", async () => {
  api.getActiveTeraBoxAuthorization.mockResolvedValue({
    code: 0,
    data: { ...pending, status: "authorized" },
  });
  render(<TeraBoxCliAccountsPanel />);
  await screen.findByRole("alert");
  expect(screen.queryByText("terabox.authorization.status.authorized")).toBeNull();
  expect(screen.getByText("terabox.account.notConnected")).toBeTruthy();
  expect(api.startTeraBoxAuthorization).not.toHaveBeenCalled();
});

it("cancels only the session and does not disconnect a configured account", async () => {
  api.getTeraBoxAccount.mockResolvedValue({ code: 0, data: { isConfigured: true } });
  api.getActiveTeraBoxAuthorization.mockResolvedValue({ code: 0, data: pending });
  render(<TeraBoxCliAccountsPanel />);
  const input = await screen.findByLabelText("terabox.authorization.code");
  fireEvent.change(input, { target: { value: "discard-me" } });
  fireEvent.click(screen.getByText("terabox.authorization.cancel"));
  await waitFor(() => expect(screen.queryByLabelText("terabox.authorization.code")).toBeNull());
  expect(api.cancelTeraBoxAuthorization).toHaveBeenCalledWith(pending.id, quiet);
  expect(api.logoutTeraBoxAccount).not.toHaveBeenCalled();
  expect(screen.getByText("terabox.account.connected")).toBeTruthy();
});

it("confirms a danger-colored disconnect and preserves state when the server refuses", async () => {
  api.getTeraBoxAccount.mockResolvedValue({ code: 0, data: { isConfigured: true } });
  api.logoutTeraBoxAccount.mockResolvedValue({ code: 1, message: "TERABOX_ACCOUNT_IN_USE" });
  render(<TeraBoxCliAccountsPanel />);
  await screen.findByText("terabox.account.connected");
  fireEvent.click(screen.getByText("terabox.account.disconnect"));
  const dialog = await screen.findByRole("dialog");
  const confirm = within(dialog).getByRole("button", { name: "terabox.account.disconnect" });
  expect(confirm).toHaveAttribute("data-color", "danger");
  expect(api.logoutTeraBoxAccount).not.toHaveBeenCalled();
  fireEvent.click(confirm);
  await screen.findByRole("alert");
  expect(screen.getByText("terabox.account.connected")).toBeTruthy();
  expect(screen.getByRole("dialog")).toBeTruthy();
});

it("blocks a second submit while the first completion is pending", async () => {
  let resolve!: (value: unknown) => void;
  api.completeTeraBoxAuthorization.mockReturnValue(
    new Promise((r) => {
      resolve = r;
    }),
  );
  render(<TeraBoxCliAccountsPanel />);
  fireEvent.change(await start(), { target: { value: "one-time-code" } });
  const submit = screen.getByText("terabox.authorization.complete");
  fireEvent.click(submit);
  fireEvent.click(submit);
  expect(api.completeTeraBoxAuthorization).toHaveBeenCalledTimes(1);
  await act(async () => {
    resolve({ code: 0, data: { ...pending, status: "failed" } });
  });
  await screen.findByText("terabox.authorization.status.failed");
});
