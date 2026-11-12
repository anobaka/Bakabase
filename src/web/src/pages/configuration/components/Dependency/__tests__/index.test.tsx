import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import Dependency from "..";
import { useDependentComponentContextsStore } from "@/stores/dependentComponentContexts";
import { DependentComponentStatus } from "@/sdk/constants";

const state = vi.hoisted(() => ({ pureClient: false, managerMounted: vi.fn(), entries: {} }));
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: { count?: number }) =>
      options?.count == null ? key : `${key}:${options.count}`,
  }),
}));
vi.mock("@/stores/remoteAccess", () => ({
  useIsPureClient: () => state.pureClient,
  useRemoteAccessStore: (select: (value: unknown) => unknown) =>
    select({ serverName: "Home", serverId: "server-a" }),
}));
vi.mock("@/components/Dependency/DependencyManager", () => ({
  default: () => {
    state.managerMounted();
    return <span>Component controls</span>;
  },
  useDependencyChecks: () => ({ entries: state.entries }),
}));
vi.mock("@/components/bakaui", () => ({
  Spinner: () => <span role="status">Loading</span>,
  Tooltip: ({ children }: { children: React.ReactNode }) => children,
}));

beforeEach(() => {
  state.pureClient = false;
  state.entries = {};
  state.managerMounted.mockClear();
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
      id: "baidu-pcs-go-component-service",
      name: "BaiduPCS-Go",
      defaultLocation: "/components/baidu",
      status: DependentComponentStatus.NotInstalled,
      isAvailableOnCurrentPlatform: true,
      isRequired: false,
      installationProgress: 0,
    },
    {
      id: "locale-emulator-component-service",
      name: "Locale Emulator",
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

describe("passive dependency settings summary", () => {
  it.each(["7-zip", "archives"])(
    "keeps names and purposes searchable (%s) and carries the search into management",
    (query) => {
      render(<Dependency query={query} />);
      expect(screen.getByText("7-Zip")).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "dependencies.manage" })).toHaveAttribute(
        "href",
        `#/configuration?tab=dependency&q=${query}`,
      );
      expect(state.managerMounted).not.toHaveBeenCalled();
    },
  );
  it("shows installed and installing counts without treating missing optional components as failures", () => {
    render(<Dependency />);
    expect(screen.getByText("dependencies.count.installed:1")).toBeInTheDocument();
    expect(screen.getByText("dependencies.count.attention:0")).toBeInTheDocument();
    expect(screen.queryByText("Component controls")).toBeNull();
    expect(state.managerMounted).not.toHaveBeenCalled();
  });
  it("opens the whole catalogue when the search only matches the dependency section", () => {
    render(<Dependency query="依赖" />);
    expect(screen.getByRole("link", { name: "dependencies.manage" })).toHaveAttribute(
      "href",
      "#/configuration?tab=dependency",
    );
  });
  it("preserves the server and local Locale Emulator distinction", () => {
    state.pureClient = true;
    render(<Dependency />);
    expect(screen.getByText("dependencies.server")).toBeInTheDocument();
    expect(
      screen.getByText("configuration.dependency.localeEmulatorRunsOnThisMachine"),
    ).toBeInTheDocument();
  });
  it("shows loading without mounting per-component checks", () => {
    useDependentComponentContextsStore.getState().setContexts([]);
    render(<Dependency />);
    expect(screen.getByRole("status")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "dependencies.manage" })).toHaveAttribute(
      "href",
      "#/configuration?tab=dependency",
    );
    expect(state.managerMounted).not.toHaveBeenCalled();
  });
});
