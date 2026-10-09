import type { ReactNode } from "react";

import { act, cleanup, render, screen, waitFor, fireEvent } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import Component from "../Component";

import { useDependentComponentContextsStore } from "@/stores/dependentComponentContexts";
import { DependentComponentStatus } from "@/sdk/constants";

const { discover, getLatestVersion, install } = vi.hoisted(() => ({
  discover: vi.fn(),
  getLatestVersion: vi.fn(),
  install: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: {
    component: {
      discoverDependentComponent: discover,
      getDependentComponentLatestVersion: getLatestVersion,
      installDependentComponent: install,
    },
  },
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));
vi.mock("@/components/Error/Details", () => ({
  default: ({ text }: { text: string }) => <pre data-testid="error-details">{text}</pre>,
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({
    children,
    onClick,
    onPress,
    isDisabled,
  }: {
    children: ReactNode;
    onClick?: () => void;
    onPress?: () => void;
    isDisabled?: boolean;
  }) => (
    <button disabled={isDisabled} onClick={onClick ?? onPress}>
      {children}
    </button>
  ),
  Chip: ({ children, title }: { children: ReactNode; title?: string }) => (
    <span data-chip="" title={title}>
      {children}
    </span>
  ),
  Spinner: () => <span data-testid="spinner" />,
  Progress: ({ value, isIndeterminate, "aria-label": label }: any) => (
    <div
      aria-label={label}
      aria-valuenow={isIndeterminate ? undefined : value}
      role="progressbar"
    />
  ),
  Modal: () => null,
}));

const id = "ffmpeg";

const context = (
  status: DependentComponentStatus,
  version?: string,
  isAvailableOnCurrentPlatform = true,
) => ({
  id,
  name: "FFmpeg",
  defaultLocation: "/components/ffmpeg",
  status,
  isAvailableOnCurrentPlatform,
  isRequired: false,
  installationProgress: 0,
  version,
});

type ComponentContext = ReturnType<
  typeof useDependentComponentContextsStore.getState
>["contexts"][number];
const seed = (...contexts: ComponentContext[]) =>
  useDependentComponentContextsStore.getState().setContexts(contexts);

const latest = (
  version: string | null,
  canUpdate: boolean,
  description?: string,
  installedVersionRecognized?: boolean,
) =>
  getLatestVersion.mockResolvedValue({
    code: 0,
    data: { version, canUpdate, description, installedVersionRecognized },
  });

describe("Dependency component", () => {
  beforeEach(() => {
    discover.mockReset().mockResolvedValue({ code: 0 });
    getLatestVersion.mockReset();
    install.mockReset();
  });

  afterEach(() => {
    cleanup();
    seed();
  });

  it("checks an installed component and offers a newer version", async () => {
    seed(context(DependentComponentStatus.Installed, "6.0"));
    latest("6.1", true);

    render(<Component id={id} />);

    expect(
      await screen.findByRole("button", { name: "configuration.dependency.update" }),
    ).toBeEnabled();
    expect(screen.getByText("6.1")).toBeInTheDocument();
    expect(discover).not.toHaveBeenCalled();
    expect(getLatestVersion).toHaveBeenCalledWith(id, { showErrorToast: false });
  });

  it("shows an installed component as up to date when nothing newer exists", async () => {
    seed(context(DependentComponentStatus.Installed, "6.1"));
    latest("6.1", false, undefined, true);

    render(<Component id={id} />);

    expect(await screen.findByLabelText("configuration.dependency.upToDate")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "configuration.dependency.update" })).toBeNull();
    expect(screen.queryByText("configuration.dependency.installedVersionNotRecognized")).toBeNull();
  });

  it("does not show a failed lookup as up to date", async () => {
    seed(context(DependentComponentStatus.Installed, "6.1"));
    latest(null, false);

    render(<Component id={id} />);

    expect(
      await screen.findByText("configuration.dependency.couldNotCheckForUpdates"),
    ).toBeInTheDocument();
    expect(screen.queryByLabelText("configuration.dependency.upToDate")).toBeNull();
  });

  it("does not show a platform without a published build as up to date", async () => {
    seed(context(DependentComponentStatus.Installed, "7.1.1"));
    latest("N/A", false, "Runtime is not supported: osx-arm64.");

    render(<Component id={id} />);

    const chip = await screen.findByText("configuration.dependency.couldNotCheckForUpdates");

    expect(chip).toHaveAttribute("title", "Runtime is not supported: osx-arm64.");
    expect(screen.queryByLabelText("configuration.dependency.upToDate")).toBeNull();
  });

  it("does not show an installed version it could not compare as up to date", async () => {
    seed(context(DependentComponentStatus.Installed, "unknown", true));
    latest("2.5.0.1", false, undefined, false);

    render(<Component id={id} />);

    expect(
      await screen.findByText("configuration.dependency.installedVersionNotRecognized"),
    ).toBeInTheDocument();
    expect(screen.queryByLabelText("configuration.dependency.upToDate")).toBeNull();
    expect(screen.queryByRole("button", { name: "configuration.dependency.update" })).toBeNull();
  });

  it("checks a component that is not installed", async () => {
    seed(context(DependentComponentStatus.NotInstalled));
    latest("6.1", true);

    render(<Component id={id} />);

    expect(
      await screen.findByRole("button", { name: "configuration.dependency.install" }),
    ).toBeInTheDocument();
    expect(discover).toHaveBeenCalledWith(id, { showErrorToast: false });
  });

  it("asks nothing for a component unavailable on this platform", async () => {
    seed(context(DependentComponentStatus.NotInstalled, undefined, false));

    render(<Component id={id} />);

    expect(
      await screen.findByText("configuration.dependency.notAvailableOnCurrentPlatform"),
    ).toBeInTheDocument();
    expect(discover).not.toHaveBeenCalled();
    expect(getLatestVersion).not.toHaveBeenCalled();
  });

  it("checks again with the current state once an update finishes installing", async () => {
    seed(context(DependentComponentStatus.Installed, "6.0"));
    latest("6.1", true);

    render(<Component id={id} />);
    await screen.findByRole("button", { name: "configuration.dependency.update" });

    act(() => seed(context(DependentComponentStatus.Installing, "6.0")));
    latest("6.1", false);
    act(() => seed(context(DependentComponentStatus.Installed, "6.1")));

    expect(await screen.findByLabelText("configuration.dependency.upToDate")).toBeInTheDocument();
    await waitFor(() => expect(getLatestVersion).toHaveBeenCalledTimes(2));
    expect(discover).not.toHaveBeenCalled();
  });

  it("reads the current store state when re-checking, not the state it was created with", async () => {
    seed(context(DependentComponentStatus.Installed, "6.0"));
    latest("6.1", true);

    render(<Component id={id} />);
    await screen.findByRole("button", { name: "configuration.dependency.update" });

    // The component became unavailable (e.g. the view switched to a server on another platform)
    // while an install finished: the re-check must see that and ask for nothing.
    act(() => seed(context(DependentComponentStatus.Installing, "6.0")));
    act(() => seed(context(DependentComponentStatus.Installed, "6.1", false)));

    await act(async () => {});
    expect(discover).not.toHaveBeenCalled();
    expect(getLatestVersion).toHaveBeenCalledTimes(1);
  });

  it("handles discovery failure inline and allows a retry", async () => {
    seed(context(DependentComponentStatus.NotInstalled));
    discover.mockRejectedValueOnce(new Error("Discovery failed\nserver diagnostic"));
    latest("6.1", true);
    render(<Component id={id} />);

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "configuration.dependency.discoveryFailed",
    );
    expect(screen.getByTestId("error-details")).toHaveTextContent("server diagnostic");
    expect(getLatestVersion).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "configuration.dependency.checkAgain" }));
    expect(
      await screen.findByRole("button", { name: "configuration.dependency.install" }),
    ).toBeEnabled();
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("handles latest-version HTTP response errors without showing an up-to-date state", async () => {
    seed(context(DependentComponentStatus.Installed, "6.0"));
    getLatestVersion.mockRejectedValue({
      error: { message: "Release lookup failed\nmore details" },
    });
    render(<Component id={id} />);

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "configuration.dependency.failedToGetVersion",
    );
    expect(screen.getByTestId("error-details")).toHaveTextContent("Release lookup failed");
    expect(screen.queryByLabelText("configuration.dependency.upToDate")).toBeNull();
    expect(getLatestVersion).toHaveBeenCalledWith(id, { showErrorToast: false });
  });

  it("disables duplicate actions while installing and refreshes only after completion", async () => {
    seed(context(DependentComponentStatus.NotInstalled));
    latest("6.1", true);
    let finish!: (response: { code: number }) => void;

    install.mockReturnValue(
      new Promise((resolve) => {
        finish = resolve;
      }),
    );
    render(<Component id={id} />);
    const button = await screen.findByRole("button", { name: "configuration.dependency.install" });

    fireEvent.click(button);
    fireEvent.click(button);
    expect(button).toBeDisabled();
    expect(
      screen.getByRole("button", { name: "configuration.dependency.checkAgain" }),
    ).toBeDisabled();
    expect(install).toHaveBeenCalledExactlyOnceWith(id, { showErrorToast: false });
    act(() => seed({ ...context(DependentComponentStatus.Installing), installationProgress: 100 }));
    expect(screen.getByRole("status")).toHaveTextContent(
      "configuration.dependency.installingAndVerifying",
    );
    expect(screen.queryByLabelText("configuration.dependency.upToDate")).toBeNull();
    expect(getLatestVersion).toHaveBeenCalledTimes(1);

    latest("6.1", false, undefined, true);
    await act(async () => {
      seed(context(DependentComponentStatus.Installed, "6.1"));
      finish({ code: 0 });
    });
    expect(await screen.findByLabelText("configuration.dependency.upToDate")).toBeInTheDocument();
    expect(getLatestVersion).toHaveBeenCalledTimes(2);
  });

  it("keeps the installed version visible after a failed update and offers retry", async () => {
    seed(context(DependentComponentStatus.Installed, "6.0"));
    latest("6.1", true);
    install.mockResolvedValue({ code: 500, message: "Extraction failed\nlong technical details" });
    render(<Component id={id} />);
    fireEvent.click(await screen.findByRole("button", { name: "configuration.dependency.update" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "configuration.dependency.installFailed",
    );
    expect(screen.getByText("6.0")).toBeInTheDocument();
    expect(screen.getByLabelText("configuration.dependency.installed")).toBeInTheDocument();
    expect(screen.getByTestId("error-details")).toHaveTextContent("long technical details");
    expect(screen.getByRole("button", { name: "configuration.dependency.update" })).toBeEnabled();
    expect(screen.queryByLabelText("configuration.dependency.upToDate")).toBeNull();
  });

  it("catches installation network failures without leaving controls busy", async () => {
    seed(context(DependentComponentStatus.NotInstalled));
    latest("6.1", true);
    install.mockRejectedValue(new TypeError("Connection lost"));
    render(<Component id={id} />);
    fireEvent.click(
      await screen.findByRole("button", { name: "configuration.dependency.install" }),
    );

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "configuration.dependency.installFailed",
    );
    expect(screen.getByRole("button", { name: "configuration.dependency.install" })).toBeEnabled();
  });

  it.each([DependentComponentStatus.Installed, DependentComponentStatus.NotInstalled])(
    "preserves a previous failed install when reopening settings with status %s",
    async (status) => {
      seed({
        ...context(status, status === DependentComponentStatus.Installed ? "6.0" : undefined),
        error: "Prior extraction failure",
      });
      // Mirrors a successful discovery broadcasting a context with its error cleared.
      discover.mockImplementation(async () => {
        seed(context(DependentComponentStatus.Installed, "6.0"));

        return { code: 0 };
      });
      latest("6.1", true);
      const first = render(<Component id={id} />);

      await waitFor(() => expect(getLatestVersion).toHaveBeenCalledTimes(1));
      first.unmount();
      render(<Component id={id} />);
      await waitFor(() => expect(getLatestVersion).toHaveBeenCalledTimes(2));

      expect(screen.getByRole("alert")).toHaveTextContent("configuration.dependency.installFailed");
      expect(screen.getByTestId("error-details")).toHaveTextContent("Prior extraction failure");
      expect(discover).not.toHaveBeenCalled();

      // An explicit check is allowed to rediscover the component and clear the old result.
      fireEvent.click(screen.getByRole("button", { name: "configuration.dependency.checkAgain" }));
      await waitFor(() => expect(discover).toHaveBeenCalledTimes(1));
      await waitFor(() => expect(screen.queryByRole("alert")).toBeNull());
    },
  );

  it.each([DependentComponentStatus.Installed, DependentComponentStatus.NotInstalled])(
    "keeps the result of a failed install from another browser with status %s",
    async (status) => {
      seed({ ...context(DependentComponentStatus.Installing, "6.0"), installationProgress: 90 });
      latest("6.1", true);
      discover.mockImplementation(async () => {
        seed(context(DependentComponentStatus.Installed, "6.0"));

        return { code: 0 };
      });
      render(<Component id={id} />);
      act(() =>
        useDependentComponentContextsStore.getState().updateContext({
          ...context(status, status === DependentComponentStatus.Installed ? "6.0" : undefined),
          error: "Remote extraction failure",
        }),
      );

      await waitFor(() => expect(getLatestVersion).toHaveBeenCalledTimes(1));
      expect(discover).not.toHaveBeenCalled();
      expect(screen.getByRole("alert")).toHaveTextContent("configuration.dependency.installFailed");
      expect(screen.getByTestId("error-details")).toHaveTextContent("Remote extraction failure");
      expect(
        screen.getByRole("button", {
          name:
            status === DependentComponentStatus.Installed
              ? "configuration.dependency.update"
              : "configuration.dependency.install",
        }),
      ).toBeEnabled();
    },
  );

  it("waits for an installation already running instead of starting discovery", async () => {
    seed({ ...context(DependentComponentStatus.Installing), installationProgress: 45 });
    render(<Component id={id} />);

    expect(screen.getByRole("progressbar")).toHaveAttribute("aria-valuenow", "45");
    expect(screen.getByRole("status")).toHaveTextContent("configuration.dependency.downloading");
    expect(discover).not.toHaveBeenCalled();
    expect(getLatestVersion).not.toHaveBeenCalled();
    expect(
      screen.getByRole("button", { name: "configuration.dependency.checkAgain" }),
    ).toBeDisabled();
  });
});
