import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { beforeAll, beforeEach, afterEach, expect, it, vi } from "vitest";
import { createInstance } from "i18next";
import DecompressBalloon from "../components/DecompressBalloon";
import { Button } from "@/components/bakaui";
import { PasswordSearchOrder } from "@/sdk/constants";
import cn from "@/locales/cn/components/fileExplorer.json";
import en from "@/locales/en/components/fileExplorer.json";
import commonCn from "@/locales/cn/common.json";
import commonEn from "@/locales/en/common.json";

const mocks = vi.hoisted(() => ({ search: vi.fn(), all: vi.fn(), remove: vi.fn(), decompress: vi.fn(), owner: vi.fn(), success: vi.fn() }));
const i18n = createInstance();
vi.mock("@/sdk/BApi", () => ({ default: {
  password: { searchPasswords: mocks.search, getAllPasswords: mocks.all, deletePassword: mocks.remove },
  file: { decompressFiles: mocks.decompress },
} }));
vi.mock("@/features/management/resourceContext", () => ({ useResourceOwnerScope: mocks.owner }));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: i18n.t }) }));
vi.mock("@/components/bakaui", async () => ({
  ...(await import("@/test/radioControls")),
  Button: ({ children, onPress, onClick, isDisabled, isLoading, ...rest }: any) => <button type="button"
    aria-pressed={rest["aria-pressed"]} aria-label={rest["aria-label"]} disabled={isDisabled || isLoading}
    onClick={event => { onClick?.(event); onPress?.(); }}>{children}</button>,
  Input: ({ label, value, onValueChange, type, isDisabled }: any) => <input aria-label={label} type={type}
    value={value} disabled={isDisabled} onChange={event => onValueChange(event.target.value)} />,
  Modal: ({ title, visible = true, children, footer, onClose, isDismissable = true }: any) => visible ?
    <div role="dialog" aria-label={typeof title === "string" ? title : "Extract dialog"}>
      <header>{title}</header><button aria-label="Dismiss" disabled={!isDismissable} onClick={onClose}>×</button>
      {children}<footer>{footer}</footer>
    </div> : null,
  Spinner: (props: any) => <span role="status" aria-label={props["aria-label"]} />,
  Tooltip: ({ children, content }: any) => <span title={content}>{children}</span>,
  toast: { success: mocks.success },
}));
const saved = (text: string) => ({ text, usedTimes: 1, lastUsedAt: "2026-11-11T12:00:00Z" });
const ui = (path = "/work/archive.zip", passwords = ["filename-candidate"]) =>
  <DecompressBalloon entry={{ path }} passwords={passwords} trigger={<Button>Open extraction</Button>} />;
const open = () => fireEvent.click(screen.getByRole("button", { name: "Open extraction" }));
const password = () => screen.getByRole("textbox", { name: cn["fileExplorer.decompress.password"] });
const start = () => screen.getByRole("button", { name: cn["fileExplorer.decompress.start"] });

beforeAll(async () => { await i18n.init({ lng: "cn", fallbackLng: false, keySeparator: false,
  interpolation: { escapeValue: false }, resources: { cn: { translation: { ...commonCn, ...cn } }, en: { translation: { ...commonEn, ...en } } } }); });
beforeEach(async () => {
  vi.clearAllMocks(); await i18n.changeLanguage("cn");
  mocks.owner.mockReturnValue(undefined);
  mocks.search.mockResolvedValue({ code: 0, data: [] });
  mocks.all.mockResolvedValue({ code: 0, data: [saved("saved-choice")] });
  mocks.decompress.mockResolvedValue({ code: 0 });
});
afterEach(cleanup);

it("opens only on an explicit press, fetches bounded choices on demand and never starts from hovering or opening", async () => {
  render(ui()); fireEvent.mouseOver(screen.getByRole("button"));
  expect(screen.queryByRole("dialog")).toBeNull(); expect(mocks.search).not.toHaveBeenCalled();
  open(); expect(screen.getByRole("dialog", { name: "Extract dialog" })).toBeVisible();
  expect(password()).toHaveAttribute("type", "text"); expect(password()).toHaveValue("");
  await waitFor(() => expect(mocks.search).toHaveBeenCalledTimes(2));
  expect(mocks.search).toHaveBeenCalledWith({ order: PasswordSearchOrder.Latest, pageSize: 5 }, expect.objectContaining({ sensitiveResponse: true }));
  expect(mocks.decompress).not.toHaveBeenCalled(); expect(mocks.all).not.toHaveBeenCalled();
});

it("merges duplicate choices with their origins, and selection only fills the exact password", async () => {
  mocks.search.mockImplementation(({ order }: any) => Promise.resolve({ code: 0, data:
    order === PasswordSearchOrder.Latest ? [saved("same"), saved("recent")] : [saved("same"), saved("frequent")] }));
  render(ui("/work/archive.zip", ["same", " spaces "])); open();
  await screen.findByRole("button", { name: "frequent" });
  const choice = screen.getAllByRole("button", { name: "same" }); expect(choice).toHaveLength(1);
  expect(choice[0].parentElement).toHaveAttribute("title", "文件名候选，尚未验证 · 最近使用 · 常用密码");
  fireEvent.click(choice[0]); expect(password()).toHaveValue("same");
  fireEvent.click(screen.getByRole("button", { name: "spaces" })); expect(password()).toHaveValue(" spaces ");
  expect(mocks.decompress).not.toHaveBeenCalled(); expect(mocks.remove).not.toHaveBeenCalled();
});

it("uses the existing saved-password selector in selection-only mode", async () => {
  render(ui()); open(); fireEvent.click(screen.getByRole("button", { name: cn["passwordSelector.choose"] }));
  fireEvent.click(await screen.findByRole("button", { name: "saved-choice" }));
  expect(password()).toHaveValue("saved-choice");
  expect(screen.getAllByRole("dialog")).toHaveLength(1);
  expect(mocks.all).toHaveBeenCalledOnce(); expect(mocks.remove).not.toHaveBeenCalled(); expect(mocks.decompress).not.toHaveBeenCalled();
});

it("submits an explicit empty password to disable filename fallback, only after pressing start", async () => {
  render(ui()); open(); fireEvent.click(screen.getByRole("button", { name: "filename-candidate" }));
  fireEvent.click(screen.getByRole("button", { name: cn["fileExplorer.decompress.noPassword"] }));
  expect(mocks.decompress).not.toHaveBeenCalled(); fireEvent.click(start());
  expect(mocks.decompress).toHaveBeenCalledWith({ paths: ["/work/archive.zip"], password: "" }, expect.objectContaining({ sensitiveResponse: true }));
  await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  expect(mocks.success).toHaveBeenCalledWith(cn["fileExplorer.decompress.started"]);
});

it("locks repeated submission, retains a plain-text server error and the draft, then permits a deliberate retry", async () => {
  let resolve!: (value: unknown) => void;
  mocks.decompress.mockReturnValueOnce(new Promise(done => { resolve = done; }));
  const view = render(ui()); open(); fireEvent.change(password(), { target: { value: " Exact value " } });
  fireEvent.click(start()); fireEvent.click(start());
  expect(mocks.decompress).toHaveBeenCalledOnce(); expect(start()).toBeDisabled();
  expect(screen.getByRole("button", { name: "关闭" })).toBeDisabled();
  expect(screen.getByRole("button", { name: "选择已保存密码" })).toBeDisabled();
  await act(async () => resolve({ code: 500, message: "<script>refused</script>" }));
  expect(screen.getByRole("alert")).toHaveTextContent("<script>refused</script>");
  expect(view.container.querySelector("script")).toBeNull(); expect(password()).toHaveValue(" Exact value ");
  fireEvent.click(start()); await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  expect(mocks.decompress.mock.lastCall?.[0].password).toBe(" Exact value ");
});

it("keeps manual input usable when a saved-password query fails and allows retrying that read", async () => {
  mocks.search.mockRejectedValueOnce(new Error("unavailable"));
  render(ui()); open(); fireEvent.change(password(), { target: { value: "manual" } });
  await screen.findByText(cn["fileExplorer.decompress.passwordsFailed"]);
  expect(password()).toHaveValue("manual"); expect(start()).toBeEnabled();
  fireEvent.click(screen.getByRole("button", { name: "重试" }));
  await waitFor(() => expect(screen.queryByText(cn["fileExplorer.decompress.passwordsFailed"])).toBeNull());
  expect(mocks.decompress).not.toHaveBeenCalled();
});

it("ignores late password reads after closing, including when another path opens", async () => {
  const pending: { resolve: (value: any) => void; signal: AbortSignal }[] = [];
  mocks.search.mockImplementation((_query: any, options: any) => new Promise(resolve => pending.push({ resolve, signal: options.signal })));
  const view = render(ui()); open(); fireEvent.click(screen.getByRole("button", { name: "关闭" }));
  expect(pending.every(request => request.signal.aborted)).toBe(true);
  mocks.search.mockResolvedValue({ code: 0, data: [saved("new-choice")] });
  view.rerender(ui("/work/new.zip", [])); open(); await screen.findByRole("button", { name: "new-choice" });
  await act(async () => { pending.forEach(request => request.resolve({ code: 0, data: [saved("old-secret")] })); });
  expect(screen.queryByRole("button", { name: "old-secret" })).toBeNull(); expect(mocks.decompress).not.toHaveBeenCalled();
});

it.each(["path", "owner"])("blocks submission when its selected %s changes without rerouting a password", async changed => {
  const view = render(ui()); open(); fireEvent.change(password(), { target: { value: "private" } });
  if (changed === "owner") mocks.owner.mockReturnValue({ key: "remote", source: { isLocal: false } });
  view.rerender(ui(changed === "path" ? "/work/other.zip" : "/work/archive.zip"));
  expect(start()).toBeDisabled(); expect(screen.getByRole("alert")).toHaveTextContent(cn["fileExplorer.decompress.unavailable"]);
  fireEvent.click(start()); expect(mocks.decompress).not.toHaveBeenCalled();
});

it("does not read this server's password library for a remote owner", () => {
  mocks.owner.mockReturnValue({ key: "remote", source: { isLocal: false } }); render(ui()); open();
  expect(mocks.search).not.toHaveBeenCalled(); expect(mocks.all).not.toHaveBeenCalled(); expect(start()).toBeDisabled();
});

it("wraps long paths within the small dialog and supplies all new labels in both languages", async () => {
  for (const key of Object.keys(cn).filter(key => key.startsWith("fileExplorer.decompress.")))
    expect((en as Record<string, string>)[key], key).toBeTruthy();
  const path = `/work/${"long-folder/".repeat(30)}archive.zip`;
  render(ui(path)); open();
  expect(screen.getByText(path)).toHaveClass("break-all", "max-h-20", "overflow-auto");
  await act(async () => i18n.changeLanguage("en"));
  expect(screen.getByRole("button", { name: "Start extraction" })).toBeVisible();
  expect(screen.queryByText("fp.te.db.defaultPassword")).toBeNull();
  expect(mocks.decompress).not.toHaveBeenCalled();
});
