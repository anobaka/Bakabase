import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import ProfilesManagement from "./ProfilesSettings";
import { useDesktopProfiles } from "./useDesktopProfiles";

function ProfilesSettings({ query }: { query?: string }) {
  const controller = useDesktopProfiles();

  return <ProfilesManagement controller={controller} query={query} />;
}

const reportMatch = vi.hoisted(() => vi.fn());

const api = vi.hoisted(() => ({
  getDesktopProfiles: vi.fn(),
  addDesktopProfile: vi.fn(),
  updateDesktopProfile: vi.fn(),
  removeDesktopProfile: vi.fn(),
  openDesktopProfile: vi.fn(),
}));

vi.mock("@/stores/remoteAccess", () => ({
  useRemoteAccessStore: (selector: any) =>
    selector({ context: "known", clientMode: 0, isLocal: true }),
}));
vi.mock("@/pages/configuration/components/SettingsSection", () => ({
  useSettingsSearchMatch: reportMatch,
}));
vi.mock("@/sdk/BApi", () => ({ default: { app: api } }));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));
vi.mock("@/components/FileSystemSelector", () => ({ FileSystemSelectorModal: () => null }));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, isDisabled, type }: any) => (
    <button disabled={isDisabled} type={type || "button"} onClick={onPress}>
      {children}
    </button>
  ),
  Input: ({ label, value, onValueChange, isDisabled, ...props }: any) => (
    <label>
      {label}
      <input
        aria-label={label || props["aria-label"]}
        disabled={isDisabled}
        value={value}
        onChange={(event) => onValueChange(event.target.value)}
      />
    </label>
  ),
}));
const current = {
  id: "default",
  name: "Main",
  dataDirectory: "/main",
  isCurrent: true,
  isDefault: true,
  isRunning: true,
  isAvailable: true,
};
const other = {
  id: "other",
  name: "Test space",
  dataDirectory: "/test",
  isCurrent: false,
  isDefault: false,
  isRunning: false,
  isAvailable: true,
};
const catalog = {
  available: true,
  currentId: "default",
  environmentOverride: false,
  currentDirectory: "/main",
  profiles: [current, other],
};

beforeEach(() => {
  vi.clearAllMocks();
  api.getDesktopProfiles.mockResolvedValue({ data: catalog });
  for (const key of [
    "addDesktopProfile",
    "updateDesktopProfile",
    "removeDesktopProfile",
    "openDesktopProfile",
  ] as const)
    api[key].mockResolvedValue({ code: 0 });
});

describe("desktop workspaces", () => {
  it("surfaces an unreadable registry in the local desktop instead of silently hiding it", async () => {
    api.getDesktopProfiles.mockRejectedValue(new Error("Registry is unreadable"));
    render(<ProfilesSettings />);
    expect(await screen.findByRole("alert")).toHaveTextContent("Registry is unreadable");
  });
  it("reports matched sections to the existing configuration search and hides unmatched searches", async () => {
    const { rerender } = render(<ProfilesSettings query="profiles" />);

    await screen.findByText("Main");
    expect(reportMatch).toHaveBeenLastCalledWith("desktop-profiles", true);
    rerender(<ProfilesSettings query="not a matching section" />);
    expect(screen.queryByText("Main")).not.toBeInTheDocument();
    expect(reportMatch).toHaveBeenLastCalledWith("desktop-profiles", false);
  });
  it("is absent on a headless server", async () => {
    api.getDesktopProfiles.mockResolvedValue({ data: { available: false, profiles: [] } });
    const { container } = render(<ProfilesSettings />);

    await waitFor(() => expect(api.getDesktopProfiles).toHaveBeenCalledOnce());
    expect(container).toBeEmptyDOMElement();
  });
  it("distinguishes opening a second window from switching and closing this one", async () => {
    render(<ProfilesSettings />);
    fireEvent.click(await screen.findByRole("button", { name: "profiles.open" }));
    await waitFor(() =>
      expect(api.openDesktopProfile).toHaveBeenCalledWith("other", { closeCurrent: false }),
    );
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "profiles.switch" })).not.toBeDisabled(),
    );
    fireEvent.click(screen.getByRole("button", { name: "profiles.switch" }));
    await waitFor(() =>
      expect(api.openDesktopProfile).toHaveBeenLastCalledWith("other", { closeCurrent: true }),
    );
  });
  it("registers an ENV-selected current directory in place without creating new data", async () => {
    api.getDesktopProfiles.mockResolvedValue({
      data: { ...catalog, environmentOverride: true, currentDirectory: "/env-library" },
    });
    render(<ProfilesSettings />);
    fireEvent.click(await screen.findByRole("button", { name: "profiles.registerCurrent" }));
    expect(screen.getByLabelText("profiles.directory")).toHaveValue("/env-library");
    fireEvent.change(screen.getByLabelText("profiles.name"), { target: { value: "Work" } });
    fireEvent.click(screen.getByRole("button", { name: "profiles.save" }));
    await waitFor(() =>
      expect(api.addDesktopProfile).toHaveBeenCalledWith({
        name: "Work",
        directory: "/env-library",
        create: false,
      }),
    );
  });
  it("requires an explicit name and path for a fresh space and preserves failed input", async () => {
    api.addDesktopProfile.mockResolvedValue({ code: 400, message: "Directory is not empty" });
    render(<ProfilesSettings />);
    fireEvent.click(await screen.findByRole("button", { name: "profiles.addNew" }));
    expect(screen.getByRole("button", { name: "profiles.save" })).toBeDisabled();
    fireEvent.change(screen.getByLabelText("profiles.name"), { target: { value: "Sandbox" } });
    fireEvent.change(screen.getByLabelText("profiles.directory"), { target: { value: "/new" } });
    fireEvent.click(screen.getByRole("button", { name: "profiles.save" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Directory is not empty");
    expect(screen.getByLabelText("profiles.directory")).toHaveValue("/new");
    expect(api.addDesktopProfile).toHaveBeenCalledWith({
      name: "Sandbox",
      directory: "/new",
      create: true,
    });
  });
  it("removal is confirmed, never offered for the running current space, and only removes registration", async () => {
    render(<ProfilesSettings />);
    fireEvent.click(await screen.findByRole("button", { name: "profiles.remove" }));
    expect(api.removeDesktopProfile).not.toHaveBeenCalled();
    const confirmation = screen.getByRole("alert");

    fireEvent.click(within(confirmation).getByRole("button", { name: "profiles.remove" }));
    await waitFor(() => expect(api.removeDesktopProfile).toHaveBeenCalledWith("other"));
  });
  it("does not offer to open unavailable storage as an empty data space", async () => {
    api.getDesktopProfiles.mockResolvedValue({
      data: { ...catalog, profiles: [current, { ...other, isAvailable: false }] },
    });
    render(<ProfilesSettings />);
    expect(await screen.findByRole("button", { name: "profiles.open" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "profiles.switch" })).toBeDisabled();
  });
});
