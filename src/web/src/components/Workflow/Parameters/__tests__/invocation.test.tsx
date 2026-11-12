import type { ReactNode } from "react";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import ManualRunModal from "../../ManualRunModal";
import WorkflowParameterSummary from "../WorkflowParameterSummary";
import ParameterBindingsEditor from "../ParameterBindingsEditor";
import ParameterDefinitionsEditor from "../ParameterDefinitionsEditor";

const mocks = vi.hoisted(() => ({ get: vi.fn(), run: vi.fn(), directory: vi.fn() }));
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: { defaultValue?: string }) =>
      /^workflow\.parameters\.(names|values|fields)\./.test(key)
        ? (options?.defaultValue ?? key)
        : key,
  }),
}));
vi.mock("@/sdk/BApi", () => ({
  default: { workflow: { getWorkflow: mocks.get, runWorkflowManually: mocks.run } },
}));
vi.mock("../../Triggers", () => ({ getWorkflowTriggerUI: () => ({}) }));
vi.mock("@/components/FileSystemSelector/components/Button", () => ({
  default: ({ children, fileSystemSelectorProps, ...props }: any) => (
    <button
      aria-label={props["aria-label"]}
      onClick={() => {
        mocks.directory(fileSystemSelectorProps);
        fileSystemSelectorProps.onSelected({ path: "/nas/work" });
      }}
    >
      {fileSystemSelectorProps.defaultSelectedPath || children}
    </button>
  ),
}));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ children, onOk, footer }: any) => (
    <div>
      {children}
      <button disabled={footer?.okProps?.isDisabled} onClick={onOk}>
        Run
      </button>
    </div>
  ),
  Button: ({ children, onPress, isDisabled, ...props }: any) => (
    <button aria-label={props["aria-label"]} disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Input: ({ label, value, onValueChange, isDisabled, type, isInvalid }: any) => (
    <input
      aria-label={label}
      aria-invalid={isInvalid}
      type={type}
      disabled={isDisabled}
      value={value}
      onChange={(event) => onValueChange(event.target.value)}
    />
  ),
  Textarea: ({ label, value, onValueChange, isInvalid }: any) => (
    <textarea
      aria-label={label}
      aria-invalid={isInvalid}
      value={value}
      onChange={(event) => onValueChange(event.target.value)}
    />
  ),
  Switch: ({ children, isSelected, onValueChange, isDisabled }: any) => (
    <label>
      <input
        type="checkbox"
        checked={isSelected ?? false}
        disabled={isDisabled}
        onChange={(event) => onValueChange(event.target.checked)}
      />
      {children}
    </label>
  ),
  Select: ({ label, selectedKeys, dataSource, onSelectionChange, isDisabled }: any) => (
    <select
      aria-label={label}
      disabled={isDisabled}
      value={[...selectedKeys][0] ?? ""}
      onChange={(event) => onSelectionChange(new Set([event.target.value]))}
    >
      <option value="" />
      {dataSource.map((item: any) => (
        <option key={item.value} value={item.value} disabled={item.isDisabled}>
          {item.label}
        </option>
      ))}
    </select>
  ),
  Accordion: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  AccordionItem: ({ children, title }: any) => (
    <section>
      <h3>{title}</h3>
      {children}
    </section>
  ),
  Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
  Spinner: () => <span>loading</span>,
  toast: { success: vi.fn() },
}));

const trigger: any = {
  kind: "fs.manualScan",
  supportsManualRun: true,
  requiresManualPayload: false,
  payloadFields: [],
};
const declared = (changes: Record<string, unknown> = {}) => ({
  key: "destination",
  type: "string",
  required: true,
  sensitive: false,
  displayInRun: false,
  ...changes,
});
beforeEach(() => {
  vi.clearAllMocks();
  mocks.run.mockResolvedValue({ code: 0 });
});

describe("workflow parameter invocation", () => {
  it("blocks a required parameter until provided with the directory picker and sends only this run's override", async () => {
    mocks.get.mockResolvedValue({
      code: 0,
      data: { parameters: [declared({ format: "directory" })] },
    });
    render(<ManualRunModal workflowId={7} workflowName="Test" trigger={trigger} />);
    await screen.findByText("workflow.parameters.requiredWithoutDefault");
    expect(screen.getByRole("button", { name: "Run" })).toBeDisabled();
    fireEvent.click(screen.getByLabelText("workflow.parameters.override"));
    fireEvent.click(screen.getByRole("button", { name: "destination" }));
    expect(screen.getByRole("button", { name: "destination" })).toHaveTextContent("/nas/work");
    fireEvent.click(screen.getByRole("button", { name: "Run" }));
    await waitFor(() =>
      expect(mocks.run).toHaveBeenCalledExactlyOnceWith(7, {
        argsJson: "{}",
        parameterValues: { destination: "/nas/work" },
      }),
    );
    expect(mocks.directory.mock.calls[0][0]).toMatchObject({
      targetType: "folder",
      multiple: false,
    });
  });

  it("retains defaults on the server and does not run after the contract cannot be loaded", async () => {
    mocks.get.mockRejectedValue(new Error("offline"));
    render(<ManualRunModal workflowId={7} workflowName="Test" trigger={trigger} />);
    await screen.findByText("workflow.parameters.loadFailed");
    expect(screen.getByRole("button", { name: "Run" })).toBeDisabled();
    mocks.get.mockResolvedValue({
      code: 0,
      data: { parameters: [declared({ type: "boolean", defaultValue: false })] },
    });
    fireEvent.click(screen.getByRole("button", { name: "common.action.retry" }));
    await screen.findByText("workflow.parameters.useDefault");
    fireEvent.click(screen.getByRole("button", { name: "Run" }));
    await waitFor(() =>
      expect(mocks.run).toHaveBeenCalledWith(7, { argsJson: "{}", parameterValues: {} }),
    );
  });

  it("blocks invalid structured input instead of sending the previous valid value", async () => {
    mocks.get.mockResolvedValue({ code: 0, data: { parameters: [declared({ type: "object" })] } });
    render(<ManualRunModal workflowId={7} workflowName="Test" trigger={trigger} />);
    await screen.findByText("workflow.parameters.requiredWithoutDefault");
    fireEvent.click(screen.getByLabelText("workflow.parameters.override"));
    fireEvent.change(screen.getByLabelText("destination"), { target: { value: "{" } });
    await waitFor(() => expect(screen.getByRole("button", { name: "Run" })).toBeDisabled());
    expect(mocks.run).not.toHaveBeenCalled();
    fireEvent.change(screen.getByLabelText("destination"), {
      target: { value: '{"enabled":true}' },
    });
    await waitFor(() => expect(screen.getByRole("button", { name: "Run" })).toBeEnabled());
  });

  it("shows only server-approved safe values and masks sensitive values even if supplied", () => {
    render(
      <WorkflowParameterSummary
        sources={[
          {
            key: "destination",
            type: "string",
            source: "caller",
            hasValue: true,
            sensitive: false,
            displayValue: "/nas/work",
          },
          {
            key: "secret",
            type: "string",
            source: "override",
            hasValue: true,
            sensitive: true,
            displayValue: "must-not-render",
          },
        ]}
      />,
    );
    expect(screen.getByText("/nas/work")).toBeVisible();
    expect(screen.getByText("workflow.parameters.source.caller")).toBeVisible();
    expect(screen.queryByText("must-not-render")).toBeNull();
    expect(screen.getByText("workflow.parameters.valueHidden")).toBeVisible();
  });

  it("restores fixed configuration by removing only the selected field binding", () => {
    const change = vi.fn();
    render(
      <ParameterBindingsEditor
        configJson={'{"path":"/old"}'}
        parameters={[declared()]}
        bindings={[{ configPath: "/path", parameterKey: "destination" }]}
        onChange={change}
      />,
    );
    fireEvent.change(screen.getByRole("combobox", { name: "path" }), {
      target: { value: "__fixed__" },
    });
    expect(change).toHaveBeenCalledWith([]);
  });

  it("reports malformed default JSON without replacing it with a last valid value", async () => {
    const valid = vi.fn();
    render(
      <ParameterDefinitionsEditor
        parameters={[declared({ type: "object", defaultValue: {} })]}
        onChange={vi.fn()}
        onValidityChange={valid}
      />,
    );
    fireEvent.change(screen.getByLabelText("workflow.parameters.defaultValue"), {
      target: { value: "{" },
    });
    await waitFor(() => expect(valid).toHaveBeenLastCalledWith(false));
  });
});
