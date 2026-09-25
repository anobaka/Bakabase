import type { ReactNode } from "react";

import { act, cleanup, render, screen, waitFor } from "@testing-library/react";
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
vi.mock("@/components/bakaui", () => ({
  Button: ({
    children,
    onClick,
    onPress,
  }: {
    children: ReactNode;
    onClick?: () => void;
    onPress?: () => void;
  }) => <button onClick={onClick ?? onPress}>{children}</button>,
  Chip: ({ children, title }: { children: ReactNode; title?: string }) => (
    <span data-chip="" title={title}>
      {children}
    </span>
  ),
  Spinner: () => <span data-testid="spinner" />,
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

const seed = (...contexts: ReturnType<typeof context>[]) =>
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

    expect(await screen.findByText(/configuration.dependency.clickToUpdate/)).toHaveTextContent(
      "6.1",
    );
    expect(discover).toHaveBeenCalledWith(id);
    expect(getLatestVersion).toHaveBeenCalledWith(id);
  });

  it("shows an installed component as up to date when nothing newer exists", async () => {
    seed(context(DependentComponentStatus.Installed, "6.1"));
    latest("6.1", false, undefined, true);

    render(<Component id={id} />);

    expect(await screen.findByLabelText("configuration.dependency.upToDate")).toBeInTheDocument();
    expect(screen.queryByText(/configuration.dependency.clickToUpdate/)).toBeNull();
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
    expect(screen.queryByText(/configuration.dependency.clickToUpdate/)).toBeNull();
  });

  it("checks a component that is not installed", async () => {
    seed(context(DependentComponentStatus.NotInstalled));
    latest("6.1", true);

    render(<Component id={id} />);

    expect(await screen.findByText(/configuration.dependency.clickToUpdate/)).toBeInTheDocument();
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
    await screen.findByText(/configuration.dependency.clickToUpdate/);

    act(() => seed(context(DependentComponentStatus.Installing, "6.0")));
    latest("6.1", false);
    act(() => seed(context(DependentComponentStatus.Installed, "6.1")));

    expect(await screen.findByLabelText("configuration.dependency.upToDate")).toBeInTheDocument();
    await waitFor(() => expect(getLatestVersion).toHaveBeenCalledTimes(2));
    expect(discover).toHaveBeenCalledTimes(2);
  });

  it("reads the current store state when re-checking, not the state it was created with", async () => {
    seed(context(DependentComponentStatus.Installed, "6.0"));
    latest("6.1", true);

    render(<Component id={id} />);
    await screen.findByText(/configuration.dependency.clickToUpdate/);

    // The component became unavailable (e.g. the view switched to a server on another platform)
    // while an install finished: the re-check must see that and ask for nothing.
    act(() => seed(context(DependentComponentStatus.Installing, "6.0")));
    discover.mockImplementation(async () => {
      seed(context(DependentComponentStatus.Installed, "6.1", false));

      return { code: 0 };
    });
    act(() => seed(context(DependentComponentStatus.Installed, "6.1")));

    await waitFor(() => expect(discover).toHaveBeenCalledTimes(2));
    await act(async () => {});
    expect(getLatestVersion).toHaveBeenCalledTimes(1);
  });
});
