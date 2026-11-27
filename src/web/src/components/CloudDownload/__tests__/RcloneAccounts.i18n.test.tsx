import { useEffect, useState } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import i18n from "@/i18n";
import RcloneAccountsPanel from "../RcloneAccountsPanel";

const api = vi.hoisted(() => ({
  getRcloneAccounts: vi.fn(),
  startRcloneAuthorization: vi.fn(),
  answerRcloneAuthorization: vi.fn(),
  cancelRcloneAuthorization: vi.fn(),
  getRcloneAuthorizationDrives: vi.fn(),
  previewRcloneAuthorizationDrive: vi.fn(),
}));
// Use the application resources and parseMissingKeyHandler, not the global key-only test mock.
vi.mock("react-i18next", async (importOriginal) => await importOriginal());
vi.mock("@/sdk/BApi", () => ({ default: { rclone: api } }));
vi.mock("@/utils/openExternalUrl", () => ({ openExternalUrl: vi.fn() }));
vi.mock("@/components/OAuthClientConfiguration", () => ({
  default: function MockOAuthClientConfiguration({ onStatusChange }: any) {
    useEffect(() => {
      let active = true;
      void Promise.resolve().then(() => {
        if (active) onStatusChange({ mode: "manual", isReady: false, hasUnsavedChanges: false });
      });
      return () => {
        active = false;
      };
    }, [onStatusChange]);
    return null;
  },
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
        onChange={(event) => onValueChange(event.target.value)}
      />
    </label>
  ),
  Select: ({ label, selectedKeys, dataSource, isDisabled, onSelectionChange }: any) => (
    <select
      aria-label={label}
      value={selectedKeys[0] ?? ""}
      disabled={isDisabled}
      onChange={(event) => onSelectionChange(new Set([event.target.value]))}
    >
      <option value="" />
      {dataSource.map((item: any) => (
        <option key={item.value} value={item.value}>
          {item.label}
        </option>
      ))}
    </select>
  ),
  Switch: () => null,
  Spinner: () => null,
  Chip: ({ children }: any) => <span>{children}</span>,
  Accordion: ({ children }: any) => <div>{children}</div>,
  AccordionItem: ({ title, children }: any) => {
    const [open, setOpen] = useState(false);
    return (
      <div>
        <button onClick={() => setOpen(!open)}>{title}</button>
        {open && children}
      </div>
    );
  },
}));

beforeEach(() => {
  vi.resetAllMocks();
  api.getRcloneAccounts.mockResolvedValue({
    code: 0,
    data: [{ id: "fixture-account", name: "Test account", isConfigured: false, isDefault: false }],
  });
  api.cancelRcloneAuthorization.mockResolvedValue({ code: 0 });
  api.getRcloneAuthorizationDrives.mockImplementation(async (_id, query) => ({
    code: 0,
    data: { questionId: query.questionId, candidates: [] },
  }));
});
afterEach(cleanup);

it.each(["config_type", "config_driveid", "config_drive_ok"])(
  "renders translated %s guidance with the production missing-key policy and language changes",
  async (name) => {
    await i18n.changeLanguage("zh-CN");
    api.startRcloneAuthorization.mockResolvedValue({
      code: 0,
      data: {
        id: "fixture-session",
        accountId: "fixture-account",
        status: "awaitingInput",
        authorizationMode: "manual",
        expiresAt: "2999-10-21T10:00:00Z",
        question: {
          id: "fixture-question",
          name,
          help: "",
          type: "string",
          required: true,
          isSecret: false,
          exclusive: true,
          defaultValue: "fixture-choice",
          examples: [{ value: "fixture-choice", help: "Fixture option" }],
        },
      },
    });
    const hintKey =
      name === "config_driveid"
        ? "rclone.authorization.questionHint.config_driveid"
        : "rclone.authorization.selectionHint";
    if (name !== "config_driveid")
      expect(i18n.exists(`rclone.authorization.questionHint.${name}`)).toBe(false);
    render(<RcloneAccountsPanel providerId="oneDrive" />);
    fireEvent.click(await screen.findByText(i18n.t<string>("rclone.authorization.start")));
    const region = await screen.findByRole("region", {
      name: i18n.t<string>("rclone.authorization.title"),
    });
    expect(within(region).getByText(i18n.t<string>(hintKey))).toBeVisible();
    expect(region.textContent).not.toContain("rclone.authorization.questionHint.");
    await act(async () => {
      await i18n.changeLanguage("en-US");
    });
    expect(within(region).getByText(i18n.t<string>(hintKey))).toBeVisible();
    expect(region.textContent).not.toContain("rclone.authorization.questionHint.");
  },
);

it("renders understandable connection choices in both languages and submits the native choice", async () => {
  await i18n.changeLanguage("zh-CN");
  api.startRcloneAuthorization.mockResolvedValue({
    code: 0,
    data: {
      id: "fixture-session",
      accountId: "fixture-account",
      status: "awaitingInput",
      authorizationMode: "manual",
      expiresAt: "2999-10-21T10:00:00Z",
      question: {
        id: "type-choice",
        name: "config_type",
        help: "",
        type: "string",
        required: true,
        isSecret: false,
        exclusive: true,
        defaultValue: "onedrive",
        examples: ["onedrive", "sharepoint", "url", "search", "driveid", "siteid", "path"].map(
          (value) => ({ value, help: `Native ${value}` }),
        ),
      },
    },
  });
  api.answerRcloneAuthorization.mockResolvedValue({
    code: 0,
    data: { id: "fixture-session", status: "ready", question: null },
  });
  render(<RcloneAccountsPanel providerId="oneDrive" />);
  fireEvent.click(await screen.findByText(i18n.t<string>("rclone.authorization.start")));
  const select = await screen.findByRole("combobox", { name: "选择磁盘或站点类型" });
  expect(
    within(select).getByRole("option", {
      name: "常用 · 我的 OneDrive（个人 / 工作或学校）",
    }),
  ).toHaveProperty("selected", true);
  expect(within(select).getByRole("option", { name: "高级 · 按磁盘 ID 连接" })).toHaveValue(
    "driveid",
  );
  expect(api.answerRcloneAuthorization).not.toHaveBeenCalled();
  await act(async () => {
    await i18n.changeLanguage("en-US");
  });
  expect(
    within(select).getByRole("option", {
      name: "Common · My OneDrive (personal / work or school)",
    }),
  ).toHaveValue("onedrive");
  fireEvent.change(select, { target: { value: "url" } });
  fireEvent.click(screen.getByText(i18n.t<string>("rclone.authorization.answer")));
  await waitFor(() =>
    expect(api.answerRcloneAuthorization).toHaveBeenCalledWith(
      "fixture-session",
      { questionId: "type-choice", value: "url" },
      { showErrorToast: false, sensitiveResponse: true },
    ),
  );
});

it("displays numbered opaque drives and friendly personal names, with exact IDs only in details and submitted values", async () => {
  await i18n.changeLanguage("zh-CN");
  const opaqueName = "b!abcdefghijklmnopqrstuvwxyz0123456789";
  api.startRcloneAuthorization.mockResolvedValue({
    code: 0,
    data: {
      id: "fixture-session",
      accountId: "fixture-account",
      status: "awaitingInput",
      authorizationMode: "manual",
      expiresAt: "2999-10-21T10:00:00Z",
      question: {
        id: "drive-choice",
        name: "config_driveid",
        help: "",
        type: "string",
        required: true,
        isSecret: false,
        exclusive: false,
        defaultValue: "opaque-drive-id",
        examples: [
          { value: "opaque-drive-id", help: `${opaqueName} (business)` },
          { value: "personal-drive-id", help: "OneDrive (personal)" },
        ],
      },
    },
  });
  api.answerRcloneAuthorization.mockResolvedValue({
    code: 0,
    data: {
      id: "fixture-session",
      accountId: "fixture-account",
      status: "awaitingInput",
      authorizationMode: "manual",
      expiresAt: "2999-10-21T10:00:00Z",
      question: {
        id: "confirm-choice",
        name: "config_drive_ok",
        help: "",
        type: "bool",
        required: false,
        isSecret: false,
        exclusive: true,
        defaultValue: "true",
        examples: [
          { value: "true", help: "Yes" },
          { value: "false", help: "No" },
        ],
      },
    },
  });
  render(<RcloneAccountsPanel providerId="oneDrive" />);
  fireEvent.click(await screen.findByText(i18n.t<string>("rclone.authorization.start")));
  fireEvent.click(await screen.findByText(i18n.t<string>("rclone.authorization.otherDrives")));
  const select = await screen.findByRole("combobox", { name: "选择磁盘" });
  expect(select).toHaveValue("");
  fireEvent.change(select, { target: { value: "opaque-drive-id" } });
  expect(within(select).getByRole("option", { name: "OneDrive · 个人网盘" })).toHaveValue(
    "personal-drive-id",
  );
  expect(screen.queryByText(`${opaqueName} (business)`)).toBeNull();
  fireEvent.click(screen.getByText("查看所选项目的原始信息"));
  expect(screen.getByText(`${opaqueName} (business)`)).toBeVisible();
  expect(screen.getByText("opaque-drive-id")).toBeVisible();
  fireEvent.change(select, { target: { value: "personal-drive-id" } });
  expect(screen.queryByText("opaque-drive-id")).toBeNull();
  fireEvent.click(screen.getByText(i18n.t<string>("rclone.authorization.answer")));
  expect(await screen.findByRole("combobox", { name: "确认使用此磁盘" })).toHaveValue("true");
  expect(screen.getByRole("option", { name: "使用此磁盘" })).toHaveValue("true");
  expect(api.answerRcloneAuthorization).toHaveBeenCalledExactlyOnceWith(
    "fixture-session",
    { questionId: "drive-choice", value: "personal-drive-id" },
    { showErrorToast: false, sensitiveResponse: true },
  );
});
