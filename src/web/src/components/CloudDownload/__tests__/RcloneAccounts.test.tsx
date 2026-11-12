import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { Children, useEffect } from "react";
import RcloneAccountsPanel from "../RcloneAccountsPanel";
import RcloneAccountSelect from "../RcloneAccountSelect";

const api = vi.hoisted(() => ({
  getRcloneAccounts: vi.fn(),
  saveRcloneAccount: vi.fn(),
  setDefaultRcloneAccount: vi.fn(),
  validateRcloneAccount: vi.fn(),
  deleteRcloneAccount: vi.fn(),
  startRcloneAuthorization: vi.fn(),
  getRcloneAuthorization: vi.fn(),
  answerRcloneAuthorization: vi.fn(),
  cancelRcloneAuthorization: vi.fn(),
  startRcloneBrowserAuthorization: vi.fn(),
  stopRcloneBrowserAuthorization: vi.fn(),
  submitRcloneBrowserAuthorizationCallback: vi.fn(),
  getRcloneAuthorizationDrives: vi.fn(),
  previewRcloneAuthorizationDrive: vi.fn(),
}));
const openExternalUrl = vi.hoisted(() => vi.fn());
const oauthStatus = vi.hoisted(() => ({
  value: { mode: "manual", isReady: true, hasUnsavedChanges: false },
}));
vi.mock("@/utils/openExternalUrl", () => ({ openExternalUrl }));
vi.mock("@/components/OAuthClientConfiguration", () => ({
  default: ({ providerId, onStatusChange }: any) => {
    useEffect(() => {
      let active = true;
      void Promise.resolve().then(() => {
        if (active) onStatusChange(oauthStatus.value);
      });
      return () => {
        active = false;
      };
    }, [providerId, onStatusChange]);
    return <span>OAuth configuration: {providerId}</span>;
  },
}));
vi.mock("@/sdk/BApi", () => ({ default: { rclone: api } }));
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string | string[], options?: { defaultValue?: string }) => {
      const selectedKey = Array.isArray(key) ? key[key.length - 1] : key;
      return selectedKey.startsWith("rclone.authorization.question.")
        ? (options?.defaultValue ?? selectedKey)
        : selectedKey;
    },
  }),
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, isDisabled }: any) => (
    <button disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Input: ({ label, value, onValueChange, type, isDisabled }: any) => (
    <label>
      {label}
      <input
        type={type}
        value={value}
        disabled={isDisabled}
        onChange={(e) => onValueChange(e.target.value)}
      />
    </label>
  ),
  Select: ({
    label,
    selectedKeys,
    disabledKeys = [],
    dataSource,
    isDisabled,
    onSelectionChange,
  }: any) => (
    <select
      aria-label={label}
      value={selectedKeys[0] ?? ""}
      disabled={isDisabled}
      onChange={(e) => onSelectionChange(new Set([e.target.value]))}
    >
      <option value="">Default</option>
      {dataSource.map((item: any) => (
        <option key={item.value} value={item.value} disabled={disabledKeys.includes(item.value)}>
          {item.label}
        </option>
      ))}
    </select>
  ),
  Switch: ({ children, isSelected, onValueChange, isDisabled }: any) => (
    <label>
      {children}
      <input
        type="checkbox"
        checked={isSelected}
        disabled={isDisabled}
        onChange={(e) => onValueChange(e.target.checked)}
      />
    </label>
  ),
  Spinner: () => <span>loading</span>,
  Chip: ({ children }: any) => <span>{children}</span>,
  Accordion: ({ children, selectedKeys, onSelectionChange, isDisabled }: any) => (
    <div>
      {Children.map(children, (child: any) => {
        const open = selectedKeys?.includes(child.key) ?? false;
        return (
          <div>
            <button
              disabled={isDisabled}
              onClick={() => onSelectionChange?.(new Set(open ? [] : [child.key]))}
            >
              {child.props.title}
            </button>
            {open && child.props.children}
          </div>
        );
      })}
    </div>
  ),
  AccordionItem: ({ children }: any) => <div>{children}</div>,
}));
const authOptions = { showErrorToast: false, sensitiveResponse: true };
const account = {
  id: "account-1",
  name: "My drive",
  providerId: "oneDrive",
  isConfigured: false,
  isDefault: false,
};
const session = {
  id: "session-1",
  accountId: "account-1",
  status: "awaitingInput",
  authorizationMode: "manual",
  expiresAt: "2999-10-21T10:00:00Z",
  remoteAuthorizeCommand: "rclone authorize onedrive",
  question: {
    id: "question-token",
    name: "config_token",
    help: "Paste the result",
    type: "string",
    required: true,
    isSecret: true,
    exclusive: false,
    examples: [],
  },
};
const browserSession = {
  ...session,
  browser: {
    status: "awaitingCallback",
    authorizationUrl:
      "https://login.microsoftonline.com/common/oauth2/v2.0/authorize?state=fixture",
    expiresAt: "2999-10-21T10:00:00Z",
  },
};
const suggestedDriveSession = {
  ...session,
  remoteAuthorizeCommand: null,
  question: {
    ...session.question,
    id: "drive-suggestions",
    name: "config_driveid",
    isSecret: false,
    exclusive: false,
    defaultValue: "drive-1",
    examples: [
      { value: "drive-1", help: "Team documents" },
      { value: "drive-2", help: "Project archive" },
    ],
  },
};
beforeEach(() => {
  vi.resetAllMocks();
  oauthStatus.value = { mode: "manual", isReady: true, hasUnsavedChanges: false };
  api.getRcloneAccounts.mockResolvedValue({ code: 0, data: [account] });
  api.saveRcloneAccount.mockResolvedValue({ code: 0, data: account });
  api.startRcloneAuthorization.mockResolvedValue({ code: 0, data: session });
  api.getRcloneAuthorizationDrives.mockImplementation(async (_id, query) => ({
    code: 0,
    data: { questionId: query.questionId, candidates: [] },
  }));
  api.cancelRcloneAuthorization.mockResolvedValue({ code: 0 });
  api.startRcloneBrowserAuthorization.mockResolvedValue({ code: 0, data: browserSession });
  api.stopRcloneBrowserAuthorization.mockResolvedValue({
    code: 0,
    data: { ...session, browser: { ...browserSession.browser, status: "cancelled" } },
  });
});
afterEach(() => vi.useRealTimers());

const openDriveChoices = async () => {
  const existing = screen.queryByRole("combobox", { name: "config_driveid" });
  if (existing) return existing;
  fireEvent.click(await screen.findByText("rclone.authorization.otherDrives"));
  return screen.findByRole("combobox", { name: "config_driveid" });
};

it("shows non-exclusive suggestions by name and submits the exact selected value", async () => {
  api.startRcloneAuthorization.mockResolvedValue({ code: 0, data: suggestedDriveSession });
  api.answerRcloneAuthorization.mockResolvedValue({
    code: 0,
    data: { ...session, status: "ready", question: null },
  });
  render(<RcloneAccountsPanel providerId="oneDrive" />);
  fireEvent.click(await screen.findByText("rclone.authorization.start"));
  const select = await openDriveChoices();
  expect(select).toHaveValue("");
  expect(within(select).getByRole("option", { name: "Team documents" })).toHaveProperty(
    "selected",
    false,
  );
  expect(api.answerRcloneAuthorization).not.toHaveBeenCalled();
  fireEvent.change(select, { target: { value: "drive-2" } });
  fireEvent.click(screen.getByText("rclone.authorization.answer"));
  await screen.findByText("rclone.authorization.status.ready");
  expect(api.answerRcloneAuthorization).toHaveBeenCalledWith(
    "session-1",
    { questionId: "drive-suggestions", value: "drive-2" },
    authOptions,
  );
});

it("preserves the selected drive on a same-question refresh, resets for the next question and never submits an empty optional drive", async () => {
  const current = {
    ...suggestedDriveSession,
    question: { ...suggestedDriveSession.question, required: false },
  };
  api.startRcloneAuthorization.mockResolvedValue({ code: 0, data: current });
  api.getRcloneAuthorization.mockResolvedValue({ code: 0, data: current });
  api.answerRcloneAuthorization.mockResolvedValue({
    code: 0,
    data: { ...current, question: { ...current.question, id: "new-drive-question" } },
  });
  render(<RcloneAccountsPanel providerId="oneDrive" />);
  fireEvent.click(await screen.findByText("rclone.authorization.start"));
  const select = await openDriveChoices();
  expect(select).toHaveValue("");
  expect(screen.getByText("rclone.authorization.answer")).toBeDisabled();
  fireEvent.change(select, { target: { value: "drive-2" } });
  fireEvent.click(screen.getByText("rclone.authorization.refresh"));
  await waitFor(() => expect(api.getRcloneAuthorization).toHaveBeenCalledTimes(1));
  await waitFor(() => expect(screen.getByText("rclone.authorization.answer")).toBeEnabled());
  expect(select).toHaveValue("drive-2");
  expect(api.getRcloneAuthorizationDrives).toHaveBeenCalledTimes(1);
  fireEvent.click(screen.getByText("rclone.authorization.answer"));
  await waitFor(() => expect(api.getRcloneAuthorizationDrives).toHaveBeenCalledTimes(2));
  expect(await openDriveChoices()).toHaveValue("");
  expect(screen.getByText("rclone.authorization.answer")).toBeDisabled();
});

it("retains a manual drive across refresh without reloading metadata, and explains why unlisted IDs cannot be previewed", async () => {
  api.startRcloneAuthorization.mockResolvedValue({ code: 0, data: suggestedDriveSession });
  api.getRcloneAuthorization.mockResolvedValue({ code: 0, data: suggestedDriveSession });
  render(<RcloneAccountsPanel providerId="oneDrive" />);
  fireEvent.click(await screen.findByText("rclone.authorization.start"));
  fireEvent.click(await screen.findByText("rclone.authorization.enterManualAnswer"));
  fireEvent.change(screen.getByRole("textbox", { name: "config_driveid" }), {
    target: { value: "custom-drive" },
  });
  expect(screen.getByText("rclone.authorization.manualDrivePreviewUnavailable")).toBeVisible();
  expect(screen.queryByText("rclone.authorization.previewRoot")).toBeNull();
  fireEvent.click(screen.getByText("rclone.authorization.refresh"));
  await waitFor(() => expect(api.getRcloneAuthorization).toHaveBeenCalledTimes(1));
  expect(screen.getByRole("textbox", { name: "config_driveid" })).toHaveValue("custom-drive");
  expect(api.getRcloneAuthorizationDrives).toHaveBeenCalledTimes(1);
});

it("preserves an unlisted default as a manual value and clears it when switching to suggestions", async () => {
  api.startRcloneAuthorization.mockResolvedValue({
    code: 0,
    data: {
      ...suggestedDriveSession,
      question: { ...suggestedDriveSession.question, defaultValue: "custom-drive" },
    },
  });
  render(<RcloneAccountsPanel providerId="oneDrive" />);
  fireEvent.click(await screen.findByText("rclone.authorization.start"));
  expect(await screen.findByRole("textbox", { name: "config_driveid" })).toHaveValue(
    "custom-drive",
  );
  expect(screen.queryByRole("combobox", { name: "config_driveid" })).toBeNull();
  fireEvent.click(screen.getByText("rclone.authorization.useSuggestedAnswer"));
  expect(await openDriveChoices()).toHaveValue("");
  expect(screen.getByText("rclone.authorization.answer")).toBeDisabled();
  expect(api.answerRcloneAuthorization).not.toHaveBeenCalled();
});

it("allows an explicit custom answer for non-exclusive suggestions", async () => {
  api.startRcloneAuthorization.mockResolvedValue({ code: 0, data: suggestedDriveSession });
  api.answerRcloneAuthorization.mockResolvedValue({
    code: 0,
    data: { ...session, status: "ready", question: null },
  });
  render(<RcloneAccountsPanel providerId="oneDrive" />);
  fireEvent.click(await screen.findByText("rclone.authorization.start"));
  fireEvent.click(await screen.findByText("rclone.authorization.enterManualAnswer"));
  const input = screen.getByRole("textbox", { name: "config_driveid" });
  expect(input).toHaveValue("");
  fireEvent.change(input, { target: { value: "custom-drive" } });
  fireEvent.click(screen.getByText("rclone.authorization.answer"));
  await screen.findByText("rclone.authorization.status.ready");
  expect(api.answerRcloneAuthorization).toHaveBeenCalledWith(
    "session-1",
    { questionId: "drive-suggestions", value: "custom-drive" },
    authOptions,
  );
});

it("keeps exclusive choices restricted and resets custom-answer mode for a new question", async () => {
  api.startRcloneAuthorization.mockResolvedValue({ code: 0, data: suggestedDriveSession });
  api.answerRcloneAuthorization.mockResolvedValue({
    code: 0,
    data: {
      ...session,
      problem: "RCLONE_CONFIGURATION_ACCESS_DENIED",
      question: {
        ...suggestedDriveSession.question,
        id: "connection-choice",
        name: "config_type",
        exclusive: true,
        defaultValue: "onedrive",
        examples: [{ value: "onedrive", help: "OneDrive" }],
      },
    },
  });
  render(<RcloneAccountsPanel providerId="oneDrive" />);
  fireEvent.click(await screen.findByText("rclone.authorization.start"));
  fireEvent.click(await screen.findByText("rclone.authorization.enterManualAnswer"));
  fireEvent.change(screen.getByLabelText("config_driveid"), {
    target: { value: "custom-drive" },
  });
  fireEvent.click(screen.getByText("rclone.authorization.answer"));
  expect(await screen.findByRole("combobox", { name: "config_type" })).toHaveValue("onedrive");
  expect(screen.getByRole("alert")).toHaveTextContent(
    "rclone.problem.RCLONE_CONFIGURATION_ACCESS_DENIED",
  );
  expect(screen.queryByText("rclone.authorization.enterManualAnswer")).toBeNull();
  expect(screen.queryByText("rclone.authorization.useSuggestedAnswer")).toBeNull();
  expect(screen.queryByDisplayValue("custom-drive")).toBeNull();
  expect(api.answerRcloneAuthorization).toHaveBeenCalledTimes(1);
});

it("never exposes secret defaults or suggestions as selectable answers", async () => {
  api.startRcloneAuthorization.mockResolvedValue({
    code: 0,
    data: {
      ...suggestedDriveSession,
      question: {
        ...suggestedDriveSession.question,
        isSecret: true,
        defaultValue: "secret-default",
        examples: [{ value: "secret-option", help: "Secret suggestion" }],
      },
    },
  });
  render(<RcloneAccountsPanel providerId="oneDrive" />);
  fireEvent.click(await screen.findByText("rclone.authorization.start"));
  const input = await screen.findByLabelText("rclone.authorization.secretAnswer");
  expect(input).toHaveAttribute("type", "password");
  expect(input).toHaveValue("");
  expect(screen.queryByRole("combobox")).toBeNull();
  expect(screen.queryByText("Secret suggestion")).toBeNull();
  expect(screen.queryByText("rclone.authorization.enterManualAnswer")).toBeNull();
});

it("listing and saving OneDrive accounts never starts authorization without a user action", async () => {
  render(<RcloneAccountsPanel providerId="oneDrive" />);
  await screen.findByText("My drive");
  expect(api.startRcloneAuthorization).not.toHaveBeenCalled();
  fireEvent.change(screen.getByLabelText("cloudDownload.account.name"), {
    target: { value: "Work" },
  });
  fireEvent.click(screen.getByText("rclone.account.save"));
  await waitFor(() =>
    expect(api.saveRcloneAccount).toHaveBeenCalledWith(
      expect.objectContaining({ providerId: "oneDrive", name: "Work" }),
    ),
  );
  expect(api.startRcloneAuthorization).not.toHaveBeenCalled();
  expect(screen.getByLabelText("cloudDownload.account.name")).toHaveValue("");
});

it("retains native headless OAuth behind advanced mode and renders subsequent drive questions", async () => {
  const changed = vi.fn();
  const driveSession = {
    ...session,
    remoteAuthorizeCommand: null,
    question: {
      ...session.question,
      id: "drive",
      name: "Drive",
      help: "Select a drive",
      isSecret: false,
      exclusive: true,
      examples: [{ value: "drive-1", help: "Team documents" }],
    },
  };
  api.answerRcloneAuthorization
    .mockResolvedValueOnce({ code: 0, data: driveSession })
    .mockResolvedValueOnce({
      code: 0,
      data: { ...session, status: "ready", question: null, remoteAuthorizeCommand: null },
    });
  render(<RcloneAccountsPanel providerId="oneDrive" onChanged={changed} />);
  fireEvent.click(await screen.findByText("rclone.authorization.start"));
  await waitFor(() => expect(screen.getByText("rclone.browser.advanced")).not.toBeDisabled());
  fireEvent.click(screen.getByText("rclone.browser.advanced"));
  expect(await screen.findByText("rclone authorize onedrive")).toBeVisible();
  expect(api.stopRcloneBrowserAuthorization).toHaveBeenCalledWith("session-1", authOptions);
  const token = screen.getByLabelText("rclone.authorization.secretAnswer");
  expect(token).toHaveAttribute("type", "password");
  fireEvent.change(token, { target: { value: "private-fixture-token" } });
  fireEvent.click(screen.getByText("rclone.authorization.answer"));
  await waitFor(() =>
    expect(api.answerRcloneAuthorization).toHaveBeenCalledWith(
      "session-1",
      {
        questionId: "question-token",
        value: "private-fixture-token",
      },
      authOptions,
    ),
  );
  expect(screen.queryByDisplayValue("private-fixture-token")).toBeNull();
  fireEvent.change(await screen.findByLabelText("Drive"), { target: { value: "drive-1" } });
  fireEvent.click(screen.getByText("rclone.authorization.answer"));
  await screen.findByText("rclone.authorization.status.ready");
  expect(api.answerRcloneAuthorization).toHaveBeenLastCalledWith(
    "session-1",
    {
      questionId: "drive",
      value: "drive-1",
    },
    authOptions,
  );
  expect(changed).toHaveBeenCalledTimes(1);
});

it("does not retain a submitted authorization secret after an API failure", async () => {
  api.answerRcloneAuthorization.mockResolvedValue({ code: 400, message: "Authorization rejected" });
  render(<RcloneAccountsPanel providerId="oneDrive" />);
  fireEvent.click(await screen.findByText("rclone.authorization.start"));
  await waitFor(() => expect(screen.getByText("rclone.browser.advanced")).not.toBeDisabled());
  fireEvent.click(screen.getByText("rclone.browser.advanced"));
  const input = await screen.findByLabelText("rclone.authorization.secretAnswer");
  fireEvent.change(input, { target: { value: "private-fixture-token" } });
  fireEvent.click(screen.getByText("rclone.authorization.answer"));
  await screen.findByRole("alert");
  expect(input).toHaveValue("");
  expect(api.startRcloneAuthorization).toHaveBeenCalledTimes(1);
});

it("sends provider-specific write-only credentials and clears them after a successful save", async () => {
  api.getRcloneAccounts.mockResolvedValue({ code: 0, data: [] });
  const changed = vi.fn();
  render(<RcloneAccountsPanel providerId="pikpak" onChanged={changed} />);
  fireEvent.change(screen.getByLabelText("cloudDownload.account.name"), {
    target: { value: "Personal" },
  });
  fireEvent.change(screen.getByLabelText("rclone.account.username"), {
    target: { value: "person@example.test" },
  });
  fireEvent.change(screen.getByLabelText("rclone.account.password"), {
    target: { value: "fixture-password" },
  });
  fireEvent.click(screen.getByText("rclone.account.save"));
  await waitFor(() => expect(changed).toHaveBeenCalledTimes(1));
  expect(api.saveRcloneAccount).toHaveBeenCalledWith(
    expect.objectContaining({
      providerId: "pikpak",
      username: "person@example.test",
      password: "fixture-password",
    }),
  );
  expect(screen.getByLabelText("rclone.account.password")).toHaveValue("");
  expect(api.startRcloneAuthorization).not.toHaveBeenCalled();
});

it("cancels only the explicitly started session", async () => {
  render(<RcloneAccountsPanel providerId="oneDrive" />);
  fireEvent.click(await screen.findByText("rclone.authorization.start"));
  const region = await screen.findByRole("region");
  await waitFor(() => expect(within(region).getByText("common.action.cancel")).not.toBeDisabled());
  fireEvent.click(within(region).getByText("common.action.cancel"));
  await waitFor(() =>
    expect(api.cancelRcloneAuthorization).toHaveBeenCalledWith("session-1", authOptions),
  );
  expect(screen.queryByRole("region")).toBeNull();
});

it("requires a Pixeldrain root and a key for the personal filesystem, but permits a public root", async () => {
  api.getRcloneAccounts.mockResolvedValue({ code: 0, data: [] });
  render(<RcloneAccountsPanel providerId="pixeldrain" />);
  fireEvent.change(screen.getByLabelText("cloudDownload.account.name"), {
    target: { value: "Public folder" },
  });
  expect(screen.getByLabelText("rclone.account.rootFolderId")).toHaveValue("me");
  expect(screen.getByText("rclone.account.save")).toBeDisabled();
  fireEvent.change(screen.getByLabelText("rclone.account.rootFolderId"), {
    target: { value: "public-directory-id" },
  });
  fireEvent.click(screen.getByText("rclone.account.save"));
  await waitFor(() =>
    expect(api.saveRcloneAccount).toHaveBeenCalledWith(
      expect.objectContaining({
        providerId: "pixeldrain",
        rootFolderId: "public-directory-id",
        apiKey: undefined,
      }),
    ),
  );
});

it("filters account selection by provider, disables unconfigured accounts, and ignores stale lists", async () => {
  let finishOld!: (value: unknown) => void;
  api.getRcloneAccounts
    .mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finishOld = resolve;
        }),
    )
    .mockResolvedValueOnce({
      code: 0,
      data: [{ ...account, id: "pikpak-1", providerId: "pikpak", name: "PikPak pending" }],
    });
  const changed = vi.fn();
  const { rerender } = render(<RcloneAccountSelect providerId="oneDrive" onChange={changed} />);
  rerender(<RcloneAccountSelect providerId="pikpak" onChange={changed} />);
  expect(await screen.findByRole("option", { name: /PikPak pending/ })).toBeDisabled();
  finishOld({ code: 0, data: [account] });
  await waitFor(() =>
    expect(api.getRcloneAccounts).toHaveBeenLastCalledWith({ providerId: "pikpak" }),
  );
  expect(screen.queryByRole("option", { name: /My drive/ })).toBeNull();
});

it("ignores an authorization response from a previous provider", async () => {
  let finish!: (value: unknown) => void;
  api.startRcloneAuthorization.mockImplementation(
    () =>
      new Promise((resolve) => {
        finish = resolve;
      }),
  );
  const changed = vi.fn();
  const { rerender } = render(<RcloneAccountsPanel providerId="oneDrive" onChanged={changed} />);
  fireEvent.click(await screen.findByText("rclone.authorization.start"));
  rerender(<RcloneAccountsPanel providerId="pikpak" onChanged={changed} />);
  await act(async () => {
    finish({ code: 0, data: { ...session, status: "ready" } });
  });
  expect(screen.queryByRole("region")).toBeNull();
  expect(changed).not.toHaveBeenCalled();
  expect(screen.getByLabelText("rclone.account.username")).not.toBeDisabled();
});

it("does not let a late save clear another provider's editor or invalidate its readiness", async () => {
  let finish!: (value: unknown) => void;
  api.saveRcloneAccount.mockImplementation(
    () =>
      new Promise((resolve) => {
        finish = resolve;
      }),
  );
  const changed = vi.fn();
  const { rerender } = render(<RcloneAccountsPanel providerId="oneDrive" onChanged={changed} />);
  fireEvent.change(screen.getByLabelText("cloudDownload.account.name"), {
    target: { value: "Old" },
  });
  fireEvent.click(screen.getByText("rclone.account.save"));
  rerender(<RcloneAccountsPanel providerId="pikpak" onChanged={changed} />);
  fireEvent.change(screen.getByLabelText("cloudDownload.account.name"), {
    target: { value: "New" },
  });
  await act(async () => {
    finish({ code: 0, data: account });
  });
  expect(screen.getByLabelText("cloudDownload.account.name")).toHaveValue("New");
  expect(changed).not.toHaveBeenCalled();
});

it("prepares guided authorization, opens Microsoft only on click, and clears the callback before submission", async () => {
  let finish!: (value: unknown) => void;
  api.submitRcloneBrowserAuthorizationCallback.mockImplementation(
    () =>
      new Promise((resolve) => {
        finish = resolve;
      }),
  );
  render(<RcloneAccountsPanel providerId="oneDrive" />);
  fireEvent.click(await screen.findByText("rclone.authorization.start"));
  const login = await screen.findByText("rclone.browser.openLogin");
  await waitFor(() => expect(login).not.toBeDisabled());
  expect(api.startRcloneBrowserAuthorization).toHaveBeenCalledWith(
    "session-1",
    {
      questionId: "question-token",
    },
    authOptions,
  );
  expect(openExternalUrl).not.toHaveBeenCalled();
  expect(screen.queryByText("rclone authorize onedrive")).toBeNull();
  fireEvent.click(login);
  expect(openExternalUrl).toHaveBeenCalledWith(browserSession.browser.authorizationUrl);

  const callback = screen.getByLabelText("rclone.browser.callbackUrl");
  expect(callback).toHaveAttribute("type", "password");
  fireEvent.change(callback, {
    target: { value: "http://localhost:53682/?state=fixture&code=private-fixture-code" },
  });
  fireEvent.click(screen.getByText("rclone.browser.submit"));
  expect(callback).toHaveValue("");
  expect(api.submitRcloneBrowserAuthorizationCallback).toHaveBeenCalledWith(
    "session-1",
    {
      questionId: "question-token",
      callbackUrl: "http://localhost:53682/?state=fixture&code=private-fixture-code",
    },
    authOptions,
  );
  await act(async () => {
    finish({ code: 400, message: "RCLONE_BROWSER_AUTHORIZATION_STATE_MISMATCH" });
  });
  expect(callback).toHaveValue("");
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "rclone.problem.RCLONE_BROWSER_AUTHORIZATION_STATE_MISMATCH",
  );
  expect(api.submitRcloneBrowserAuthorizationCallback).toHaveBeenCalledTimes(1);
});

it("continues native drive selection after a guided callback and releases ownership when ready", async () => {
  api.submitRcloneBrowserAuthorizationCallback.mockResolvedValue({
    code: 0,
    data: {
      ...session,
      remoteAuthorizeCommand: null,
      question: {
        ...session.question,
        id: "drive-choice",
        name: "config_driveid",
        isSecret: false,
        exclusive: true,
        examples: [{ value: "drive-1", help: "Documents" }],
      },
      browser: { ...browserSession.browser, status: "completed" },
    },
  });
  api.answerRcloneAuthorization.mockResolvedValue({
    code: 0,
    data: { ...session, status: "ready", question: null },
  });
  const changed = vi.fn();
  const { unmount } = render(<RcloneAccountsPanel providerId="oneDrive" onChanged={changed} />);
  fireEvent.click(await screen.findByText("rclone.authorization.start"));
  await waitFor(() => expect(screen.getByText("rclone.browser.openLogin")).not.toBeDisabled());
  fireEvent.change(screen.getByLabelText("rclone.browser.callbackUrl"), {
    target: { value: "http://localhost:53682/?state=fixture&code=one-use-code" },
  });
  fireEvent.click(screen.getByText("rclone.browser.submit"));
  fireEvent.change(await openDriveChoices(), {
    target: { value: "drive-1" },
  });
  fireEvent.click(screen.getByText("rclone.authorization.answer"));
  await waitFor(() => expect(changed).toHaveBeenCalledTimes(1));
  unmount();
  expect(api.cancelRcloneAuthorization).not.toHaveBeenCalled();
});

it("does not expose CLI inputs until the browser listener has stopped", async () => {
  let finish!: (value: unknown) => void;
  api.stopRcloneBrowserAuthorization.mockImplementation(
    () =>
      new Promise((resolve) => {
        finish = resolve;
      }),
  );
  render(<RcloneAccountsPanel providerId="oneDrive" />);
  fireEvent.click(await screen.findByText("rclone.authorization.start"));
  await waitFor(() => expect(screen.getByText("rclone.browser.advanced")).not.toBeDisabled());
  fireEvent.click(screen.getByText("rclone.browser.advanced"));
  expect(screen.queryByLabelText("rclone.authorization.secretAnswer")).toBeNull();
  await act(async () => {
    finish({ code: 400, message: "RCLONE_REQUEST_FAILED" });
  });
  expect(screen.queryByLabelText("rclone.authorization.secretAnswer")).toBeNull();
  expect(screen.getByLabelText("rclone.browser.callbackUrl")).toBeVisible();
});

it("cleans up only its active authorization on provider change and ignores a late browser result", async () => {
  let finish!: (value: unknown) => void;
  api.startRcloneBrowserAuthorization.mockImplementation(
    () =>
      new Promise((resolve) => {
        finish = resolve;
      }),
  );
  const { rerender } = render(<RcloneAccountsPanel providerId="oneDrive" />);
  fireEvent.click(await screen.findByText("rclone.authorization.start"));
  await waitFor(() => expect(api.startRcloneBrowserAuthorization).toHaveBeenCalledTimes(1));
  rerender(<RcloneAccountsPanel providerId="pikpak" />);
  expect(api.cancelRcloneAuthorization).toHaveBeenCalledWith("session-1", authOptions);
  await act(async () => {
    finish({ code: 0, data: browserSession });
  });
  expect(screen.queryByText("rclone.browser.openLogin")).toBeNull();
  expect(api.cancelRcloneAuthorization.mock.calls.every(([id]) => id === "session-1")).toBe(true);
  expect(screen.getByLabelText("rclone.account.username")).not.toBeDisabled();
});

it("cancels an awaiting session created after the panel unmounts", async () => {
  let finish!: (value: unknown) => void;
  api.startRcloneAuthorization.mockImplementation(
    () =>
      new Promise((resolve) => {
        finish = resolve;
      }),
  );
  const { unmount } = render(<RcloneAccountsPanel providerId="oneDrive" />);
  fireEvent.click(await screen.findByText("rclone.authorization.start"));
  unmount();
  await act(async () => {
    finish({ code: 0, data: session });
  });
  expect(api.cancelRcloneAuthorization).toHaveBeenCalledWith("session-1", authOptions);
  expect(api.startRcloneBrowserAuthorization).not.toHaveBeenCalled();
});

it("clears callback input and prevents replay when the server reports a failed exchange", async () => {
  api.submitRcloneBrowserAuthorizationCallback.mockResolvedValue({
    code: 0,
    data: {
      ...browserSession,
      browser: {
        ...browserSession.browser,
        status: "failed",
        problem: "RCLONE_BROWSER_AUTHORIZATION_CALLBACK_FAILED",
      },
    },
  });
  const { unmount } = render(<RcloneAccountsPanel providerId="oneDrive" />);
  fireEvent.click(await screen.findByText("rclone.authorization.start"));
  await waitFor(() => expect(screen.getByText("rclone.browser.openLogin")).not.toBeDisabled());
  fireEvent.change(screen.getByLabelText("rclone.browser.callbackUrl"), {
    target: { value: "http://localhost:53682/?state=fixture&code=one-use-code" },
  });
  fireEvent.click(screen.getByText("rclone.browser.submit"));
  await screen.findByText("rclone.problem.RCLONE_BROWSER_AUTHORIZATION_CALLBACK_FAILED");
  expect(screen.getByLabelText("rclone.browser.callbackUrl")).toHaveValue("");
  expect(screen.getByText("rclone.browser.submit")).toBeDisabled();
  expect(screen.getByText("rclone.browser.restart")).toBeEnabled();
  unmount();
  expect(api.cancelRcloneAuthorization).toHaveBeenCalledWith("session-1", authOptions);
});

it("does not offer a restart or replay while the server is exchanging the callback", async () => {
  api.startRcloneBrowserAuthorization.mockResolvedValue({
    code: 0,
    data: { ...browserSession, browser: { ...browserSession.browser, status: "exchanging" } },
  });
  render(<RcloneAccountsPanel providerId="oneDrive" />);
  fireEvent.click(await screen.findByText("rclone.authorization.start"));
  expect(await screen.findByRole("status")).toHaveTextContent("rclone.browser.exchanging");
  expect(screen.getByText("rclone.browser.submit")).toBeDisabled();
  expect(screen.queryByText("rclone.browser.restart")).toBeNull();
});

it("can cancel while the browser is starting and ignores its late response", async () => {
  let finish!: (value: unknown) => void;
  api.startRcloneBrowserAuthorization.mockImplementation(
    () =>
      new Promise((resolve) => {
        finish = resolve;
      }),
  );
  render(<RcloneAccountsPanel providerId="oneDrive" />);
  fireEvent.click(await screen.findByText("rclone.authorization.start"));
  const region = await screen.findByRole("region");
  const cancel = within(region).getByText("common.action.cancel");
  expect(cancel).toBeEnabled();
  fireEvent.click(cancel);
  await waitFor(() => expect(screen.queryByRole("region")).toBeNull());
  expect(screen.getByText("My drive")).toBeVisible();
  await act(async () => {
    finish({ code: 0, data: browserSession });
  });
  expect(screen.queryByRole("region")).toBeNull();
  expect(api.cancelRcloneAuthorization.mock.calls.every(([id]) => id === "session-1")).toBe(true);
});

it("can cancel an in-flight callback without accepting its late successful result", async () => {
  let finish!: (value: unknown) => void;
  api.submitRcloneBrowserAuthorizationCallback.mockImplementation(
    () =>
      new Promise((resolve) => {
        finish = resolve;
      }),
  );
  const changed = vi.fn();
  render(<RcloneAccountsPanel providerId="oneDrive" onChanged={changed} />);
  fireEvent.click(await screen.findByText("rclone.authorization.start"));
  await waitFor(() => expect(screen.getByText("rclone.browser.openLogin")).toBeEnabled());
  fireEvent.change(screen.getByLabelText("rclone.browser.callbackUrl"), {
    target: { value: "http://localhost:53682/?state=fixture&code=one-use-code" },
  });
  fireEvent.click(screen.getByText("rclone.browser.submit"));
  const cancel = within(screen.getByRole("region")).getByText("common.action.cancel");
  expect(cancel).toBeEnabled();
  fireEvent.click(cancel);
  await waitFor(() => expect(screen.queryByRole("region")).toBeNull());
  await act(async () => {
    finish({ code: 0, data: { ...session, status: "ready", question: null } });
  });
  expect(screen.queryByText("rclone.authorization.status.ready")).toBeNull();
  expect(changed).not.toHaveBeenCalled();
});

it("keeps a failed cancellation refreshable and retryable without restoring stale state", async () => {
  let finish!: (value: unknown) => void;
  api.startRcloneBrowserAuthorization.mockImplementation(
    () =>
      new Promise((resolve) => {
        finish = resolve;
      }),
  );
  api.cancelRcloneAuthorization
    .mockResolvedValueOnce({ code: 400, message: "RCLONE_REQUEST_FAILED" })
    .mockResolvedValue({ code: 0 });
  api.getRcloneAuthorization.mockResolvedValue({ code: 0, data: browserSession });
  render(<RcloneAccountsPanel providerId="oneDrive" />);
  fireEvent.click(await screen.findByText("rclone.authorization.start"));
  fireEvent.click(within(await screen.findByRole("region")).getByText("common.action.cancel"));
  await screen.findByRole("alert");
  const refresh = screen.getByText("rclone.authorization.refresh");
  expect(refresh).toBeEnabled();
  fireEvent.click(refresh);
  await waitFor(() =>
    expect(api.getRcloneAuthorization).toHaveBeenCalledWith("session-1", authOptions),
  );
  await waitFor(() =>
    expect(within(screen.getByRole("region")).getByText("common.action.cancel")).toBeEnabled(),
  );
  await act(async () => {
    finish({ code: 0, data: browserSession });
  });
  expect(api.cancelRcloneAuthorization).toHaveBeenCalledTimes(1);
  expect(screen.getByRole("region")).toBeVisible();
  fireEvent.click(within(screen.getByRole("region")).getByText("common.action.cancel"));
  await waitFor(() => expect(screen.queryByRole("region")).toBeNull());
});

it.each([
  {
    message:
      "Bakabase.Service.Components.CloudDownloads.Rclone.RcloneRemoteException: RCLONE_BROWSER_AUTHORIZATION_STATE_MISMATCH\n" +
      "   at Bakabase.Service.Components.CloudDownloads.Rclone.RcloneBrowserAuthorizationProcess.SubmitAsync(String sessionId, String questionId, String callbackUrl, CancellationToken ct)\n" +
      "   at Bakabase.Service.Controllers.RcloneAccountsController.SubmitRcloneBrowserAuthorizationCallback(...)",
    expected: "rclone.problem.RCLONE_BROWSER_AUTHORIZATION_STATE_MISMATCH",
  },
  { message: "Unexpected connection failure", expected: "Unexpected connection failure" },
])(
  "localizes wrapped expected errors while preserving unknown errors: $expected",
  async ({ message, expected }) => {
    api.getRcloneAccounts.mockResolvedValue({ code: 400, message });
    render(<RcloneAccountsPanel providerId="oneDrive" />);
    const alert = await screen.findByRole("alert");
    expect(alert.textContent).toBe(expected);
    expect(alert).not.toHaveTextContent("RcloneRemoteException");
    expect(alert).not.toHaveTextContent("SubmitAsync");
  },
);

it.each([
  { mode: "automatic", isReady: false, hasUnsavedChanges: false },
  { mode: "manual", isReady: true, hasUnsavedChanges: true },
])(
  "blocks a new authorization when configuration is incomplete or unsaved: $mode",
  async (status) => {
    oauthStatus.value = status;
    render(<RcloneAccountsPanel providerId="oneDrive" />);
    expect(await screen.findByText("rclone.authorization.start")).toBeDisabled();
    expect(api.startRcloneAuthorization).not.toHaveBeenCalled();
  },
);

it("automatically follows a web callback into drive selection without displaying manual inputs", async () => {
  oauthStatus.value = { mode: "automatic", isReady: true, hasUnsavedChanges: false };
  const automaticSession = { ...browserSession, authorizationMode: "automatic" };
  api.startRcloneAuthorization.mockResolvedValue({
    code: 0,
    data: { ...session, authorizationMode: "automatic" },
  });
  api.startRcloneBrowserAuthorization.mockResolvedValue({ code: 0, data: automaticSession });
  api.getRcloneAuthorization.mockResolvedValue({
    code: 0,
    data: {
      ...automaticSession,
      question: {
        ...session.question,
        id: "drive",
        name: "Drive",
        isSecret: false,
        exclusive: true,
        examples: [{ value: "drive-1", help: "Documents" }],
      },
    },
  });
  render(<RcloneAccountsPanel providerId="oneDrive" />);
  const start = await screen.findByText("rclone.authorization.start");
  await waitFor(() => expect(start).toBeEnabled());
  vi.useFakeTimers();
  await act(async () => {
    fireEvent.click(start);
  });
  expect(screen.getByText("rclone.browser.automaticWaiting")).toBeVisible();
  expect(screen.queryByLabelText("rclone.browser.callbackUrl")).toBeNull();
  expect(screen.queryByText("rclone.browser.advanced")).toBeNull();
  expect(openExternalUrl).not.toHaveBeenCalled();
  fireEvent.click(screen.getByText("rclone.browser.openLogin"));
  expect(openExternalUrl).toHaveBeenCalledTimes(1);
  await act(async () => {
    await vi.advanceTimersByTimeAsync(2000);
  });
  expect(screen.getByLabelText("Drive")).toBeVisible();
  expect(api.getRcloneAuthorization).toHaveBeenCalledWith("session-1", authOptions);
  await act(async () => {
    await vi.advanceTimersByTimeAsync(6000);
  });
  expect(api.getRcloneAuthorization).toHaveBeenCalledTimes(1);
  expect(api.submitRcloneBrowserAuthorizationCallback).not.toHaveBeenCalled();
});

it("stops automatic polling on cancel and ignores the in-flight poll result", async () => {
  let finish!: (value: unknown) => void;
  const automaticSession = { ...browserSession, authorizationMode: "automatic" };
  api.startRcloneAuthorization.mockResolvedValue({
    code: 0,
    data: { ...session, authorizationMode: "automatic" },
  });
  api.startRcloneBrowserAuthorization.mockResolvedValue({ code: 0, data: automaticSession });
  api.getRcloneAuthorization.mockImplementation(
    () =>
      new Promise((resolve) => {
        finish = resolve;
      }),
  );
  const changed = vi.fn();
  render(<RcloneAccountsPanel providerId="oneDrive" onChanged={changed} />);
  const start = await screen.findByText("rclone.authorization.start");
  await waitFor(() => expect(start).toBeEnabled());
  vi.useFakeTimers();
  await act(async () => {
    fireEvent.click(start);
  });
  await act(async () => {
    await vi.advanceTimersByTimeAsync(2000);
  });
  expect(api.getRcloneAuthorization).toHaveBeenCalledTimes(1);
  await act(async () => {
    fireEvent.click(within(screen.getByRole("region")).getByText("common.action.cancel"));
  });
  await act(async () => {
    finish({ code: 0, data: { ...automaticSession, status: "ready", question: null } });
    await vi.advanceTimersByTimeAsync(10000);
  });
  expect(screen.queryByRole("region")).toBeNull();
  expect(changed).not.toHaveBeenCalled();
  expect(api.getRcloneAuthorization).toHaveBeenCalledTimes(1);
});

it("stops automatic polling when the account panel is unmounted", async () => {
  api.startRcloneBrowserAuthorization.mockResolvedValue({
    code: 0,
    data: { ...browserSession, authorizationMode: "automatic" },
  });
  const { unmount } = render(<RcloneAccountsPanel providerId="oneDrive" />);
  const start = await screen.findByText("rclone.authorization.start");
  await waitFor(() => expect(start).toBeEnabled());
  vi.useFakeTimers();
  await act(async () => {
    fireEvent.click(start);
  });
  unmount();
  await act(async () => {
    await vi.advanceTimersByTimeAsync(10000);
  });
  expect(api.getRcloneAuthorization).not.toHaveBeenCalled();
  expect(api.cancelRcloneAuthorization).toHaveBeenCalledWith("session-1", authOptions);
});
