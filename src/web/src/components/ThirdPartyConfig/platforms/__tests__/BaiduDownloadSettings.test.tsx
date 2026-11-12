import type { ReactNode } from "react";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import BaiduDownloadSettings from "../BaiduDownloadSettings";
import type { BaiduAccount } from "../BaiduNetdiskConfig";

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, values?: Record<string, unknown>) =>
      values ? `${key} ${JSON.stringify(values)}` : key,
  }),
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({
    children,
    onPress,
    isDisabled,
  }: {
    children: ReactNode;
    onPress: () => void;
    isDisabled?: boolean;
  }) => (
    <button disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  NumberInput: ({ label, value, onValueChange, isDisabled, minValue, maxValue }: any) => (
    <label>
      {label}
      <input
        type="number"
        value={value}
        disabled={isDisabled}
        min={minValue}
        max={maxValue}
        onChange={(e) => onValueChange(e.target.value === "" ? NaN : Number(e.target.value))}
      />
    </label>
  ),
  Switch: ({ children, isSelected, onValueChange, isDisabled }: any) => (
    <label>
      <input
        type="checkbox"
        checked={isSelected}
        disabled={isDisabled}
        onChange={(e) => onValueChange(e.target.checked)}
      />
      {children}
    </label>
  ),
  Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
}));
afterEach(cleanup);
const account = (extra = {}): BaiduAccount => ({
  id: "account-1",
  name: "Baidu",
  isLoggedIn: true,
  isDefault: true,
  downloadOptions: { maxConcurrentTasks: 1, connectionsPerFile: 1, userConfirmedSvip: false },
  effectiveMaxConcurrentTasks: 1,
  effectiveConnectionsPerFile: 1,
  membership: { kind: "unknown", probeMethod: "baidu-xpan-uinfo" },
  ...extra,
});
const key = (value: string) => `cloudDownload.baidu.concurrency.${value}`;

describe("Baidu concurrency configuration", () => {
  it("keeps legacy accounts at one task and one connection without an automatic write", () => {
    const save = vi.fn();
    const refresh = vi.fn();
    render(
      <BaiduDownloadSettings
        account={account({ downloadOptions: null })}
        busy={false}
        onSave={save}
        onRefresh={refresh}
      />,
    );
    expect(screen.getByLabelText(key("tasks"))).toHaveValue(1);
    expect(screen.getByLabelText(key("connections"))).toHaveValue(1);
    expect(screen.getByText("cloudDownload.baidu.membership.unknown")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: key("save") })).toBeDisabled();
    expect(save).not.toHaveBeenCalled();
    expect(refresh).not.toHaveBeenCalled();
  });

  it("saves distinct task and file settings with explicit manual confirmation", () => {
    const save = vi.fn();
    render(
      <BaiduDownloadSettings account={account()} busy={false} onSave={save} onRefresh={vi.fn()} />,
    );
    fireEvent.change(screen.getByLabelText(key("tasks")), { target: { value: "5" } });
    fireEvent.change(screen.getByLabelText(key("connections")), { target: { value: "10" } });
    fireEvent.click(screen.getByLabelText(key("confirmSvip")));
    fireEvent.click(screen.getByRole("button", { name: key("save") }));
    expect(save).toHaveBeenCalledExactlyOnceWith({
      maxConcurrentTasks: 5,
      connectionsPerFile: 10,
      userConfirmedSvip: true,
    });
    // Configured values are not presented as effective until confirmed by the server.
    expect(screen.getByText((value) => value.startsWith(key("effective")))).toHaveTextContent(
      '"tasks":1,"connections":1',
    );
  });

  it("refreshes membership independently, preserving unsaved settings and failed-observation history", () => {
    const refresh = vi.fn();
    const save = vi.fn();
    const view = render(
      <BaiduDownloadSettings account={account()} busy={false} onSave={save} onRefresh={refresh} />,
    );
    fireEvent.change(screen.getByLabelText(key("connections")), { target: { value: "4" } });
    fireEvent.click(screen.getByRole("button", { name: "cloudDownload.baidu.membership.refresh" }));
    expect(refresh).toHaveBeenCalledOnce();
    expect(save).not.toHaveBeenCalled();
    view.rerender(
      <BaiduDownloadSettings
        account={account({
          membership: {
            kind: "svip",
            checkedAt: "2026-11-05T00:00:00Z",
            failureCode: "BAIDU_MEMBERSHIP_UNAVAILABLE",
          },
        })}
        busy={false}
        onSave={save}
        onRefresh={refresh}
      />,
    );
    expect(screen.getByLabelText(key("connections"))).toHaveValue(4);
    expect(screen.getByRole("status")).toHaveTextContent("cloudDownload.baidu.membership.failed");
  });

  it("rejects out-of-range input and disables actions while logged out", () => {
    const view = render(
      <BaiduDownloadSettings
        account={account()}
        busy={false}
        onSave={vi.fn()}
        onRefresh={vi.fn()}
      />,
    );
    fireEvent.change(screen.getByLabelText(key("tasks")), { target: { value: "6" } });
    expect(screen.getByRole("button", { name: key("save") })).toBeDisabled();
    view.rerender(
      <BaiduDownloadSettings
        account={account({ isLoggedIn: false })}
        busy={false}
        onSave={vi.fn()}
        onRefresh={vi.fn()}
      />,
    );
    expect(screen.getByLabelText(key("tasks"))).toBeDisabled();
    expect(
      screen.getByRole("button", { name: "cloudDownload.baidu.membership.refresh" }),
    ).toBeDisabled();
  });
});
