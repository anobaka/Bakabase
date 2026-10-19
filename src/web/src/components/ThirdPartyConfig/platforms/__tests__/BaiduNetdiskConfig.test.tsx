import type { ReactNode } from "react";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import BaiduNetdiskConfig from "../BaiduNetdiskConfig";

const api = vi.hoisted(() => ({ getBaiduPcsAccounts: vi.fn(), loginBaiduPcsAccount: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({ default: { baiduPcs: api } }));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/pages/configuration/components/Dependency/components/Component", () => ({
  default: () => <span>Dependency</span>,
}));
vi.mock("@/components/ExternalLink", () => ({
  default: ({ children }: { children: ReactNode }) => <span>{children}</span>,
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
  Input: ({
    label,
    value,
    onValueChange,
    type,
  }: {
    label: string;
    value: string;
    onValueChange: (value: string) => void;
    type?: string;
  }) => (
    <label>
      {label}
      <input type={type} value={value} onChange={(event) => onValueChange(event.target.value)} />
    </label>
  ),
  Switch: ({
    children,
    isSelected,
    onValueChange,
  }: {
    children: ReactNode;
    isSelected: boolean;
    onValueChange: (value: boolean) => void;
  }) => (
    <label>
      <input
        type="checkbox"
        checked={isSelected}
        onChange={(event) => onValueChange(event.target.checked)}
      />
      {children}
    </label>
  ),
  Spinner: () => null,
}));

beforeEach(() => {
  api.getBaiduPcsAccounts.mockReset().mockResolvedValue({ code: 0, data: [] });
  api.loginBaiduPcsAccount.mockReset().mockResolvedValue({ code: 0, data: { id: "account-1" } });
});

describe("Baidu credentials", () => {
  it("imports a cookie through the account API and clears the credential fields after success", async () => {
    render(<BaiduNetdiskConfig />);
    fireEvent.change(screen.getByLabelText("cloudDownload.account.name"), {
      target: { value: "Primary" },
    });
    fireEvent.change(screen.getByLabelText("Cookie"), {
      target: { value: "BDUSS=test-value; STOKEN=another-value" },
    });
    fireEvent.click(screen.getByRole("button", { name: "cloudDownload.account.login" }));
    await waitFor(() =>
      expect(api.loginBaiduPcsAccount).toHaveBeenCalledWith({
        name: "Primary",
        cookies: "BDUSS=test-value; STOKEN=another-value",
      }),
    );
    await waitFor(() => expect(screen.getByLabelText("Cookie")).toHaveValue(""));
    expect(screen.getByLabelText("Cookie")).toHaveAttribute("type", "password");
  });

  it("requires STOKEN as well as BDUSS when entering separate credentials", async () => {
    render(<BaiduNetdiskConfig />);
    fireEvent.click(screen.getByLabelText("cloudDownload.account.cookieMode"));
    fireEvent.change(screen.getByLabelText("cloudDownload.account.name"), {
      target: { value: "Primary" },
    });
    fireEvent.change(screen.getByLabelText("BDUSS"), { target: { value: "identity" } });
    expect(screen.getByRole("button", { name: "cloudDownload.account.login" })).toBeDisabled();
    fireEvent.change(screen.getByLabelText("STOKEN"), { target: { value: "share-session" } });
    expect(screen.getByRole("button", { name: "cloudDownload.account.login" })).not.toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "cloudDownload.account.login" }));
    await waitFor(() =>
      expect(api.loginBaiduPcsAccount).toHaveBeenCalledWith({
        name: "Primary",
        bduss: "identity",
        stoken: "share-session",
      }),
    );
  });
});
