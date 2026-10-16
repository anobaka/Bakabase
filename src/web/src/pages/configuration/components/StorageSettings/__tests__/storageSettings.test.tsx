import type { ReactNode } from "react";

import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import StorageSettings from "..";
import { SettingsSearchResults } from "../../SettingsSection";

import { ClientMode, DataPathSource } from "@/sdk/constants";

const mocks = vi.hoisted(() => ({
  context: { context: "known", clientMode: 0, isLocal: true, serverName: "NAS" },
  getProfiles: vi.fn(),
  openProfile: vi.fn(),
  importData: vi.fn(),
  relocate: vi.fn(),
}));

vi.mock("@/stores/remoteAccess", () => ({
  useRemoteAccessStore: (selector: any) => selector(mocks.context),
  useIsPureClient: () => mocks.context.clientMode === ClientMode.PureClient,
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    app: {
      getDesktopProfiles: mocks.getProfiles,
      openDesktopProfile: mocks.openProfile,
    },
  },
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));
vi.mock("@/components/FileSystemSelector", () => ({ FileSystemSelectorModal: () => null }));
vi.mock("../../AppInfo/IdentityRecoveryLink", () => ({
  default: () => <span>Identity recovery</span>,
}));
vi.mock("../../AppInfo/Import", () => ({
  default: () => <button onClick={mocks.importData}>Import data</button>,
}));
vi.mock("../../AppInfo/Relocation", () => ({
  RelocationButton: () => <button onClick={mocks.relocate}>Move data</button>,
  RelocationRestartGate: () => <span>Pending relocation gate</span>,
}));
vi.mock("../../AppInfo/LegacyNotice", () => ({
  LegacyAppDataNoticeBanner: () => <span>Legacy notice</span>,
}));
vi.mock("@/components/FilePathValue", () => ({
  default: ({ path, description }: any) => <div title={description}>{path}</div>,
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, isDisabled, type, ...props }: any) => (
    <button
      aria-label={props["aria-label"]}
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
      disabled={isDisabled}
      value={(Array.from(selectedKeys)[0] as string) || ""}
      onChange={(event) => onSelectionChange(new Set([event.target.value]))}
    >
      {dataSource.map((item: any) => (
        <option key={item.value} value={item.value}>
          {item.label}
        </option>
      ))}
    </select>
  ),
  Dropdown: ({ children }: any) => <>{children}</>,
  DropdownTrigger: ({ children }: any) => <>{children}</>,
  DropdownMenu: () => null,
  DropdownItem: () => null,
  Input: () => null,
  Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
  Divider: () => <hr />,
  Tooltip: ({ children }: { children: ReactNode }) => <>{children}</>,
  Accordion: ({ children, selectedKeys, onSelectionChange }: any) => {
    const expanded = selectedKeys.has("storage");

    return (
      <div>
        <button
          aria-expanded={expanded}
          onClick={() => onSelectionChange(new Set(expanded ? [] : ["storage"]))}
        >
          {children.props.title}
        </button>
        {expanded && children}
      </div>
    );
  },
  AccordionItem: ({ children }: { children: ReactNode }) => <div>{children}</div>,
}));
const main = {
  id: "main",
  name: "Main library",
  dataDirectory: "/library",
  isCurrent: true,
  isDefault: true,
  isRunning: true,
  isAvailable: true,
};
const other = {
  id: "other",
  name: "Sandbox",
  dataDirectory: "/sandbox",
  isCurrent: false,
  isDefault: false,
  isRunning: false,
  isAvailable: true,
};
const catalog = { available: true, profiles: [main, other] };
const appInfo = {
  appDataPath: "/library",
  dataPath: "/library/database",
  logPath: "/library/logs",
  tempFilesPath: "/library/tmp",
  backupPath: "/library/backups",
  anchorPath: "/anchor",
  dataPathSource: DataPathSource.UserConfigured,
};
const fixture = (query = "") => (
  <SettingsSearchResults>
    {(matched) => (
      <>
        <StorageSettings appInfo={appInfo} query={query} />
        {!matched && <p>No settings matched</p>}
      </>
    )}
  </SettingsSearchResults>
);

beforeEach(() => {
  vi.clearAllMocks();
  Object.assign(mocks.context, {
    context: "known",
    clientMode: ClientMode.AllInOne,
    isLocal: true,
    serverName: "NAS",
  });
  mocks.getProfiles.mockResolvedValue({ data: catalog });
  mocks.openProfile.mockResolvedValue({ code: 0 });
});
afterEach(cleanup);

describe("workspace and server storage settings", () => {
  it("combines compact workspace selection with current storage while details remain collapsed", async () => {
    render(fixture());
    expect(await screen.findByRole("combobox", { name: "profiles.select" })).toHaveValue("main");
    expect(mocks.getProfiles).toHaveBeenCalledOnce();
    expect(screen.getByText("profiles.currentDirectory")).toBeVisible();
    expect(screen.getByRole("button", { name: "configuration.storage.details" })).toHaveAttribute(
      "aria-expanded",
      "false",
    );
    expect(screen.queryByText("/library/logs")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Import data" })).toBeVisible();
    expect(screen.getByRole("button", { name: "Move data" })).toBeVisible();
    const management = screen.getByRole("region", { name: "profiles.manageSection" });

    expect(within(management).getByRole("button", { name: "profiles.rename" })).toBeVisible();
    expect(within(management).getByRole("button", { name: "profiles.switch" })).toBeDisabled();
    expect(screen.queryByRole("button", { name: "profiles.open" })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "configuration.storage.details" }));
    for (const path of [
      "/anchor",
      "/library/database",
      "/library/tmp",
      "/library/logs",
      "/library/backups",
    ])
      expect(screen.getByText(path)).toBeVisible();
    expect(screen.getByText("Pending relocation gate")).toBeVisible();
    expect(screen.getByText("Legacy notice")).toBeVisible();
    fireEvent.change(screen.getByRole("combobox"), { target: { value: "other" } });
    expect(mocks.openProfile).not.toHaveBeenCalled();
    expect(screen.getByText("/library")).toBeVisible();
    expect(screen.getByText(/profiles.targetDirectory/)).toHaveTextContent("/sandbox");
    fireEvent.click(within(management).getByRole("button", { name: "profiles.switch" }));
    await waitFor(() =>
      expect(mocks.openProfile).toHaveBeenCalledWith("other", { closeCurrent: true }),
    );
  });

  it.each([ClientMode.RemoteBrowser, ClientMode.PureClient])(
    "keeps storage in a server context (%s) without querying or exposing desktop workspaces",
    async (mode) => {
      Object.assign(mocks.context, {
        clientMode: mode,
        isLocal: mode === ClientMode.RemoteBrowser,
      });
      render(fixture());
      await act(async () => {});
      expect(mocks.getProfiles).not.toHaveBeenCalled();
      expect(
        screen.getByRole("region", {
          name:
            mode === ClientMode.PureClient
              ? "configuration.storage.title.server"
              : "configuration.storage.title",
        }),
      ).toBeVisible();
      expect(screen.getByText("/library")).toBeVisible();
      expect(screen.getByRole("button", { name: "Import data" })).toBeVisible();
      expect(screen.queryByRole("combobox", { name: "profiles.select" })).not.toBeInTheDocument();
      if (mode === ClientMode.PureClient)
        expect(screen.getByText(/configuration.storage.serverDescription/)).toHaveTextContent(
          "NAS",
        );
    },
  );

  it("keeps current storage readable even when the profile registry fails", async () => {
    mocks.getProfiles.mockRejectedValue(new Error("Registry unavailable"));
    render(fixture());
    expect(await screen.findByRole("alert")).toHaveTextContent("Registry unavailable");
    expect(screen.getByText("/library")).toBeVisible();
    expect(screen.getByRole("button", { name: "Move data" })).toBeVisible();
  });

  it("searches collapsed detail rows, expands matching rows and participates in no-results reporting", async () => {
    const { rerender } = render(fixture("log"));

    expect(screen.getByText("/library/logs")).toBeVisible();
    expect(screen.queryByText("/library/database")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "configuration.storage.details" })).toHaveAttribute(
      "aria-expanded",
      "true",
    );
    await waitFor(() => expect(screen.queryByText("No settings matched")).not.toBeInTheDocument());
    rerender(fixture("missing setting xyz"));
    expect(await screen.findByText("No settings matched")).toBeVisible();
    expect(screen.queryByRole("region")).not.toBeInTheDocument();
    rerender(fixture());
    expect(screen.getByRole("button", { name: "configuration.storage.details" })).toHaveAttribute(
      "aria-expanded",
      "false",
    );
  });

  it("finds the storage details heading and reveals all detail rows", async () => {
    render(fixture("configuration.storage.details"));
    expect(screen.getByText("/anchor")).toBeVisible();
    expect(screen.getByText("/library/logs")).toBeVisible();
    expect(screen.getByText("/library/database")).toBeVisible();
    expect(screen.queryByRole("button", { name: "Import data" })).not.toBeInTheDocument();
    await act(async () => {});
  });

  it("does not request or expose workspaces before the current host context is known", async () => {
    mocks.context.context = "asking";
    const { rerender } = render(fixture());

    expect(mocks.getProfiles).not.toHaveBeenCalled();
    expect(screen.getByText("/library")).toBeVisible();
    expect(screen.queryByRole("combobox", { name: "profiles.select" })).not.toBeInTheDocument();
    mocks.context.context = "known";
    rerender(fixture());
    expect(await screen.findByRole("combobox", { name: "profiles.select" })).toBeVisible();
    expect(mocks.getProfiles).toHaveBeenCalledOnce();
  });

  it("finds workspace management without unrelated storage rows", async () => {
    render(fixture("profiles.managesection"));
    expect(await screen.findByRole("combobox", { name: "profiles.select" })).toBeVisible();
    expect(screen.queryByRole("button", { name: "Import data" })).not.toBeInTheDocument();
    expect(screen.queryByText("No settings matched")).not.toBeInTheDocument();
  });

  it.each([false, true])(
    "preserves environment relocation restrictions and system-path warnings (system=%s)",
    async (dataInSystemPath) => {
      render(
        <StorageSettings
          appInfo={{ ...appInfo, dataPathSource: DataPathSource.Environment, dataInSystemPath }}
        />,
      );
      expect(screen.queryByRole("button", { name: "Move data" })).not.toBeInTheDocument();
      if (dataInSystemPath)
        expect(
          screen.getByText("configuration.appInfo.tip.appDataPath.systemPathRiskNotice"),
        ).toBeVisible();
      await act(async () => {});
    },
  );

  it.each([false, true])(
    "preserves user-configured relocation and path warnings (system=%s)",
    async (dataInSystemPath) => {
      render(
        <StorageSettings appInfo={{ ...appInfo, dataInSystemPath, dataInInstallRoot: true }} />,
      );
      expect(screen.getByRole("button", { name: "Move data" })).toBeVisible();
      expect(
        screen.getByText("configuration.appInfo.tip.appDataPath.installRootRiskNotice"),
      ).toBeVisible();
      await act(async () => {});
    },
  );
});
