import type { ReactNode } from "react";
import type { BakabaseInfrastructuresComponentsAppUpgradeAbstractionsAppVersionInfo } from "@/sdk/Api";

import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter, useLocation } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import AppInfo from "..";

import { UpdaterStatus } from "@/sdk/constants";
import { useAppUpdaterStateStore } from "@/stores/appUpdaterState";

type VersionInfo = BakabaseInfrastructuresComponentsAppUpgradeAbstractionsAppVersionInfo;
type CheckResponse = { code: number; data?: VersionInfo; message?: string };

const mocks = vi.hoisted(() => ({
  check: vi.fn(),
  download: vi.fn(),
  patch: vi.fn(),
  applyPatches: vi.fn(),
  localRestart: vi.fn(),
  remoteRestart: vi.fn(),
  changelog: vi.fn(),
  context: {
    remote: false,
    pure: false,
    localRestarting: false,
    remoteRestarting: false,
    remoteTimedOut: false,
    enablePreReleaseChannel: false,
  },
}));

vi.mock("@/sdk/BApi", () => ({
  default: {
    updater: { getNewAppVersion: mocks.check, startUpdatingApp: mocks.download },
    options: { patchAppOptions: mocks.patch },
  },
}));
vi.mock("@/stores/options", () => ({
  useAppOptionsStore: (selector: (state: unknown) => unknown) =>
    selector({ data: { enablePreReleaseChannel: mocks.context.enablePreReleaseChannel } }),
}));
vi.mock("@/stores/remoteAccess", () => ({
  useIsPureClient: () => mocks.context.pure,
  useIsRemoteClient: () => mocks.context.remote,
}));
vi.mock("@/components/UpdateRestart", () => ({
  useUpdateRestart: () => ({
    restarting: mocks.context.localRestarting,
    restart: mocks.localRestart,
  }),
  useRemoteServerUpdateRestart: () => ({
    restarting: mocks.context.remoteRestarting,
    timedOut: mocks.context.remoteTimedOut,
    restart: mocks.remoteRestart,
  }),
}));
vi.mock("../IdentityRecoveryLink", () => ({ default: () => null }));
vi.mock("../Relocation", () => ({
  RelocationButton: () => null,
  RelocationRestartGate: () => null,
}));
vi.mock("../LegacyNotice", () => ({ LegacyAppDataNoticeBanner: () => null }));
vi.mock("@/components/FilePathValue", () => ({ default: () => null }));
vi.mock("@/components/ExternalLink", () => ({
  default: ({ children, href }: { children: ReactNode; href: string }) => (
    <a href={href}>{children}</a>
  ),
}));
vi.mock("@/components/Changelog", () => ({
  ChangelogButton: ({ version, from }: { version?: string; from?: string }) => (
    <button aria-label={`notes ${version}`} onClick={() => mocks.changelog({ version, from })}>
      notes
    </button>
  ),
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({
    children,
    onPress,
    isDisabled,
    isLoading,
    variant,
    "aria-label": label,
  }: {
    children: ReactNode;
    onPress?: () => void;
    isDisabled?: boolean;
    isLoading?: boolean;
    variant?: string;
    "aria-label"?: string;
  }) => (
    <button
      aria-label={label}
      data-loading={isLoading}
      data-variant={variant}
      disabled={isDisabled}
      onClick={onPress}
    >
      {children}
    </button>
  ),
  Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
  Snippet: ({ children }: { children: ReactNode }) => <span>{children}</span>,
  Divider: () => <hr />,
  Spinner: () => <span aria-hidden>spinner</span>,
  Tooltip: ({ children, content }: { children: ReactNode; content: string }) => (
    <span title={content}>{children}</span>
  ),
  Popover: ({ trigger, children }: { trigger: ReactNode; children: ReactNode }) => (
    <div>
      {trigger}
      {children}
    </div>
  ),
  Progress: ({
    value,
    isIndeterminate,
    "aria-label": label,
  }: {
    value?: number;
    isIndeterminate?: boolean;
    "aria-label": string;
  }) => (
    <progress aria-label={label} data-indeterminate={isIndeterminate} max={100} value={value} />
  ),
  Switch: ({
    children,
    isSelected,
    onValueChange,
    "aria-label": label,
  }: {
    children: ReactNode;
    isSelected?: boolean;
    onValueChange: (checked: boolean) => void;
    "aria-label": string;
  }) => (
    <label>
      <input
        aria-label={label}
        checked={isSelected}
        role="switch"
        type="checkbox"
        onChange={(event) => onValueChange(event.target.checked)}
      />
      {children}
    </label>
  ),
}));

const key = (suffix: string) => `configuration.appInfo.${suffix}`;
const checkButton = () => screen.getByRole("button", { name: key("checkForUpdates") });
const versionInfo = (overrides: Partial<VersionInfo> = {}): VersionInfo => ({
  version: "2.4.1",
  runningVersion: "2.4.0",
  installedVersion: "2.4.0",
  installers: [],
  channelBehindInstalled: false,
  updateCheckUnavailable: false,
  ...overrides,
});
const deferred = <T,>() => {
  let resolve!: (value: T) => void;
  let reject!: (reason: Error) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });

  return { promise, resolve, reject };
};
const Location = () => <output aria-label="route">{useLocation().pathname}</output>;
const fixture = (query = "版本") => (
  <MemoryRouter>
    <AppInfo appInfo={{ coreVersion: "2.4.0" }} applyPatches={mocks.applyPatches} query={query} />
    <Location />
  </MemoryRouter>
);

beforeEach(() => {
  vi.clearAllMocks();
  mocks.check.mockResolvedValue({ code: 0, data: versionInfo() });
  mocks.download.mockResolvedValue({ code: 0 });
  mocks.applyPatches.mockImplementation((_api, _patches, success) => success?.({ code: 0 }));
  Object.assign(mocks.context, {
    remote: false,
    pure: false,
    localRestarting: false,
    remoteRestarting: false,
    remoteTimedOut: false,
    enablePreReleaseChannel: false,
  });
  useAppUpdaterStateStore.setState({ status: undefined, percentage: undefined, error: undefined });
});
afterEach(cleanup);

describe("configuration app versions", () => {
  it("shows current and target versions with one explicit update action and secondary tools", async () => {
    mocks.check.mockResolvedValue({
      code: 0,
      data: versionInfo({
        installers: [
          { url: "https://example.com/installer", name: "Installer", osArchitecture: 1 },
        ],
      }),
    });
    render(fixture());

    expect(await screen.findByText(key("updateAvailable"))).toBeVisible();
    expect(screen.getByText(key("coreVersion"))).toBeVisible();
    expect(screen.getByText(key("latestVersion"))).toBeVisible();
    expect(screen.getByText("2.4.0")).toBeVisible();
    expect(screen.getByText("2.4.1")).toBeVisible();
    const download = screen.getByRole("button", { name: key("clickToAutoUpdate") });

    expect(download).toHaveAttribute("data-variant", "solid");
    expect(screen.getByRole("button", { name: key("autoUpdateFails") })).toBeVisible();
    expect(screen.getByRole("link", { name: "Installer" })).toHaveAttribute(
      "href",
      "https://example.com/installer",
    );
    expect(screen.getByRole("switch", { name: key("preReleaseChannel") })).not.toBeChecked();
    expect(mocks.download).not.toHaveBeenCalled();
    fireEvent.click(download);
    expect(mocks.download).toHaveBeenCalledOnce();
    fireEvent.click(screen.getByRole("button", { name: key("viewAllChangelogs") }));
    expect(screen.getByLabelText("route")).toHaveTextContent("/changelog");
  });

  it("checks explicitly, disables repeated checks and never claims up to date before a result", async () => {
    const initial = deferred<CheckResponse>();

    mocks.check.mockReturnValueOnce(initial.promise);
    render(fixture());
    expect(checkButton()).toBeDisabled();
    expect(checkButton()).toHaveAttribute("data-loading", "true");
    expect(screen.queryByText(key("upToDate"))).not.toBeInTheDocument();
    fireEvent.click(checkButton());
    expect(mocks.check).toHaveBeenCalledOnce();
    await act(async () => initial.resolve({ code: 0, data: versionInfo({ version: undefined }) }));
    expect(screen.getByText(key("upToDate"))).toBeVisible();
    const next = deferred<CheckResponse>();

    mocks.check.mockReturnValueOnce(next.promise);
    fireEvent.click(checkButton());
    fireEvent.click(checkButton());
    expect(mocks.check).toHaveBeenCalledTimes(2);
    expect(checkButton()).toBeDisabled();
    await act(async () => next.resolve({ code: 0, data: versionInfo() }));
    expect(screen.getByText(key("updateAvailable"))).toBeVisible();
  });

  it.each(["reject", "code", "missing data"])(
    "offers a check retry after %s without a false green status",
    async (failure) => {
      if (failure === "reject") mocks.check.mockRejectedValueOnce(new Error("offline"));
      else
        mocks.check.mockResolvedValueOnce(
          failure === "code" ? { code: 1, message: "offline" } : { code: 0 },
        );
      useAppUpdaterStateStore.setState({ status: UpdaterStatus.UpToDate });
      render(fixture());
      await waitFor(() => expect(checkButton()).not.toBeDisabled());
      expect(screen.getByText(key("failedToGetLatestVersion"))).toBeVisible();
      expect(screen.queryByText(key("upToDate"))).not.toBeInTheDocument();
      expect(checkButton()).toHaveTextContent(key("clickToRetry"));
      fireEvent.click(checkButton());
      expect(await screen.findByText(key("updateAvailable"))).toBeVisible();
      expect(mocks.check).toHaveBeenCalledTimes(2);
    },
  );

  it("uses a concrete new version despite an old up-to-date singleton status", async () => {
    useAppUpdaterStateStore.setState({ status: UpdaterStatus.UpToDate });
    render(fixture());
    expect(await screen.findByText(key("updateAvailable"))).toBeVisible();
    expect(screen.queryByText(key("upToDate"))).not.toBeInTheDocument();
  });

  it("retries checking when the backend publishes Failed and returns no version after a check error", async () => {
    mocks.check.mockImplementationOnce(async () => {
      useAppUpdaterStateStore.setState({ status: UpdaterStatus.Failed, error: "offline" });

      return { code: 0 };
    });
    render(fixture());
    expect(await screen.findByText(key("failedToGetLatestVersion"))).toBeVisible();
    expect(screen.queryByText(key("failedToUpdateApp"))).not.toBeInTheDocument();
    expect(screen.queryByText(key("upToDate"))).not.toBeInTheDocument();
    expect(checkButton()).toHaveTextContent(key("clickToRetry"));
    fireEvent.click(checkButton());
    await waitFor(() => expect(mocks.check).toHaveBeenCalledTimes(2));
    expect(mocks.download).not.toHaveBeenCalled();
  });

  it("keeps a download retry after a successful recheck but offers a check retry when that check fails", async () => {
    useAppUpdaterStateStore.setState({ status: UpdaterStatus.Failed, error: "download failed" });
    render(fixture());
    expect(await screen.findByText(key("failedToUpdateApp"))).toBeVisible();
    fireEvent.click(screen.getByRole("button", { name: key("clickToRetry") }));
    expect(mocks.download).toHaveBeenCalledOnce();
    const next = deferred<CheckResponse>();

    mocks.check.mockReturnValueOnce(next.promise);
    fireEvent.click(checkButton());
    expect(screen.queryByText(key("failedToUpdateApp"))).not.toBeInTheDocument();
    await act(async () => next.reject(new Error("check failed")));
    expect(screen.getByText(key("failedToGetLatestVersion"))).toBeVisible();
    expect(screen.getByRole("alert")).toHaveTextContent("check failed");
    fireEvent.click(checkButton());
    expect(await screen.findByText(key("failedToUpdateApp"))).toBeVisible();
    expect(mocks.download).toHaveBeenCalledOnce();
    fireEvent.click(screen.getByRole("button", { name: key("clickToRetry") }));
    expect(mocks.download).toHaveBeenCalledTimes(2);
  });

  it.each([undefined, UpdaterStatus.UpToDate, UpdaterStatus.Unavailable])(
    "honors unavailable installation metadata with singleton status %s",
    async (status) => {
      useAppUpdaterStateStore.setState({ status });
      mocks.check.mockResolvedValueOnce({
        code: 0,
        data: versionInfo({ version: undefined, updateCheckUnavailable: true }),
      });
      render(fixture());
      expect(await screen.findByText(key("updateCheckUnavailable"))).toBeVisible();
      expect(screen.queryByText(key("upToDate"))).not.toBeInTheDocument();
    },
  );

  it("keeps channel and installed-version warnings and the changelog's installed lower bound", async () => {
    mocks.check.mockResolvedValueOnce({
      code: 0,
      data: versionInfo({ runningVersion: "2.4.0-dev", installedVersion: "2.3.9" }),
    });
    render(fixture());
    expect(await screen.findByText(key("runningVersionMismatch"))).toBeVisible();
    fireEvent.click(screen.getByRole("button", { name: "notes 2.4.1" }));
    expect(mocks.changelog).toHaveBeenCalledWith({ version: "2.4.1", from: "2.3.9" });
    fireEvent.click(screen.getByRole("button", { name: "notes 2.4.0" }));
    expect(mocks.changelog).toHaveBeenCalledWith({ version: "2.4.0", from: undefined });
    mocks.check.mockResolvedValueOnce({
      code: 0,
      data: versionInfo({ version: undefined, channelBehindInstalled: true }),
    });
    fireEvent.click(checkButton());
    expect(await screen.findByText(key("channelBehind"))).toBeVisible();
    expect(screen.queryByText(key("upToDate"))).not.toBeInTheDocument();
  });

  it("falls back to the running version for update notes when the installed lower bound is absent", async () => {
    mocks.check.mockResolvedValueOnce({
      code: 0,
      data: versionInfo({ installedVersion: undefined }),
    });
    render(fixture());
    await screen.findByText(key("updateAvailable"));
    fireEvent.click(screen.getByRole("button", { name: "notes 2.4.1" }));
    expect(mocks.changelog).toHaveBeenCalledWith({ version: "2.4.1", from: "2.4.0" });
  });

  it.each([UpdaterStatus.Running, UpdaterStatus.PendingRestart])(
    "preserves active updater state %s throughout a new check",
    async (status) => {
      useAppUpdaterStateStore.setState({ status, percentage: 37, error: "download failed" });
      render(fixture());
      await waitFor(() => expect(checkButton()).not.toBeDisabled());
      const next = deferred<CheckResponse>();

      mocks.check.mockReturnValueOnce(next.promise);
      fireEvent.click(checkButton());
      expect(checkButton()).toBeDisabled();
      if (status === UpdaterStatus.Running) {
        expect(screen.getByRole("progressbar")).toHaveAttribute("value", "37");
        expect(screen.getByText(key("downloading"))).toBeVisible();
      } else if (status === UpdaterStatus.PendingRestart) {
        expect(screen.getByRole("button", { name: key("restartToUpdate") })).not.toBeDisabled();
        expect(screen.getByText(key("readyToRestart"))).toBeVisible();
      }
      await act(async () => next.reject(new Error("check failed")));
      expect(screen.queryByText(key("upToDate"))).not.toBeInTheDocument();
    },
  );

  it.each([false, true])("uses only the correct restart callback for remote=%s", async (remote) => {
    mocks.context.remote = remote;
    useAppUpdaterStateStore.setState({ status: UpdaterStatus.PendingRestart });
    render(fixture());
    await waitFor(() => expect(checkButton()).not.toBeDisabled());
    fireEvent.click(screen.getByRole("button", { name: key("restartToUpdate") }));
    expect(remote ? mocks.remoteRestart : mocks.localRestart).toHaveBeenCalledOnce();
    expect(remote ? mocks.localRestart : mocks.remoteRestart).not.toHaveBeenCalled();
  });

  it("shows remote restart progress, blocks repeated restart and preserves timeout guidance", async () => {
    mocks.context.remote = true;
    mocks.context.remoteRestarting = true;
    useAppUpdaterStateStore.setState({ status: UpdaterStatus.PendingRestart });
    const view = render(fixture());

    await screen.findByText("appUpdate.serverRestarting");
    expect(screen.getByRole("button", { name: key("restartToUpdate") })).toBeDisabled();
    mocks.context.remoteRestarting = false;
    mocks.context.remoteTimedOut = true;
    view.rerender(fixture());
    expect(screen.getByText("appUpdate.serverRestartUnconfirmed")).toBeVisible();
    expect(screen.getByRole("button", { name: key("restartToUpdate") })).not.toBeDisabled();
  });

  it("rechecks after saving the channel and ignores an older response for the previous channel", async () => {
    const older = deferred<CheckResponse>();
    const newer = deferred<CheckResponse>();

    mocks.check.mockReturnValueOnce(older.promise).mockReturnValueOnce(newer.promise);
    render(fixture());
    fireEvent.click(screen.getByRole("switch", { name: key("preReleaseChannel") }));
    expect(mocks.applyPatches).toHaveBeenCalledWith(
      mocks.patch,
      { enablePreReleaseChannel: true },
      expect.any(Function),
    );
    expect(mocks.check).toHaveBeenCalledTimes(2);
    await act(async () =>
      newer.resolve({ code: 0, data: versionInfo({ version: "2.5.0-beta.1" }) }),
    );
    expect(screen.getByText("2.5.0-beta.1")).toBeVisible();
    await act(async () => older.resolve({ code: 0, data: versionInfo({ version: "2.4.1" }) }));
    expect(screen.queryByText("2.4.1")).not.toBeInTheDocument();
    expect(screen.getByText("2.5.0-beta.1")).toBeVisible();
    expect(checkButton()).not.toBeDisabled();
  });

  it.each(["latest", "最新", "beta", "测试", "upgrade"])(
    "finds the update tools using %s",
    async (query) => {
      render(fixture(query));
      expect(await screen.findByText(key("updateAvailable"))).toBeVisible();
      expect(screen.getByText(key("latestVersion"))).toBeVisible();
      expect(screen.queryByText(key("coreVersion"))).not.toBeInTheDocument();
    },
  );

  it.each(["core", "核心", "current", "当前"])(
    "keeps the current version searchable using %s",
    async (query) => {
      render(fixture(query));
      expect(screen.getByText("2.4.0")).toBeVisible();
      expect(screen.queryByText(key("latestVersion"))).not.toBeInTheDocument();
      await act(async () => {});
    },
  );
});
