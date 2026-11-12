import type { ReactNode } from "react";
import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import PythonRuntimeConfiguration from "../PythonRuntimeConfiguration";

const api = vi.hoisted(() => ({
  getPythonRuntimeConfiguration: vi.fn(),
  getPythonRuntimeCandidates: vi.fn(),
  validatePythonRuntimeConfiguration: vi.fn(),
  applyPythonRuntimeConfiguration: vi.fn(),
  checkDependencies: vi.fn(),
  serverId: "server-a",
}));
vi.mock("@/sdk/BApi", () => ({ default: { pythonRuntime: api } }));
vi.mock("@/stores/remoteAccess", () => ({
  useRemoteAccessStore: (select: (value: unknown) => unknown) => select({ serverId: api.serverId }),
}));
vi.mock("../dependencyChecks", () => ({
  useDependencyChecks: () => ({ checkDependencies: api.checkDependencies }),
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({
    children,
    onPress,
    isDisabled,
  }: {
    children: ReactNode;
    onPress: () => void;
    isDisabled?: boolean;
  }) => (
    <button disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Input: ({
    label,
    value,
    onValueChange,
    isDisabled,
  }: {
    label: string;
    value: string;
    onValueChange: (value: string) => void;
    isDisabled?: boolean;
  }) => (
    <label>
      {label}
      <input
        disabled={isDisabled}
        value={value}
        onChange={(event) => onValueChange(event.target.value)}
      />
    </label>
  ),
  Select: ({
    label,
    selectedKeys,
    dataSource,
    onSelectionChange,
    isDisabled,
  }: {
    label: string;
    selectedKeys: string[];
    dataSource: { value: string; label: string }[];
    onSelectionChange: (keys: Set<string>) => void;
    isDisabled?: boolean;
  }) => (
    <label>
      {label}
      <select
        disabled={isDisabled}
        value={selectedKeys[0] ?? ""}
        onChange={(event) => onSelectionChange(new Set([event.target.value]))}
      >
        <option value="" />
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

const runtime = {
  pythonVersion: "3.13.16",
  architecture: "x64",
  executable: "/external/python3",
  sourceFingerprint: "fingerprint",
};
const waitReady = () => screen.findByRole("combobox", { name: "dependencies.python.source" });
beforeEach(() => {
  api.serverId = "server-a";
  api.getPythonRuntimeConfiguration
    .mockReset()
    .mockResolvedValue({ code: 0, data: { configuration: { mode: 0 } } });
  api.getPythonRuntimeCandidates.mockReset().mockResolvedValue({ code: 0, data: [] });
  api.validatePythonRuntimeConfiguration.mockReset().mockResolvedValue({
    code: 0,
    data: { isValid: true, applied: false, configuration: { mode: 0 }, runtime },
  });
  api.applyPythonRuntimeConfiguration.mockReset().mockResolvedValue({
    code: 0,
    data: {
      isValid: true,
      applied: true,
      configuration: { mode: 1, pythonExecutable: "/external/python3" },
      runtime,
    },
  });
  api.checkDependencies.mockReset().mockResolvedValue(undefined);
});

describe("Python source configuration", () => {
  it("validates an external Python without claiming it is applied and retains the active source on failed apply", async () => {
    render(<PythonRuntimeConfiguration />);
    fireEvent.change(await waitReady(), { target: { value: "1" } });
    fireEvent.change(screen.getByLabelText("dependencies.python.executable"), {
      target: { value: "/external/python3" },
    });
    fireEvent.click(screen.getByRole("button", { name: "dependencies.python.validate" }));
    expect(await screen.findByText("dependencies.python.valid")).toBeInTheDocument();
    expect(api.validatePythonRuntimeConfiguration).toHaveBeenCalledWith({
      mode: 1,
      pythonExecutable: "/external/python3",
    });
    expect(screen.queryByText("dependencies.python.applied")).toBeNull();
    api.applyPythonRuntimeConfiguration.mockResolvedValue({
      code: 0,
      data: {
        isValid: false,
        applied: false,
        error: "Python path is unavailable",
        configuration: { mode: 0 },
      },
    });
    fireEvent.click(screen.getByRole("button", { name: "dependencies.python.apply" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Python path is unavailable");
    expect(screen.getByRole("alert")).toHaveTextContent("dependencies.python.unchanged");
    expect(api.checkDependencies).not.toHaveBeenCalled();
  });
  it("shows preparation while applying and only reports the new source after the server confirms application", async () => {
    let complete!: (response: unknown) => void;
    api.applyPythonRuntimeConfiguration.mockImplementation(
      () =>
        new Promise((resolve) => {
          complete = resolve;
        }),
    );
    render(<PythonRuntimeConfiguration />);
    fireEvent.change(await waitReady(), { target: { value: "1" } });
    fireEvent.change(screen.getByLabelText("dependencies.python.executable"), {
      target: { value: "/external/python3" },
    });
    fireEvent.click(screen.getByRole("button", { name: "dependencies.python.apply" }));
    expect(screen.getByText("dependencies.python.applying")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "dependencies.python.apply" })).toBeDisabled();
    await act(async () =>
      complete({
        code: 0,
        data: {
          isValid: true,
          applied: true,
          configuration: { mode: 1, pythonExecutable: "/external/python3" },
          runtime,
        },
      }),
    );
    expect(await screen.findByText("dependencies.python.applied")).toBeInTheDocument();
    expect(screen.getByText("/external/python3")).toBeInTheDocument();
    expect(api.checkDependencies).toHaveBeenCalledWith([
      "cloud-python-component-service",
      "gdown-component-service",
      "gallery-dl-component-service",
    ]);
  });
  it("discovers candidates only when asked and fills structured Conda paths without executing a shell command", async () => {
    api.getPythonRuntimeCandidates.mockResolvedValue({
      code: 0,
      data: [
        {
          label: "Existing Conda",
          configuration: {
            mode: 2,
            condaExecutable: "/conda/bin/conda",
            condaPrefix: "/conda/envs/media",
          },
        },
      ],
    });
    render(<PythonRuntimeConfiguration />);
    await waitReady();
    expect(api.getPythonRuntimeCandidates).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "dependencies.python.discover" }));
    fireEvent.change(await screen.findByLabelText("dependencies.python.candidates"), {
      target: { value: "0" },
    });
    expect(screen.getByLabelText("dependencies.python.condaExecutable")).toHaveValue(
      "/conda/bin/conda",
    );
    expect(screen.getByLabelText("dependencies.python.condaPrefix")).toHaveValue(
      "/conda/envs/media",
    );
    fireEvent.click(screen.getByRole("button", { name: "dependencies.python.validate" }));
    await waitFor(() =>
      expect(api.validatePythonRuntimeConfiguration).toHaveBeenCalledWith({
        mode: 2,
        condaExecutable: "/conda/bin/conda",
        condaPrefix: "/conda/envs/media",
      }),
    );
  });
  it("discards validation results from the previously selected server", async () => {
    let finish!: (response: unknown) => void;
    api.validatePythonRuntimeConfiguration.mockImplementation(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    const view = render(<PythonRuntimeConfiguration />);
    await waitReady();
    fireEvent.click(screen.getByRole("button", { name: "dependencies.python.validate" }));
    api.serverId = "server-b";
    view.rerender(<PythonRuntimeConfiguration />);
    await waitReady();
    await act(async () =>
      finish({
        code: 0,
        data: { isValid: true, applied: false, configuration: { mode: 0 }, runtime },
      }),
    );
    expect(screen.queryByText("dependencies.python.valid")).toBeNull();
    expect(screen.queryByText("/external/python3")).toBeNull();
  });
  it("requires loading the active configuration before applying and allows retry after a load failure", async () => {
    api.getPythonRuntimeConfiguration.mockRejectedValueOnce(new Error("Offline"));
    render(<PythonRuntimeConfiguration />);
    expect(await screen.findByRole("alert")).toHaveTextContent("Offline");
    expect(screen.getByRole("button", { name: "dependencies.python.applyManaged" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "common.action.retry" }));
    await waitReady();
    expect(
      screen.getByRole("button", { name: "dependencies.python.applyManaged" }),
    ).not.toBeDisabled();
  });
  it("can switch to a missing managed environment even after validation says it is not installed", async () => {
    api.getPythonRuntimeConfiguration.mockResolvedValue({
      code: 0,
      data: {
        configuration: { mode: 1, pythonExecutable: "/external/python3" },
        runtime,
      },
    });
    api.validatePythonRuntimeConfiguration.mockResolvedValue({
      code: 0,
      data: { isValid: false, applied: false, error: "Managed Python is not installed" },
    });
    api.applyPythonRuntimeConfiguration.mockResolvedValue({
      code: 0,
      data: { isValid: true, applied: true, configuration: { mode: 0 }, runtime },
    });
    render(<PythonRuntimeConfiguration />);
    fireEvent.change(await waitReady(), { target: { value: "0" } });
    fireEvent.click(screen.getByRole("button", { name: "dependencies.python.validate" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Managed Python is not installed");
    const apply = screen.getByRole("button", { name: "dependencies.python.applyManaged" });
    expect(apply).not.toBeDisabled();
    fireEvent.click(apply);
    expect(await screen.findByText("dependencies.python.applied")).toBeInTheDocument();
    expect(api.applyPythonRuntimeConfiguration).toHaveBeenCalledWith({ mode: 0 });
  });
});
