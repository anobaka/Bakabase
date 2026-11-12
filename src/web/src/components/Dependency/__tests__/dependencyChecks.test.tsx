import type { ReactNode } from "react";

import {
  act,
  cleanup,
  fireEvent,
  render,
  renderHook,
  screen,
  waitFor,
} from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import DependencyManager, { useDependencyChecks } from "../DependencyManager";
import { resetDependencyChecks } from "../dependencyChecks";

import { useDependentComponentContextsStore } from "@/stores/dependentComponentContexts";
import { useRemoteAccessStore } from "@/stores/remoteAccess";
import { DependentComponentStatus } from "@/sdk/constants";

const { discover, latest, install, env } = vi.hoisted(() => ({
  discover: vi.fn(),
  latest: vi.fn(),
  install: vi.fn(),
  env: { apiEndpoint: "https://server.example" },
}));

vi.mock("@/config/env", () => ({ default: env }));
vi.mock("@/sdk/BApi", () => ({
  default: {
    component: {
      discoverDependentComponent: discover,
      getDependentComponentLatestVersion: latest,
      installDependentComponent: install,
    },
  },
}));
vi.mock("@/components/Error/Details", () => ({
  default: ({ text }: { text: string }) => <pre>{text}</pre>,
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({
    children,
    onPress,
    isDisabled,
  }: {
    children: ReactNode;
    onPress?: () => void;
    isDisabled?: boolean;
  }) => (
    <button disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
  Spinner: () => <span>loading</span>,
  Progress: () => <div role="progressbar" />,
}));
const response = (version = "6.1", canUpdate = true) => ({ code: 0, data: { version, canUpdate } });
const context = (id = "ffmpeg", status = DependentComponentStatus.Installed) => ({
  id,
  name: id,
  defaultLocation: "/components/" + id,
  status,
  isAvailableOnCurrentPlatform: true,
  isRequired: false,
  installationProgress: 0,
  version: "6.0",
});
type ComponentContext = ReturnType<typeof useDependentComponentContextsStore.getState>["contexts"][number];
const seed = (...contexts: ComponentContext[]) =>
  useDependentComponentContextsStore.getState().setContexts(contexts);
const deferred = <T,>() => {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => {
    resolve = done;
  });

  return { promise, resolve };
};

beforeEach(() => {
  seed();
  resetDependencyChecks();
  env.apiEndpoint = "https://server.example";
  useRemoteAccessStore.setState({ serverId: "server-a" });
  discover.mockReset().mockResolvedValue({ code: 0 });
  latest.mockReset().mockResolvedValue(response());
  install.mockReset().mockResolvedValue({ code: 0 });
});
afterEach(() => {
  cleanup();
  seed();
  resetDependencyChecks();
  vi.restoreAllMocks();
});

describe("shared dependency checks", () => {
  it("deduplicates simultaneous consumers and reuses a successful result after remount", async () => {
    seed(context());
    const pending = deferred<ReturnType<typeof response>>();

    latest.mockReturnValueOnce(pending.promise);
    const first = renderHook(() => useDependencyChecks());
    const second = renderHook(() => useDependencyChecks());
    let one!: Promise<void>, two!: Promise<void>;

    act(() => {
      one = first.result.current.checkDependency("ffmpeg");
      two = second.result.current.checkDependency("ffmpeg");
    });
    expect(latest).toHaveBeenCalledTimes(1);
    expect(first.result.current.entries.ffmpeg.checking).toBe(true);
    expect(second.result.current.entries.ffmpeg.checking).toBe(true);
    await act(async () => {
      pending.resolve(response());
      await Promise.all([one, two]);
    });
    first.unmount();
    second.unmount();
    const reopened = renderHook(() => useDependencyChecks());

    await act(() => reopened.result.current.checkDependency("ffmpeg"));
    expect(latest).toHaveBeenCalledTimes(1);
    expect(reopened.result.current.entries.ffmpeg.latestVersion?.version).toBe("6.1");
  });

  it("expires cached results after five minutes and explicit checks bypass the cache", async () => {
    seed(context());
    let now = 1000;

    vi.spyOn(Date, "now").mockImplementation(() => now);
    const hook = renderHook(() => useDependencyChecks());

    await act(() => hook.result.current.checkDependency("ffmpeg"));
    now += 299999;
    await act(() => hook.result.current.checkDependency("ffmpeg"));
    expect(latest).toHaveBeenCalledTimes(1);
    now += 2;
    await act(() => hook.result.current.checkDependency("ffmpeg"));
    expect(latest).toHaveBeenCalledTimes(2);
    await act(() => hook.result.current.checkDependency("ffmpeg", true, true));
    expect(discover).toHaveBeenCalledTimes(1);
    expect(latest).toHaveBeenCalledTimes(3);
  });

  it("preserves a cached lookup failure across remount and clears it after a successful explicit check", async () => {
    seed(context());
    latest.mockRejectedValueOnce(new Error("lookup unavailable"));
    const first = renderHook(() => useDependencyChecks());

    await act(() => first.result.current.checkDependency("ffmpeg"));
    first.unmount();
    const reopened = renderHook(() => useDependencyChecks());

    await act(() => reopened.result.current.checkDependency("ffmpeg"));
    expect(reopened.result.current.entries.ffmpeg.failure).toMatchObject({ stage: "latest" });
    expect(latest).toHaveBeenCalledTimes(1);
    await act(() => reopened.result.current.checkDependency("ffmpeg", true, true));
    expect(reopened.result.current.entries.ffmpeg.failure).toBeUndefined();
    expect(latest).toHaveBeenCalledTimes(2);
  });

  it("shares installation busy state and failures even after the initiating control unmounts", async () => {
    seed(context());
    const pending = deferred<{ code: number; message?: string }>();

    install.mockReturnValueOnce(pending.promise);
    const first = renderHook(() => useDependencyChecks());

    await act(() => first.result.current.checkDependency("ffmpeg"));
    let installing!: Promise<void>;

    act(() => {
      installing = first.result.current.installDependency("ffmpeg");
    });
    first.unmount();
    const reopened = renderHook(() => useDependencyChecks());

    expect(reopened.result.current.entries.ffmpeg.installRequested).toBe(true);
    let duplicate!: Promise<void>;

    act(() => {
      duplicate = reopened.result.current.installDependency("ffmpeg");
    });
    expect(install).toHaveBeenCalledTimes(1);
    await act(async () => {
      pending.resolve({ code: 500, message: "extraction failed" });
      await Promise.all([installing, duplicate]);
    });
    expect(reopened.result.current.entries.ffmpeg.installRequested).toBe(false);
    expect(reopened.result.current.entries.ffmpeg.failure).toMatchObject({
      stage: "install",
      details: "extraction failed",
    });
    expect(latest).toHaveBeenCalledTimes(2);
  });

  it("waits for the installation response and final broadcast before refreshing", async () => {
    seed(context());
    const pending = deferred<{ code: number }>();

    install.mockReturnValueOnce(pending.promise);
    const hook = renderHook(() => useDependencyChecks());

    await act(() => hook.result.current.checkDependency("ffmpeg"));
    let installing!: Promise<void>;

    act(() => {
      installing = hook.result.current.installDependency("ffmpeg");
      seed(context("ffmpeg", DependentComponentStatus.Installing));
    });
    await act(async () => {
      pending.resolve({ code: 0 });
      await installing;
    });
    expect(latest).toHaveBeenCalledTimes(1);
    act(() => seed(context("ffmpeg", DependentComponentStatus.Installed)));
    await waitFor(() => expect(latest).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(hook.result.current.entries.ffmpeg.checking).toBe(false));
  });

  it("keeps cached entries isolated by both endpoint and managed server identity", async () => {
    seed(context());
    const hook = renderHook(() => useDependencyChecks());

    await act(() => hook.result.current.checkDependency("ffmpeg"));
    act(() => useRemoteAccessStore.setState({ serverId: "server-b" }));
    expect(hook.result.current.entries.ffmpeg).toBeUndefined();
    latest.mockResolvedValueOnce(response("7.0"));
    await act(() => hook.result.current.checkDependency("ffmpeg"));
    expect(hook.result.current.entries.ffmpeg.latestVersion?.version).toBe("7.0");
    act(() => useRemoteAccessStore.setState({ serverId: "server-a" }));
    expect(hook.result.current.entries.ffmpeg.latestVersion?.version).toBe("6.1");
    env.apiEndpoint = "https://another-endpoint.example";
    hook.rerender();
    expect(hook.result.current.entries.ffmpeg).toBeUndefined();
    latest.mockResolvedValueOnce(response("8.0"));
    await act(() => hook.result.current.checkDependency("ffmpeg"));
    expect(hook.result.current.entries.ffmpeg.latestVersion?.version).toBe("8.0");
    expect(latest).toHaveBeenCalledTimes(3);
  });

  it("does not publish a late response into another server's cache", async () => {
    seed(context());
    const pending = deferred<ReturnType<typeof response>>();

    latest.mockReturnValueOnce(pending.promise);
    const hook = renderHook(() => useDependencyChecks());
    let old!: Promise<void>;

    act(() => {
      old = hook.result.current.checkDependency("ffmpeg");
    });
    act(() => useRemoteAccessStore.setState({ serverId: "server-b" }));
    latest.mockResolvedValueOnce(response("7.0"));
    await act(() => hook.result.current.checkDependency("ffmpeg"));
    await act(async () => {
      pending.resolve(response("old-server"));
      await old;
    });
    expect(hook.result.current.entries.ffmpeg.latestVersion?.version).toBe("7.0");
    act(() => useRemoteAccessStore.setState({ serverId: "server-a" }));
    expect(hook.result.current.entries.ffmpeg.latestVersion).toBeUndefined();
  });

  it("limits automatic cards and explicit batches together to three concurrent checks", async () => {
    const ids = Array.from({ length: 7 }, (_, i) => "tool-" + i);

    seed(...ids.map((id) => context(id)));
    const completions: Array<() => void> = [];
    let running = 0,
      maximum = 0;

    latest.mockImplementation(() => {
      running++;
      maximum = Math.max(maximum, running);

      return new Promise((resolve) =>
        completions.push(() => {
          running--;
          resolve(response());
        }),
      );
    });
    const hook = renderHook(() => useDependencyChecks());

    render(
      <>
        {ids.slice(0, 4).map((id) => (
          <DependencyManager key={id} id={id} />
        ))}
      </>,
    );
    let batch!: Promise<void>;

    act(() => {
      batch = hook.result.current.checkDependencies([...ids.slice(4), ids[4]]);
    });
    expect(latest).toHaveBeenCalledTimes(3);
    for (let pass = 0; pass < 3; pass++) {
      await act(async () => {
        completions.splice(0).forEach((done) => done());
      });
    }
    await act(() => batch);
    expect(maximum).toBe(3);
    expect(latest).toHaveBeenCalledTimes(7);
    expect(discover).toHaveBeenCalledTimes(3);
    expect(hook.result.current.checking).toBe(false);
  });

  it("discards an older lookup invalidated by an external installation", async () => {
    seed(context());
    const pending = deferred<ReturnType<typeof response>>();

    latest.mockReturnValueOnce(pending.promise).mockResolvedValue(response("6.1", false));
    const hook = renderHook(() => useDependencyChecks());
    let initial!: Promise<void>;

    act(() => {
      initial = hook.result.current.checkDependency("ffmpeg");
    });
    act(() => seed(context("ffmpeg", DependentComponentStatus.Installing)));
    act(() => seed(context("ffmpeg", DependentComponentStatus.Installed)));
    await act(async () => {
      pending.resolve(response("stale", true));
      await initial;
    });
    await waitFor(() =>
      expect(hook.result.current.entries.ffmpeg.latestVersion?.canUpdate).toBe(false),
    );
    expect(latest).toHaveBeenCalledTimes(2);
    expect(hook.result.current.entries.ffmpeg.latestVersion?.version).toBe("6.1");
  });

  it.each([
    { version: "3.13.5" },
    { location: "/external/python3" },
    { canAutoInstallOnCurrentPlatform: false },
  ])("refreshes cached versions after an existing source changes: %j", async (changed) => {
    const original = { ...context("python"), version: "3.13.16" };
    seed(original);
    expect(latest).not.toHaveBeenCalled();
    latest.mockResolvedValueOnce(response("3.13.16"));
    const hook = renderHook(() => useDependencyChecks());
    await act(() => hook.result.current.checkDependency("python"));
    latest.mockResolvedValueOnce(response("3.13.5", false));
    act(() => seed({ ...original, ...changed }));
    await waitFor(() =>
      expect(hook.result.current.entries.python.latestVersion).toMatchObject({
        version: "3.13.5",
        canUpdate: false,
      }),
    );
    expect(latest).toHaveBeenCalledTimes(2);
    expect(discover).not.toHaveBeenCalled();
  });

  it("discards an in-flight old source version without clearing the installation failure", async () => {
    const original = { ...context("python"), error: "Previous installation failed" };
    seed(original);
    const pending = deferred<ReturnType<typeof response>>();
    latest.mockReturnValueOnce(pending.promise).mockResolvedValue(response("3.13.5", false));
    const hook = renderHook(() => useDependencyChecks());
    let initial!: Promise<void>;
    act(() => {
      initial = hook.result.current.checkDependency("python");
    });
    act(() => seed({ ...original, version: "3.13.5" }));
    await act(async () => {
      pending.resolve(response("3.13.16", true));
      await initial;
    });
    await waitFor(() =>
      expect(hook.result.current.entries.python.latestVersion).toMatchObject({
        version: "3.13.5",
        canUpdate: false,
      }),
    );
    expect(latest).toHaveBeenCalledTimes(2);
    expect(hook.result.current.entries.python.failure).toEqual({
      stage: "install",
      details: "Previous installation failed",
    });
  });

  it("accepts an explicit discovery's source broadcast and clears the previous failed install", async () => {
    const previous = {
      ...context("python", DependentComponentStatus.NotInstalled),
      version: undefined,
      error: "Previous installation failed",
    };
    seed(previous);
    const hook = renderHook(() => useDependencyChecks());
    await act(() => hook.result.current.checkDependency("python"));
    expect(hook.result.current.entries.python.failure?.stage).toBe("install");
    discover.mockImplementationOnce(async () => {
      seed({ ...context("python"), version: "3.13.5", location: "/external/python3" });
      return { code: 0 };
    });
    latest.mockResolvedValueOnce(response("3.13.5", false));
    await act(() => hook.result.current.checkDependency("python", true, true));
    expect(hook.result.current.entries.python.latestVersion).toMatchObject({
      version: "3.13.5",
      canUpdate: false,
    });
    expect(hook.result.current.entries.python.failure).toBeUndefined();
    expect(hook.result.current.entries.python.checking).toBe(false);
    expect(discover).toHaveBeenCalledTimes(1);
    expect(latest).toHaveBeenCalledTimes(2);
  });

  it("still invalidates external source changes during an explicit check's latest lookup", async () => {
    seed(context("python"));
    const pending = deferred<ReturnType<typeof response>>();
    latest.mockReturnValueOnce(pending.promise).mockResolvedValue(response("3.13.5", false));
    const hook = renderHook(() => useDependencyChecks());
    let initial!: Promise<void>;
    act(() => {
      initial = hook.result.current.checkDependency("python", true, true);
    });
    await waitFor(() => expect(latest).toHaveBeenCalledTimes(1));
    act(() => seed({ ...context("python"), version: "3.13.5" }));
    await act(async () => {
      pending.resolve(response("old source", true));
      await initial;
    });
    await waitFor(() =>
      expect(hook.result.current.entries.python.latestVersion?.version).toBe("3.13.5"),
    );
    expect(latest).toHaveBeenCalledTimes(2);
    expect(discover).toHaveBeenCalledTimes(1);
  });

  it("offers normal version release notes as selectable plain text", async () => {
    seed(context());
    latest.mockResolvedValue({
      code: 0,
      data: {
        ...response().data,
        description: "Reviewed source\nhttps://upstream.example/release",
      },
    });
    render(<DependencyManager id="ffmpeg" />);
    expect(await screen.findByText("dependencies.versionDetails")).toBeInTheDocument();
    expect(screen.getByText(/Reviewed source/)).toHaveClass("whitespace-pre-wrap", "select-text");
  });
  it("keeps detailed failures and paths out of summary mode while exposing them in details", async () => {
    seed(context());
    latest.mockRejectedValue(new Error("Full diagnostic message"));
    const open = vi.fn();
    const view = render(<DependencyManager id="ffmpeg" mode="summary" onDetails={open} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("dependencies.detailsForError");
    expect(screen.queryByText(/Full diagnostic message/)).toBeNull();
    expect(screen.queryByText("configuration.dependency.installationLocation")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "dependencies.action.details" }));
    expect(open).toHaveBeenCalledOnce();
    view.rerender(<DependencyManager id="ffmpeg" mode="details" />);
    expect((await screen.findAllByText(/Full diagnostic message/)).length).toBeGreaterThan(0);
    expect(screen.getByText("/components/ffmpeg")).toBeInTheDocument();
  });
  it("preserves supported manual installations while disabling automatic install actions", async () => {
    const manual = { ...context(), canAutoInstallOnCurrentPlatform: false };
    seed(manual);
    render(<DependencyManager id="ffmpeg" mode="summary" />);
    expect(await screen.findByText("dependencies.manualInstallation")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "configuration.dependency.update" })).toBeNull();
    expect(screen.getByRole("button", { name: "dependencies.action.check" })).toBeInTheDocument();
    expect(
      screen.queryByLabelText("configuration.dependency.notAvailableOnCurrentPlatform"),
    ).toBeNull();
  });
  it("explains an externally managed Python without claiming its platform needs manual installation", async () => {
    const external = {
      ...context("cloud-python-component-service"),
      canAutoInstallOnCurrentPlatform: false,
    };
    seed(external);
    render(<DependencyManager id={external.id} mode="summary" />);
    expect(await screen.findByText("dependencies.python.userManaged")).toBeInTheDocument();
    expect(screen.queryByText("dependencies.manualInstallation")).toBeNull();
    expect(screen.queryByRole("button", { name: "configuration.dependency.update" })).toBeNull();
  });
});
