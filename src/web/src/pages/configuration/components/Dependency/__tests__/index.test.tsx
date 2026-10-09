import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import Dependency from "..";

import { useDependentComponentContextsStore } from "@/stores/dependentComponentContexts";
import { DependentComponentStatus } from "@/sdk/constants";

const state = vi.hoisted(() => ({ pureClient: false }));

vi.mock("@/stores/remoteAccess", () => ({ useIsPureClient: () => state.pureClient }));
vi.mock("../components/Component", () => ({ default: () => <span>Component controls</span> }));
vi.mock("@/components/bakaui", () => ({
  Spinner: () => <span role="status">Loading</span>,
  Tooltip: ({ children }: any) => children,
}));

beforeEach(() => {
  state.pureClient = false;
  useDependentComponentContextsStore.getState().setContexts([
    {
      id: "7z-archiver-component-service",
      name: "7-Zip",
      description: "Extract archives",
      defaultLocation: "/components/7z",
      status: DependentComponentStatus.Installed,
      isAvailableOnCurrentPlatform: true,
      isRequired: false,
      installationProgress: 0,
    },
    {
      id: "locale-emulator-component-service",
      name: "Locale Emulator",
      description: "Legacy applications",
      defaultLocation: "/components/locale",
      status: DependentComponentStatus.NotInstalled,
      isAvailableOnCurrentPlatform: false,
      isRequired: false,
      installationProgress: 0,
    },
  ]);
});

afterEach(() => {
  cleanup();
  useDependentComponentContextsStore.getState().setContexts([]);
});

describe("dependency settings rows", () => {
  it.each(["7-zip", "archives"])("keeps names and descriptions searchable (%s)", (query) => {
    render(<Dependency query={query} />);
    expect(screen.getByText("7-Zip")).toBeInTheDocument();
    expect(screen.queryByText("Locale Emulator")).toBeNull();
    expect(screen.getByText("configuration.dependency.archivePurpose")).toBeInTheDocument();
  });

  it("preserves the managed-server location and Locale Emulator distinction", () => {
    state.pureClient = true;
    render(<Dependency />);
    expect(screen.getByText("configuration.dependency.installedOnTheServer")).toBeInTheDocument();
    expect(
      screen.getByText("configuration.dependency.localeEmulatorRunsOnThisMachine"),
    ).toBeInTheDocument();
  });

  it("shows a loading state while contexts have not arrived", () => {
    useDependentComponentContextsStore.getState().setContexts([]);
    render(<Dependency />);
    expect(screen.getByText("configuration.dependency.loadingComponents")).toBeInTheDocument();
    expect(screen.getByRole("status")).toBeInTheDocument();
  });
});
