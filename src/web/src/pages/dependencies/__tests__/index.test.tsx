import type { ReactNode } from "react";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import DependenciesPage from "..";
import {
  dependencyNeedsAttention,
  type DependencyCheckInfo,
  type DependencyContext,
} from "@/components/Dependency/presentation";
import { useDependentComponentContextsStore } from "@/stores/dependentComponentContexts";
import { DependentComponentStatus as Status } from "@/sdk/constants";

const state = vi.hoisted(() => ({
  entries: {} as Record<string, DependencyCheckInfo>,
  checkDependencies: vi.fn(),
}));
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: { count?: number; name?: string; id?: string }) =>
      options?.count != null
        ? `${key}:${options.count}`
        : options?.name
          ? `${key}:${options.name}`
          : options?.id
            ? `${key}:${options.id}`
            : key,
  }),
}));
vi.mock("@/stores/remoteAccess", () => ({
  useIsPureClient: () => true,
  useRemoteAccessStore: (select: (value: unknown) => unknown) =>
    select({ serverName: "Home", serverId: "server-a" }),
}));
vi.mock("@/components/Dependency/DependencyManager", () => ({
  default: ({ id, onDetails }: { id: string; onDetails?: () => void }) => (
    <span data-testid={`controls:${id}`}>
      Install controls{onDetails && <button onClick={onDetails}>Details:{id}</button>}
    </span>
  ),
  useDependencyChecks: () => ({
    entries: state.entries,
    checking: false,
    checkDependencies: state.checkDependencies,
  }),
}));
vi.mock("@/components/Dependency/PythonRuntimeConfiguration", () => ({
  default: () => <p>dependencies.pythonAutomatic</p>,
}));
vi.mock("@heroui/react", () => ({
  Drawer: ({ isOpen, children }: { isOpen: boolean; children: ReactNode }) =>
    isOpen ? <div role="dialog">{children}</div> : null,
  DrawerContent: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DrawerHeader: ({ children }: { children: ReactNode }) => <header>{children}</header>,
  DrawerBody: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DrawerFooter: ({ children }: { children: ReactNode }) => <footer>{children}</footer>,
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
  Input: ({
    label,
    value,
    onValueChange,
  }: {
    label: string;
    value: string;
    onValueChange: (value: string) => void;
  }) => (
    <label>
      {label}
      <input value={value} onChange={(event) => onValueChange(event.target.value)} />
    </label>
  ),
  Select: ({
    label,
    selectedKeys,
    onSelectionChange,
    dataSource,
  }: {
    label: string;
    selectedKeys: string[];
    onSelectionChange: (keys: Set<string>) => void;
    dataSource: { value: string; label: string }[];
  }) => (
    <label>
      {label}
      <select
        value={selectedKeys[0]}
        onChange={(event) => onSelectionChange(new Set([event.target.value]))}
      >
        {dataSource.map((item) => (
          <option key={item.value} value={item.value}>
            {item.label}
          </option>
        ))}
      </select>
    </label>
  ),
  Spinner: () => <span>Loading</span>,
}));

const component = (
  id: string,
  name: string,
  status: Status = Status.NotInstalled,
  extra: Partial<DependencyContext> = {},
): DependencyContext => ({
  id,
  name,
  status,
  defaultLocation: `/components/${id}`,
  isAvailableOnCurrentPlatform: true,
  isRequired: false,
  installationProgress: 0,
  ...extra,
});
let contexts: DependencyContext[];
const renderPage = (search = "") =>
  render(
    <MemoryRouter initialEntries={[`/dependencies${search}`]}>
      <DependenciesPage />
    </MemoryRouter>,
  );

beforeEach(() => {
  state.checkDependencies.mockReset().mockResolvedValue(undefined);
  state.entries = {
    ffmpeg: { latestVersion: { canUpdate: true } },
    "7z-archiver-component-service": { latestVersion: { canUpdate: true } },
  };
  contexts = [
    component("ffmpeg", "FFmpeg", Status.Installed),
    component("7z-archiver-component-service", "7-Zip", Status.NotInstalled, {
      supportedTargets: [{ os: "windows", architecture: "x64", canAutoInstall: true }],
    }),
    component("baidu-pcs-go-component-service", "BaiduPCS-Go"),
    component("gdown-component-service", "gdown", Status.Installing),
    component("cloud-python-component-service", "Python"),
    component("locale-emulator-component-service", "Locale Emulator", Status.NotInstalled, {
      isAvailableOnCurrentPlatform: false,
    }),
    component("future-component", "Future component"),
  ];
  useDependentComponentContextsStore.getState().setContexts(contexts);
});
afterEach(() => {
  cleanup();
  useDependentComponentContextsStore.getState().setContexts([]);
});

describe("dependency manager page", () => {
  it("shows one component catalogue with category tags, runtime guidance and server ownership", () => {
    renderPage();
    expect(screen.getByText("dependencies.server:Home")).toBeInTheDocument();
    expect(screen.queryByText("dependencies.pythonAutomatic")).toBeNull();
    expect(
      screen.queryByText("configuration.dependency.localeEmulatorRunsOnThisMachine"),
    ).toBeNull();
    expect(screen.queryByRole("region", { name: "dependencies.group.other" })).toBeNull();
    expect(
      within(screen.getByRole("article", { name: "Future component" })).getByText(
        "dependencies.group.other",
      ),
    ).toBeInTheDocument();
    expect(
      within(screen.getByRole("article", { name: "dependencies.python.name" })).getByText(
        "dependencies.group.runtime",
      ),
    ).toBeInTheDocument();
    expect(screen.getAllByRole("article")).toHaveLength(7);
    expect(screen.getByText("dependencies.count.attention:1")).toBeInTheDocument();
  });
  it("still matches category names in search after replacing category sections with tags", () => {
    renderPage();
    fireEvent.change(screen.getByLabelText("dependencies.search"), {
      target: { value: "download" },
    });
    expect(screen.getAllByRole("article").map((card) => card.getAttribute("aria-label"))).toEqual([
      "BaiduPCS-Go",
      "gdown",
    ]);
    fireEvent.change(screen.getByLabelText("dependencies.search"), {
      target: { value: "runtime" },
    });
    expect(screen.getAllByRole("article")).toHaveLength(1);
    expect(screen.getByRole("article", { name: "dependencies.python.name" })).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("dependencies.search"), {
      target: { value: "windows" },
    });
    expect(screen.getAllByRole("article")).toHaveLength(1);
    expect(screen.getByRole("article", { name: "7-Zip" })).toBeInTheDocument();
  });
  it("opens full platform notes and Python configuration in details without growing the summary cards", () => {
    renderPage();
    fireEvent.click(
      screen.getByRole("button", { name: "Details:locale-emulator-component-service" }),
    );
    expect(
      within(screen.getByRole("dialog")).getByText(
        "configuration.dependency.localeEmulatorRunsOnThisMachine",
      ),
    ).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "common.action.close" }));
    fireEvent.click(screen.getByRole("button", { name: "Details:cloud-python-component-service" }));
    expect(
      within(screen.getByRole("dialog")).getByText("dependencies.pythonAutomatic"),
    ).toBeInTheDocument();
  });
  it("excludes optional never-installed updates and unsupported platforms from actionable and missing filters", () => {
    renderPage();
    fireEvent.change(screen.getByLabelText("dependencies.filter.label"), {
      target: { value: "attention" },
    });
    expect(screen.getAllByRole("article")).toHaveLength(1);
    expect(screen.getByRole("article", { name: "FFmpeg" })).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("dependencies.filter.label"), {
      target: { value: "missing" },
    });
    expect(screen.queryByRole("article", { name: "Locale Emulator" })).toBeNull();
    expect(screen.getByRole("article", { name: "7-Zip" })).toBeInTheDocument();
    expect(screen.queryByRole("article", { name: "gdown" })).toBeNull();
  });
  it("keeps a deep-linked component visible and focused even outside current search and status", () => {
    renderPage("?component=baidu-pcs-go-component-service");
    const card = screen.getByRole("article", { name: "BaiduPCS-Go" });
    expect(card).toHaveFocus();
    fireEvent.change(screen.getByLabelText("dependencies.search"), {
      target: { value: "no matches" },
    });
    fireEvent.change(screen.getByLabelText("dependencies.filter.label"), {
      target: { value: "installed" },
    });
    expect(screen.getAllByRole("article")).toEqual([card]);
    expect(screen.getByRole("status")).toHaveTextContent(
      "dependencies.focusOutsideFilter:BaiduPCS-Go",
    );
    fireEvent.click(screen.getByRole("button", { name: "dependencies.clearFocus" }));
    expect(screen.queryByRole("article")).toBeNull();
    expect(screen.getByText("dependencies.empty")).toBeInTheDocument();
  });
  it("reports unknown component links without hiding the component catalogue", () => {
    renderPage("?component=removed");
    expect(screen.getByRole("status")).toHaveTextContent("dependencies.unknownComponent:removed");
    expect(screen.getAllByRole("article")).toHaveLength(7);
  });
  it("carries settings search to the page and only checks all supported components on explicit request", () => {
    renderPage("?q=7-Zip");
    expect(screen.getAllByRole("article")).toHaveLength(1);
    expect(screen.getByRole("article", { name: "7-Zip" })).toBeInTheDocument();
    expect(state.checkDependencies).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "dependencies.checkAll" }));
    expect(state.checkDependencies).toHaveBeenCalledWith(
      expect.not.arrayContaining(["locale-emulator-component-service"]),
    );
    expect(state.checkDependencies.mock.calls[0][0]).toHaveLength(6);
  });
  it("marks missing required tools and failed checks actionable, while optional absence alone stays normal", () => {
    expect(
      dependencyNeedsAttention(component("optional", "Optional"), {
        latestVersion: { canUpdate: true },
      }),
    ).toBe(false);
    expect(
      dependencyNeedsAttention(
        component("required", "Required", Status.NotInstalled, { isRequired: true }),
      ),
    ).toBe(true);
    expect(
      dependencyNeedsAttention(component("failed", "Failed"), {
        failure: { stage: "latest", details: "Offline" },
      }),
    ).toBe(true);
    expect(
      dependencyNeedsAttention(
        component("unsupported", "Unsupported", Status.NotInstalled, {
          isAvailableOnCurrentPlatform: false,
        }),
        { failure: { stage: "install", details: "Unsupported" } },
      ),
    ).toBe(false);
  });
});
