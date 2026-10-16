import type * as SettingsSectionModule from "@/pages/configuration/components/SettingsSection";

import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
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
vi.mock("@/pages/configuration/components/SettingsSection", async (importOriginal) => ({
  ...(await importOriginal<typeof SettingsSectionModule>()),
  useSettingsSearchMatch: reportMatch,
}));
vi.mock("@/sdk/BApi", () => ({ default: { app: api } }));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));
vi.mock("@/components/FileSystemSelector", () => ({ FileSystemSelectorModal: () => null }));
vi.mock("@/components/bakaui", async () => {
  const React = await import("react");
  const MenuContext = React.createContext<any>({});

  return {
    Button: ({ children, onPress, isDisabled, type, ...props }: any) => (
      <button
        aria-label={props["aria-label"]}
        data-loading={props.isLoading}
        disabled={isDisabled}
        type={type || "button"}
        onClick={onPress}
      >
        {children}
      </button>
    ),
    Select: ({ dataSource, selectedKeys, onSelectionChange, isDisabled, ...props }: any) => (
      <select
        aria-label={props["aria-label"]}
        data-loading={props.isLoading}
        disabled={isDisabled}
        value={(Array.from(selectedKeys)[0] as string) || ""}
        onChange={(event) => onSelectionChange(new Set([event.target.value]))}
      >
        {!selectedKeys.length && <option value="" />}
        {dataSource.map((item: any) => (
          <option key={item.value} value={item.value}>
            {item.label}
          </option>
        ))}
      </select>
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
    Dropdown: ({ children }: any) => {
      const [open, setOpen] = React.useState(false);

      return <MenuContext.Provider value={{ open, setOpen }}>{children}</MenuContext.Provider>;
    },
    DropdownTrigger: ({ children }: any) => {
      const { open, setOpen } = React.useContext(MenuContext);

      return React.cloneElement(children, { onPress: () => setOpen(!open) });
    },
    DropdownMenu: ({ children, disabledKeys, onAction, ...props }: any) => {
      const { open, setOpen } = React.useContext(MenuContext);

      return open ? (
        <div aria-label={props["aria-label"]} role="menu">
          {React.Children.map(children, (child: any) =>
            React.cloneElement(child, {
              isDisabled: disabledKeys.includes(child.key),
              onPress: () => {
                onAction(child.key);
                setOpen(false);
              },
            }),
          )}
        </div>
      ) : null;
    },
    DropdownItem: ({ children, isDisabled, onPress }: any) => (
      <button disabled={isDisabled} role="menuitem" onClick={onPress}>
        {children}
      </button>
    ),
  };
});
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

const selectWorkspace = async (id: string) =>
  fireEvent.change(await screen.findByRole("combobox", { name: "profiles.select" }), {
    target: { value: id },
  });
const openManagement = async () =>
  fireEvent.click(await screen.findByRole("button", { name: "profiles.manageSection" }));

describe("desktop workspace switching", () => {
  it("surfaces an unreadable registry in the local desktop instead of silently hiding it", async () => {
    api.getDesktopProfiles.mockRejectedValue(new Error("Registry is unreadable"));
    render(<ProfilesSettings />);
    expect(await screen.findByRole("alert")).toHaveTextContent("Registry is unreadable");
  });
  it("reports matched sections to the existing configuration search and hides unmatched searches", async () => {
    const { rerender } = render(<ProfilesSettings query="profiles" />);

    await screen.findByRole("combobox");
    expect(reportMatch).toHaveBeenLastCalledWith("desktop-profiles", true);
    rerender(<ProfilesSettings query="not a matching section" />);
    expect(screen.queryByRole("combobox")).not.toBeInTheDocument();
    expect(reportMatch).toHaveBeenLastCalledWith("desktop-profiles", false);
  });
  it("is absent when workspace management is unavailable", async () => {
    api.getDesktopProfiles.mockResolvedValue({ data: { available: false, profiles: [] } });
    const { container } = render(<ProfilesSettings />);

    await waitFor(() => expect(api.getDesktopProfiles).toHaveBeenCalledOnce());
    expect(container).toBeEmptyDOMElement();
  });
  it("selects the current workspace initially and only switches after explicit confirmation", async () => {
    render(<ProfilesSettings />);
    expect(await screen.findByRole("combobox")).toHaveValue("default");
    expect(screen.getByRole("button", { name: "profiles.switch" })).toBeDisabled();
    expect(screen.queryByRole("button", { name: "profiles.open" })).not.toBeInTheDocument();
    expect(screen.queryByText("profiles.running")).not.toBeInTheDocument();
    expect(screen.queryByRole("menuitem", { name: "profiles.addNew" })).not.toBeInTheDocument();
    await selectWorkspace("other");
    expect(api.openDesktopProfile).not.toHaveBeenCalled();
    expect(screen.getByText(/profiles.targetDirectory/)).toHaveTextContent("/test");
    fireEvent.click(screen.getByRole("button", { name: "profiles.switch" }));
    await waitFor(() =>
      expect(api.openDesktopProfile).toHaveBeenCalledWith("other", { closeCurrent: true }),
    );
    expect(await screen.findByRole("status")).toHaveTextContent("profiles.switching");
    expect(screen.getByRole("button", { name: "profiles.switch" })).toBeDisabled();
    expect(api.getDesktopProfiles).toHaveBeenCalledOnce();
  });
  it("shows switching progress while the request is pending and prevents another switch", async () => {
    let resolve!: (value: { code: number }) => void;

    api.openDesktopProfile.mockReturnValue(
      new Promise((done) => {
        resolve = done;
      }),
    );
    render(<ProfilesSettings />);
    await selectWorkspace("other");
    fireEvent.click(screen.getByRole("button", { name: "profiles.switch" }));
    expect(screen.getByRole("button", { name: "profiles.switch" })).toHaveAttribute(
      "data-loading",
      "true",
    );
    expect(screen.getByRole("combobox")).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "profiles.switch" }));
    expect(api.openDesktopProfile).toHaveBeenCalledOnce();
    await act(async () => resolve({ code: 0 }));
    expect(screen.getByRole("button", { name: "profiles.switch" })).toHaveAttribute(
      "data-loading",
      "true",
    );
  });

  it("keeps the selected workspace and permits retry after a failed switch", async () => {
    api.openDesktopProfile.mockResolvedValue({ code: 409, message: "Target unavailable" });
    render(<ProfilesSettings />);
    await selectWorkspace("other");
    fireEvent.click(screen.getByRole("button", { name: "profiles.switch" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Target unavailable");
    expect(screen.getByRole("combobox")).toHaveValue("other");
    expect(screen.getByRole("button", { name: "profiles.switch" })).not.toBeDisabled();
  });
  it("renames the selected workspace without switching or changing its default status", async () => {
    render(<ProfilesSettings />);
    await selectWorkspace("other");
    fireEvent.click(screen.getByRole("button", { name: "profiles.rename" }));
    expect(screen.getByLabelText("profiles.name")).toHaveValue("Test space");
    fireEvent.change(screen.getByLabelText("profiles.name"), { target: { value: "Renamed" } });
    fireEvent.click(screen.getByRole("button", { name: "profiles.save" }));
    await waitFor(() =>
      expect(api.updateDesktopProfile).toHaveBeenCalledWith("other", {
        name: "Renamed",
        makeDefault: false,
      }),
    );
    expect(api.openDesktopProfile).not.toHaveBeenCalled();
  });
  it("keeps changing the default inside the management menu and applies it to the selected workspace", async () => {
    render(<ProfilesSettings />);
    await openManagement();
    expect(screen.getByRole("menuitem", { name: "profiles.makeDefault" })).toBeDisabled();
    expect(screen.getByRole("menuitem", { name: "profiles.remove" })).toBeDisabled();
    await selectWorkspace("other");
    fireEvent.click(screen.getByRole("menuitem", { name: "profiles.makeDefault" }));
    await waitFor(() =>
      expect(api.updateDesktopProfile).toHaveBeenCalledWith("other", {
        name: "Test space",
        makeDefault: true,
      }),
    );
    expect(api.openDesktopProfile).not.toHaveBeenCalled();
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
    expect(api.openDesktopProfile).not.toHaveBeenCalled();
  });
  it("requires an explicit name and path for a new workspace and preserves failed input", async () => {
    api.addDesktopProfile.mockResolvedValue({ code: 400, message: "Directory is not empty" });
    render(<ProfilesSettings />);
    await openManagement();
    fireEvent.click(screen.getByRole("menuitem", { name: "profiles.addNew" }));
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
  it("adds an existing directory without opening or replacing the current workspace", async () => {
    render(<ProfilesSettings />);
    await openManagement();
    fireEvent.click(screen.getByRole("menuitem", { name: "profiles.addExisting" }));
    fireEvent.change(screen.getByLabelText("profiles.name"), { target: { value: "Existing" } });
    fireEvent.change(screen.getByLabelText("profiles.directory"), {
      target: { value: "/existing" },
    });
    fireEvent.click(screen.getByRole("button", { name: "profiles.save" }));
    await waitFor(() =>
      expect(api.addDesktopProfile).toHaveBeenCalledWith({
        name: "Existing",
        directory: "/existing",
        create: false,
      }),
    );
    expect(api.openDesktopProfile).not.toHaveBeenCalled();
  });
  it("requires confirmation before removing another registration and never deletes data", async () => {
    render(<ProfilesSettings />);
    await selectWorkspace("other");
    await openManagement();
    fireEvent.click(screen.getByRole("menuitem", { name: "profiles.remove" }));
    expect(api.removeDesktopProfile).not.toHaveBeenCalled();
    fireEvent.click(
      within(screen.getByRole("alert")).getByRole("button", { name: "profiles.remove" }),
    );
    await waitFor(() => expect(api.removeDesktopProfile).toHaveBeenCalledWith("other"));
  });
  it("allows inspecting an unavailable workspace but refuses to switch into it", async () => {
    api.getDesktopProfiles.mockResolvedValue({
      data: { ...catalog, profiles: [current, { ...other, isAvailable: false }] },
    });
    render(<ProfilesSettings />);
    await selectWorkspace("other");
    expect(screen.getByRole("button", { name: "profiles.rename" })).not.toBeDisabled();
    expect(screen.getByRole("button", { name: "profiles.switch" })).toBeDisabled();
    expect(api.openDesktopProfile).not.toHaveBeenCalled();
  });
});
