import { useState } from "react";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";

import ArchivePasswordInput from "../ArchivePasswordInput";

const { read, remove, owner } = vi.hoisted(() => ({
  read: vi.fn(),
  remove: vi.fn(),
  owner: vi.fn(),
}));
vi.mock("@/sdk/BApi", () => ({
  default: { password: { getAllPasswords: read, deletePassword: remove } },
}));
vi.mock("@/features/management/resourceContext", () => ({ useResourceOwnerScope: owner }));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (k: string) => k }) }));
vi.mock("@/components/bakaui", async () => ({
  ...(await import("@/test/radioControls")),
  Input: ({ label, value, type, onValueChange }: any) => (
    <input
      aria-label={label}
      type={type}
      value={value}
      onChange={(e) => onValueChange(e.target.value)}
    />
  ),
  Button: ({ children, isDisabled, onPress }: any) => (
    <button type="button" disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Modal: ({ title, children }: any) => (
    <div role="dialog" aria-label={title}>
      {children}
    </div>
  ),
  Spinner: () => <span role="status" />,
  Tooltip: ({ children }: any) => children,
}));

function Field({ submit }: { submit: () => void }) {
  const [value, setValue] = useState("");
  return (
    <form
      onSubmit={(e) => {
        e.preventDefault();
        submit();
      }}
    >
      <ArchivePasswordInput label="Archive password" value={value} onValueChange={setValue} />
    </form>
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  owner.mockReturnValue(undefined);
  read.mockResolvedValue({
    code: 0,
    data: [{ text: "fixture", usedTimes: 2, lastUsedAt: "2026-01-01" }],
  });
});

it("loads history on demand and returns a choice to the editable draft without submitting", async () => {
  const submit = vi.fn();
  render(<Field submit={submit} />);
  const input = screen.getByRole("textbox", { name: "Archive password" });
  fireEvent.change(input, { target: { value: "typed" } });
  expect(input).toHaveAttribute("type", "text");
  expect(read).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "passwordSelector.choose" }));
  fireEvent.click(await screen.findByRole("button", { name: /fixture/ }));
  expect(input).toHaveValue("fixture");
  expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  expect(read).toHaveBeenCalledTimes(1);
  expect(remove).not.toHaveBeenCalled();
  expect(submit).not.toHaveBeenCalled();
});

it("does not fall back to this server's password library for a different owner", async () => {
  owner.mockReturnValue({ key: "peer", source: { isLocal: false } });
  render(<Field submit={vi.fn()} />);
  fireEvent.click(screen.getByRole("button", { name: "passwordSelector.choose" }));
  expect(await screen.findByText("passwordSelector.unavailable")).toBeInTheDocument();
  expect(read).not.toHaveBeenCalled();
});

it("does not lose a typed password when the saved list is unavailable", async () => {
  read.mockResolvedValue({ code: 403 });
  render(<Field submit={vi.fn()} />);
  fireEvent.change(screen.getByRole("textbox"), { target: { value: "manual" } });
  fireEvent.click(screen.getByRole("button", { name: "passwordSelector.choose" }));
  await waitFor(() => expect(screen.getByText("passwordSelector.unavailable")).toBeInTheDocument());
  expect(screen.getByRole("textbox")).toHaveValue("manual");
});
