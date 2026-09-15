import type { ReactNode } from "react";

import { createRoot, type Root } from "react-dom/client";
import { act } from "react-dom/test-utils";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import DownloadResultWorkflowField from "../DownloadResultWorkflowField";

const { searchWorkflows } = vi.hoisted(() => ({ searchWorkflows: vi.fn() }));

vi.mock("@/sdk/BApi", () => ({ default: { workflow: { searchWorkflows } } }));
vi.mock("@/components/bakaui", () => ({
  toast: { success: vi.fn() },
  Select: ({
    dataSource,
    label,
    selectedKeys,
    onSelectionChange,
    isDisabled,
  }: {
    dataSource: { value: string; label: string }[];
    label: string;
    selectedKeys: string[];
    onSelectionChange: (keys: Set<string>) => void;
    isDisabled: boolean;
  }) => (
    <select
      aria-label={label}
      disabled={isDisabled}
      value={selectedKeys[0]}
      onChange={(event) => onSelectionChange(new Set([event.target.value]))}
    >
      {dataSource.map((item) => (
        <option key={item.value} value={item.value}>
          {item.label}
        </option>
      ))}
    </select>
  ),
  Button: ({
    as,
    href,
    children,
    onPress,
    isDisabled,
    "aria-label": label,
  }: {
    as?: string;
    href?: string;
    children: ReactNode;
    onPress?: () => void;
    isDisabled?: boolean;
    "aria-label"?: string;
  }) =>
    as === "a" ? (
      <a href={href}>{children}</a>
    ) : (
      <button aria-label={label} disabled={isDisabled} onClick={onPress}>
        {children}
      </button>
    ),
}));

const workflow = (id: number, overrides = {}) => ({
  id,
  name: `Workflow ${id}`,
  enabled: true,
  triggerKind: "downloader.resultReady",
  ...overrides,
});
let container: HTMLDivElement;
let root: Root;
const picker = () => container.querySelector("select")!;
const alert = () => container.querySelector('[role="alert"]');
const refresh = async () => {
  await act(async () => container.querySelector("button")!.click());
};
const change = async (value: string) => {
  await act(async () => {
    picker().value = value;
    picker().dispatchEvent(new Event("change", { bubbles: true }));
  });
};

beforeEach(() => {
  vi.clearAllMocks();
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});
afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

describe("download result workflow selection", () => {
  it("offers only enabled result workflows and saves explicit selections", async () => {
    searchWorkflows.mockResolvedValue({
      data: [
        workflow(5),
        workflow(6, { enabled: false }),
        workflow(7, { triggerKind: "acquisition.requested" }),
      ],
    });
    const onChange = vi.fn();

    await act(async () =>
      root.render(<DownloadResultWorkflowField value={null} onChange={onChange} />),
    );
    expect(picker()).not.toBeDisabled();
    expect(searchWorkflows).toHaveBeenCalledWith({
      triggerKind: "downloader.resultReady",
      enabledOnly: true,
    });
    expect(container.querySelectorAll("option")).toHaveLength(2);
    expect(onChange).not.toHaveBeenCalled();
    await change("5");
    expect(onChange).toHaveBeenCalledWith(5);

    await act(async () =>
      root.render(<DownloadResultWorkflowField value={5} onChange={onChange} />),
    );
    expect(container.querySelector("a")).toHaveAttribute("href", "#/workflows/editor?id=5");
    await change("none");
    expect(onChange).toHaveBeenLastCalledWith(null);
  });

  it("preserves a missing or disabled saved workflow and can refresh definitions", async () => {
    searchWorkflows
      .mockResolvedValueOnce({ data: [] })
      .mockResolvedValueOnce({ data: [workflow(9)] });
    const onChange = vi.fn();

    await act(async () =>
      root.render(<DownloadResultWorkflowField value={9} onChange={onChange} />),
    );
    expect(alert()).toHaveTextContent("thirdPartyConfig.downloadResultWorkflow.unavailable");
    expect(picker()).toHaveValue("9");
    expect(onChange).not.toHaveBeenCalled();
    await refresh();
    expect(alert()).toBeNull();
    expect(container.querySelector('option[value="9"]')).toHaveTextContent("Workflow 9");
  });

  it("keeps the saved selection on a load failure and lets the user retry", async () => {
    searchWorkflows
      .mockRejectedValueOnce(new Error("offline"))
      .mockResolvedValueOnce({ data: [workflow(3)] });
    const onChange = vi.fn();

    await act(async () =>
      root.render(<DownloadResultWorkflowField value={3} onChange={onChange} />),
    );
    expect(alert()).toHaveTextContent("thirdPartyConfig.downloadResultWorkflow.loadFailed");
    expect(picker()).toBeDisabled();
    expect(onChange).not.toHaveBeenCalled();
    await refresh();
    expect(picker()).not.toBeDisabled();
    expect(picker()).toHaveValue("3");
  });

  it("reports a failed save without changing the configured workflow", async () => {
    searchWorkflows.mockResolvedValue({ data: [workflow(3)] });
    await act(async () =>
      root.render(
        <DownloadResultWorkflowField
          value={3}
          onChange={vi.fn().mockRejectedValue(new Error("rejected"))}
        />,
      ),
    );
    await change("none");
    expect(alert()).toHaveTextContent("thirdPartyConfig.downloadResultWorkflow.saveFailed");
    expect(picker()).toHaveValue("3");
  });
});
