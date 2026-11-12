import type { ReactNode } from "react";
import { useEffect, useState } from "react";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter, useLocation, useNavigate } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import ConfigurationPage from "..";
import SettingsSection from "../components/SettingsSection";

const state = vi.hoisted(() => ({ mounted: vi.fn(), unmounted: vi.fn(), appInfo: vi.fn() }));
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: { group?: string }) =>
      options?.group ? `${key}:${options.group}` : key,
  }),
}));
vi.mock("@/sdk/BApi", () => ({ default: { app: { getAppInfo: state.appInfo } } }));
vi.mock("@/components/bakaui", () => ({
  Button: ({
    children,
    onPress,
    ...props
  }: {
    children: ReactNode;
    onPress?: () => void;
    "aria-label"?: string;
    "aria-current"?: string;
  }) => (
    <button
      onClick={onPress}
      aria-label={props["aria-label"]}
      aria-current={props["aria-current"] === "page" ? "page" : undefined}
    >
      {children}
    </button>
  ),
  Input: ({
    value,
    onValueChange,
    ...props
  }: {
    value: string;
    onValueChange: (value: string) => void;
    "aria-label"?: string;
  }) => (
    <input
      aria-label={props["aria-label"]}
      value={value}
      onChange={(event) => onValueChange(event.target.value)}
    />
  ),
  Select: ({
    label,
    selectedKeys,
    dataSource,
    onSelectionChange,
  }: {
    label: string;
    selectedKeys: string[];
    dataSource: { value: string; label: string }[];
    onSelectionChange: (keys: Set<string>) => void;
  }) => (
    <select
      aria-label={label}
      value={selectedKeys[0]}
      onChange={(event) => onSelectionChange(new Set([event.target.value]))}
    >
      {dataSource.map((item) => (
        <option value={item.value} key={item.value}>
          {item.label}
        </option>
      ))}
    </select>
  ),
  Tooltip: ({ children }: { children: ReactNode }) => children,
}));

function DraftSection({ name, query = "" }: { name: string; query?: string }) {
  const [value, setValue] = useState(name === "Backup" ? "7" : "");
  useEffect(() => {
    state.mounted(name);
    return () => {
      state.unmounted(name);
    };
  }, [name]);
  return (
    <SettingsSection
      title={name}
      query={query}
      items={[
        {
          id: name,
          label: name,
          render: () => (
            <input
              aria-label={`${name} draft`}
              value={value}
              onChange={(event) => setValue(event.target.value)}
            />
          ),
        },
      ]}
    />
  );
}
vi.mock("../components/Functional", () => ({
  default: ({ query }: { query?: string }) => <DraftSection name="General" query={query} />,
}));
vi.mock("../components/StorageSettings", () => ({
  default: ({ query }: { query?: string }) => <DraftSection name="Storage" query={query} />,
}));
vi.mock("../components/VersionBackups", () => ({
  default: ({ query }: { query?: string }) => <DraftSection name="Backup" query={query} />,
}));
vi.mock("../components/RemoteAccess", () => ({
  default: ({ query }: { query?: string }) => <DraftSection name="Pairing" query={query} />,
}));
vi.mock("../components/Others", () => ({
  default: ({ group, query }: { group: string; query?: string }) => (
    <DraftSection name={group === "network" ? "Proxy" : "Parallelism"} query={query} />
  ),
}));
vi.mock("../components/Development", () => ({
  default: ({ query }: { query?: string }) => <DraftSection name="Diagnostics" query={query} />,
}));
vi.mock("../components/AppInfo", () => ({
  default: ({ query }: { query?: string }) => <DraftSection name="Updates" query={query} />,
}));
vi.mock("../components/ContactUs", () => ({
  default: ({ query }: { query?: string }) => <DraftSection name="Contact" query={query} />,
}));
vi.mock("../components/Dependency", () => ({
  default: ({ query, onOpen }: { query?: string; onOpen?: () => void }) => (
    <SettingsSection
      title="BaiduPCS"
      query={query}
      items={[
        {
          id: "dependency",
          label: "BaiduPCS",
          render: () => <button onClick={onOpen}>Open dependencies</button>,
        },
      ]}
    />
  ),
}));
vi.mock("@/pages/dependencies", () => ({
  default: ({ embedded, active }: { embedded: boolean; active: boolean }) => {
    useEffect(() => {
      state.mounted("DependencyManager");
      return () => {
        state.unmounted("DependencyManager");
      };
    }, []);
    return (
      <div data-testid="dependency-manager" data-active={active} data-embedded={embedded}>
        Dependency manager
      </div>
    );
  },
}));

function LocationControls() {
  const location = useLocation();
  const navigate = useNavigate();
  return (
    <>
      <output data-testid="location">{location.search}</output>
      <button onClick={() => navigate(-1)}>Back</button>
      <button onClick={() => navigate(1)}>Forward</button>
    </>
  );
}
const renderPage = (url = "/configuration") =>
  render(
    <MemoryRouter initialEntries={[url]}>
      <ConfigurationPage />
      <LocationControls />
    </MemoryRouter>,
  );
const selectGroup = (id: string) =>
  fireEvent.click(screen.getByRole("button", { name: `configuration.workspace.group.${id}` }));
const search = (value: string) =>
  fireEvent.change(screen.getByRole("textbox", { name: "configuration.search.placeholder" }), {
    target: { value },
  });

beforeEach(() => {
  state.mounted.mockReset();
  state.unmounted.mockReset();
  state.appInfo.mockReset().mockResolvedValue({ code: 0, data: {} });
});

describe("configuration workspace", () => {
  it("mounts groups on demand and preserves unsaved backup input and pairing codes across switches", async () => {
    renderPage();
    expect(state.mounted).toHaveBeenCalledWith("General");
    expect(state.mounted).not.toHaveBeenCalledWith("Backup");
    expect(state.mounted).not.toHaveBeenCalledWith("DependencyManager");
    expect(screen.queryByRole("button", { name: "Open dependencies" })).toBeNull();
    selectGroup("storage");
    fireEvent.change(screen.getByRole("textbox", { name: "Backup draft" }), {
      target: { value: "19" },
    });
    selectGroup("remote-access");
    fireEvent.change(screen.getByRole("textbox", { name: "Pairing draft" }), {
      target: { value: "123456" },
    });
    selectGroup("storage");
    expect(screen.getByRole("textbox", { name: "Backup draft" })).toHaveValue("19");
    selectGroup("remote-access");
    expect(screen.getByRole("textbox", { name: "Pairing draft" })).toHaveValue("123456");
    expect(state.unmounted).not.toHaveBeenCalledWith("Backup");
    expect(state.unmounted).not.toHaveBeenCalledWith("Pairing");
  });

  it("searches across groups without mounting full dependency controls, and clearing search restores the selected group", async () => {
    renderPage("/configuration?tab=storage");
    fireEvent.change(screen.getByRole("textbox", { name: "Backup draft" }), {
      target: { value: "23" },
    });
    search("proxy");
    expect(await screen.findByRole("textbox", { name: "Proxy draft" })).toBeInTheDocument();
    expect(screen.queryByRole("textbox", { name: "Backup draft" })).toBeNull();
    expect(state.mounted).not.toHaveBeenCalledWith("DependencyManager");
    search("");
    expect(screen.getByRole("textbox", { name: "Backup draft" })).toHaveValue("23");
    expect(screen.getByTestId("location")).toHaveTextContent("tab=storage");
  });

  it("opens a matching settings group from global results and preserves unrelated query parameters", async () => {
    renderPage("/configuration?tab=storage&ticket=outer");
    search("proxy");
    fireEvent.click(
      await screen.findByRole("button", {
        name: "configuration.workspace.openGroup:configuration.workspace.group.network",
      }),
    );
    expect(screen.getByRole("textbox", { name: "configuration.search.placeholder" })).toHaveValue(
      "",
    );
    expect(screen.getByTestId("location")).toHaveTextContent("tab=network&ticket=outer");
    expect(screen.getByRole("textbox", { name: "Proxy draft" })).toBeInTheDocument();
  });

  it("supports dependency deep links, keeps the embedded manager mounted and removes source filters when moving to another group", async () => {
    renderPage("/configuration?tab=dependency&component=baidu&q=gdown&ticket=outer");
    expect(screen.getByTestId("dependency-manager")).toHaveAttribute("data-embedded", "true");
    expect(screen.getByTestId("dependency-manager")).toHaveAttribute("data-active", "true");
    selectGroup("general");
    expect(screen.getByTestId("dependency-manager")).toHaveAttribute("data-active", "false");
    expect(screen.getByTestId("location")).toHaveTextContent("tab=general&ticket=outer");
    expect(screen.getByTestId("location")).not.toHaveTextContent("component");
    selectGroup("dependency");
    expect(state.mounted.mock.calls.filter(([name]) => name === "DependencyManager")).toHaveLength(
      1,
    );
    search("BaiduPCS");
    fireEvent.click(await screen.findByRole("button", { name: "Open dependencies" }));
    expect(screen.getByRole("textbox", { name: "configuration.search.placeholder" })).toHaveValue(
      "",
    );
    expect(screen.getByTestId("dependency-manager")).toHaveAttribute("data-active", "true");
  });

  it("falls back from an invalid group and follows browser back and forward navigation", async () => {
    renderPage("/configuration?tab=removed&component=stale&q=unused&ticket=outer");
    await waitFor(() =>
      expect(screen.getByTestId("location")).toHaveTextContent("tab=general&ticket=outer"),
    );
    selectGroup("storage");
    selectGroup("network");
    fireEvent.click(screen.getByRole("button", { name: "Back" }));
    expect(await screen.findByRole("textbox", { name: "Backup draft" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Forward" }));
    expect(await screen.findByRole("textbox", { name: "Proxy draft" })).toBeInTheDocument();
  });

  it("offers the same groups through the compact selector and reports truly empty searches", async () => {
    renderPage();
    fireEvent.change(screen.getByRole("combobox", { name: "configuration.workspace.navigation" }), {
      target: { value: "about" },
    });
    expect(screen.getByRole("textbox", { name: "Updates draft" })).toBeInTheDocument();
    search("not-found-anywhere");
    expect(await screen.findByText("configuration.search.noResults")).toBeInTheDocument();
    expect(state.mounted).not.toHaveBeenCalledWith("DependencyManager");
  });
  it("clears old dependency source filters when opening its group from a different global search", async () => {
    renderPage("/configuration?tab=dependency&component=old&q=gdown&ticket=outer");
    search("BaiduPCS");
    fireEvent.click(
      await screen.findByRole("button", {
        name: "configuration.workspace.openGroup:configuration.workspace.group.dependency",
      }),
    );
    expect(screen.getByTestId("location")).toHaveTextContent("tab=dependency&ticket=outer");
    expect(screen.getByTestId("location")).not.toHaveTextContent("component");
    expect(screen.getByTestId("location")).not.toHaveTextContent("q=");
  });
  it("keeps dependencies discoverable through global search without adding a summary to General", async () => {
    renderPage();
    expect(screen.queryByRole("button", { name: "Open dependencies" })).toBeNull();
    search("BaiduPCS");
    expect(await screen.findByRole("button", { name: "Open dependencies" })).toBeInTheDocument();
    expect(
      screen.getByRole("region", { name: "configuration.workspace.group.dependency" }),
    ).toBeInTheDocument();
    expect(state.mounted).not.toHaveBeenCalledWith("DependencyManager");
    search("");
    expect(screen.queryByRole("button", { name: "Open dependencies" })).toBeNull();
  });
});
