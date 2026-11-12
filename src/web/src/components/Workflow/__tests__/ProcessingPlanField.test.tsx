import { useState } from "react";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import ProcessingPlanField, { isCompleteProcessingPlan } from "../ProcessingPlanField";

vi.mock("@/components/Dependency/DependencyManager", () => ({
  default: ({ id }: { id: string }) => <div data-testid="processing-dependency">{id}</div>,
}));

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, values?: { index?: number }) =>
      values?.index ? `${key} ${values.index}` : key,
  }),
}));
vi.mock("@/components/bakaui", async () => ({
  ...(await import("@/test/radioControls")),
  Button: ({ children, isDisabled, onPress, "aria-label": label }: any) => (
    <button aria-label={label} disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Input: ({ label, value, onValueChange, isDisabled, type, endContent }: any) => (
    <><input
      aria-label={label}
      value={value}
      type={type}
      disabled={isDisabled}
      onChange={(event) => onValueChange(event.target.value)}
    />{endContent}</>
  ),
  Textarea: ({ label, value, onValueChange, isDisabled }: any) => (
    <textarea
      aria-label={label}
      value={value}
      disabled={isDisabled}
      onChange={(event) => onValueChange(event.target.value)}
    />
  ),
  Select: ({
    label,
    selectedKeys,
    dataSource,
    onSelectionChange,
    isDisabled,
    disabledKeys = [],
  }: any) => (
    <select
      aria-label={label}
      value={Array.from(selectedKeys)[0] as string}
      disabled={isDisabled}
      onChange={(event) => onSelectionChange(new Set([event.target.value]))}
    >
      {dataSource.map((item: any) => (
        <option key={item.value} value={item.value} disabled={disabledKeys.includes(item.value)}>
          {item.label}
        </option>
      ))}
    </select>
  ),
}));

function Editor({
  initial = "",
  changed = vi.fn(),
  disabled = false,
}: {
  initial?: string;
  changed?: (value: string) => void;
  disabled?: boolean;
}) {
  const [value, setValue] = useState(initial);
  return (
    <ProcessingPlanField
      value={value}
      onChange={(next) => {
        setValue(next);
        changed(next);
      }}
      isDisabled={disabled}
    />
  );
}
const key = (suffix: string) => `workflow.processing.editor.${suffix}`;

it("keeps password encoding independent of filename encoding and offers the managed tool only when needed", () => {
  const changed = vi.fn();
  render(<Editor initial='{"requirement":"required","evidence":["original instructions"],"steps":[{"id":"extract","op":"extractArchive","password":"kept","zipFileNameCodePage":932}]}' changed={changed} />);
  expect(screen.queryByTestId("processing-dependency")).toBeNull();
  fireEvent.change(screen.getByLabelText("workflow.processing.zipPasswordCodePage.label"), { target: { value: "936" } });
  const plan = JSON.parse(changed.mock.lastCall![0]);
  expect(plan.evidence).toEqual(["original instructions"]);
  expect(plan.steps[0]).toMatchObject({ password: "kept", zipFileNameCodePage: 932, zipPasswordCodePage: 936 });
  expect(screen.getByTestId("processing-dependency")).toHaveTextContent("pyzipper-component-service");
  fireEvent.change(screen.getByLabelText("workflow.processing.zipPasswordCodePage.label"), { target: { value: "auto" } });
  expect(JSON.parse(changed.mock.lastCall![0]).steps[0]).not.toHaveProperty("zipPasswordCodePage");
  expect(JSON.parse(changed.mock.lastCall![0]).steps[0].zipFileNameCodePage).toBe(932);
  expect(screen.queryByTestId("processing-dependency")).toBeNull();
});

it("removes incompatible password encoding on parser-mode changes without changing the password", () => {
  const changed = vi.fn();
  render(<Editor initial='{"requirement":"required","steps":[{"id":"extract","op":"extractArchive","zipPasswordCodePage":936,"password":"kept"}]}' changed={changed} />);
  fireEvent.change(screen.getByLabelText("workflow.processing.archiveOpenMode.label"), { target: { value: "1" } });
  expect(JSON.parse(changed.mock.lastCall![0]).steps[0]).not.toHaveProperty("zipPasswordCodePage");
  expect(JSON.parse(changed.mock.lastCall![0]).steps[0].password).toBe("kept");
  expect(screen.queryByLabelText("workflow.processing.zipPasswordCodePage.label")).toBeNull();
});

it("rejects unsupported password encodings without normalizing away their saved JSON", () => {
  const initial = '{"requirement":"required","steps":[{"id":"extract","op":"extractArchive","zipPasswordCodePage":123456}]}';
  const changed = vi.fn();
  render(<Editor initial={initial} changed={changed} />);
  expect(screen.queryByLabelText("workflow.processing.zipPasswordCodePage.label")).toBeNull();
  expect(changed).not.toHaveBeenCalled();
  expect(isCompleteProcessingPlan(initial)).toBe(false);
});

it("requires an exact parser source and preserves a selected embedded member without changing other instructions", () => {
  const changed = vi.fn();
  render(
    <Editor
      initial='{"requirement":"required","evidence":["use parser mode"],"steps":[{"id":"extract","op":"extractArchive","zipFileNameCodePage":932,"password":"kept"}]}'
      changed={changed}
    />,
  );
  fireEvent.change(screen.getByLabelText("workflow.processing.archiveOpenMode.label"), {
    target: { value: "1" },
  });
  expect(screen.queryByLabelText("workflow.processing.zipFileNameCodePage.label")).toBeNull();
  expect(isCompleteProcessingPlan(changed.mock.lastCall![0])).toBe(false);
  fireEvent.change(screen.getByLabelText(key("field.selector")), {
    target: { value: "outer.mp4" },
  });
  fireEvent.change(screen.getByLabelText(key("field.archiveMemberSelector")), {
    target: { value: "inner.zip" },
  });
  const step = JSON.parse(changed.mock.lastCall![0]).steps[0];
  expect(step).toEqual({
    id: "extract",
    op: "extractArchive",
    password: "kept",
    selector: "outer.mp4",
    archiveOpenMode: 1,
    archiveMemberSelector: "inner.zip",
  });
  expect(isCompleteProcessingPlan(changed.mock.lastCall![0])).toBe(true);
  fireEvent.change(screen.getByLabelText(key("field.archiveMemberSelector")), {
    target: { value: "" },
  });
  expect(JSON.parse(changed.mock.lastCall![0]).steps[0]).not.toHaveProperty(
    "archiveMemberSelector",
  );
  expect(isCompleteProcessingPlan(changed.mock.lastCall![0])).toBe(true);
  fireEvent.change(screen.getByLabelText(key("field.archiveMemberSelector")), {
    target: { value: "../inner.zip" },
  });
  expect(isCompleteProcessingPlan(changed.mock.lastCall![0])).toBe(false);
  fireEvent.change(screen.getByLabelText("workflow.processing.archiveOpenMode.label"), {
    target: { value: "auto" },
  });
  expect(JSON.parse(changed.mock.lastCall![0]).steps[0]).not.toHaveProperty(
    "archiveMemberSelector",
  );
  expect(JSON.parse(changed.mock.lastCall![0]).steps[0]).not.toHaveProperty("archiveOpenMode");
  expect(screen.getByLabelText("workflow.processing.zipFileNameCodePage.label")).toHaveValue(
    "auto",
  );
});

it("removes parser options when switching operation and keeps unsupported modes untouched in JSON", () => {
  const changed = vi.fn();
  const view = render(
    <Editor
      initial='{"requirement":"required","steps":[{"id":"extract","op":"extractArchive","selector":"container.bin","archiveOpenMode":1,"archiveMemberSelector":"2.zip"}]}'
      changed={changed}
    />,
  );
  fireEvent.change(screen.getByLabelText(key("operation")), { target: { value: "renameFile" } });
  expect(JSON.parse(changed.mock.lastCall![0]).steps[0]).not.toHaveProperty("archiveOpenMode");
  expect(JSON.parse(changed.mock.lastCall![0]).steps[0]).not.toHaveProperty(
    "archiveMemberSelector",
  );
  view.unmount();
  const initial =
    '{"requirement":"required","steps":[{"id":"extract","op":"extractArchive","archiveOpenMode":999}]}';
  render(<Editor initial={initial} />);
  expect(screen.getByLabelText("workflow.processing.plan")).toHaveValue(initial);
});

it("sets and clears ZIP filename encoding while preserving the rest of the plan", () => {
  const changed = vi.fn();
  const initial = {
    requirement: "required",
    evidence: ["archive names were checked"],
    steps: [
      {
        id: "extract",
        op: "extractArchive",
        input: "download",
        password: "known",
        extractionLayout: "currentDirectory",
        storagePolicy: { sourceCleanup: "keep" },
      },
    ],
  };
  render(<Editor initial={JSON.stringify(initial)} changed={changed} />);
  const encoding = screen.getByLabelText("workflow.processing.zipFileNameCodePage.label");
  expect(encoding).toHaveValue("auto");
  fireEvent.change(encoding, { target: { value: "932" } });
  expect(JSON.parse(changed.mock.lastCall![0])).toEqual({
    ...initial,
    steps: [{ ...initial.steps[0], zipFileNameCodePage: 932 }],
  });
  fireEvent.change(screen.getByLabelText(key("field.selector")), {
    target: { value: "inner.zip" },
  });
  expect(JSON.parse(changed.mock.lastCall![0]).steps[0].zipFileNameCodePage).toBe(932);
  fireEvent.change(encoding, { target: { value: "auto" } });
  expect(JSON.parse(changed.mock.lastCall![0])).toEqual({
    ...initial,
    steps: [{ ...initial.steps[0], selector: "inner.zip" }],
  });
});

it("only shows ZIP encoding for extraction and removes it when the operation changes", () => {
  const changed = vi.fn();
  render(
    <Editor
      initial='{"requirement":"required","steps":[{"id":"extract","op":"extractArchive","zipFileNameCodePage":932}]}'
      changed={changed}
    />,
  );
  expect(screen.getByLabelText("workflow.processing.zipFileNameCodePage.label")).toHaveValue("932");
  fireEvent.change(screen.getByLabelText(key("operation")), {
    target: { value: "renameFile" },
  });
  expect(screen.queryByLabelText("workflow.processing.zipFileNameCodePage.label")).toBeNull();
  expect(JSON.parse(changed.mock.lastCall![0]).steps[0]).not.toHaveProperty("zipFileNameCodePage");
});

it("keeps an unsupported ZIP code page in advanced JSON without changing it", () => {
  const initial =
    '{"requirement":"required","steps":[{"id":"extract","op":"extractArchive","zipFileNameCodePage":123456}]}';
  const changed = vi.fn();
  render(<Editor initial={initial} changed={changed} />);
  expect(screen.getByLabelText("workflow.processing.plan")).toHaveValue(initial);
  expect(changed).not.toHaveBeenCalled();
});

it("allows an extraction step to override and then inherit its workflow node layout", () => {
  const changed = vi.fn();
  render(<Editor changed={changed} />);
  fireEvent.click(screen.getByRole("button", { name: key("operation.extractArchive") }));
  expect(screen.queryByLabelText("workflow.extraction.layout.label")).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: key("stepOptions") }));
  const layout = screen.getByLabelText("workflow.extraction.layout.label");
  expect(layout).toHaveValue("inherit");
  fireEvent.change(layout, { target: { value: "archiveDirectory" } });
  expect(JSON.parse(changed.mock.lastCall![0]).steps[0].extractionLayout).toBe("archiveDirectory");
  fireEvent.change(layout, { target: { value: "inherit" } });
  expect(JSON.parse(changed.mock.lastCall![0]).steps[0]).not.toHaveProperty("extractionLayout");
});

it("edits exact directory prefixes without exposing a filename filter or flattening children", () => {
  const changed = vi.fn();
  render(<Editor changed={changed} />);
  fireEvent.click(screen.getByRole("button", { name: key("operation.rebaseDirectory") }));
  expect(screen.queryByLabelText(key("field.selector"))).not.toBeInTheDocument();
  expect(screen.getByLabelText(key("field.targetDirectory"))).toHaveValue("");
  expect(isCompleteProcessingPlan(changed.mock.lastCall![0])).toBe(false);
  fireEvent.change(screen.getByLabelText(key("field.sourceDirectory")), {
    target: { value: "outer/author" },
  });
  expect(isCompleteProcessingPlan(changed.mock.lastCall![0])).toBe(true);
  expect(JSON.parse(changed.mock.lastCall![0]).steps[0]).toEqual({
    id: "step-1",
    op: "rebaseDirectory",
    input: "download",
    sourceDirectory: "outer/author",
    targetDirectory: "",
  });
  fireEvent.change(screen.getByLabelText(key("field.sourceDirectory")), {
    target: { value: "../escape" },
  });
  expect(isCompleteProcessingPlan(changed.mock.lastCall![0])).toBe(false);
});

it("creates a single extraction step for an existing artifact without requiring JSON", () => {
  const changed = vi.fn();
  render(<Editor changed={changed} />);
  expect(screen.queryByLabelText("workflow.processing.plan")).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: key("operation.extractArchive") }));
  fireEvent.change(screen.getByLabelText(key("field.selector")), {
    target: { value: "inner.zip" },
  });
  fireEvent.change(screen.getByLabelText(key("field.password")), { target: { value: "password" } });
  const plan = JSON.parse(changed.mock.lastCall![0]);
  expect(plan).toEqual({
    requirement: "required",
    evidence: [],
    steps: [
      {
        id: "step-1",
        op: "extractArchive",
        input: "download",
        selector: "inner.zip",
        password: "password",
      },
    ],
  });
  const calls = changed.mock.calls.length;
  fireEvent.click(screen.getByRole("button", { name: key("advanced") }));
  expect(
    JSON.parse((screen.getByLabelText("workflow.processing.plan") as HTMLTextAreaElement).value),
  ).toEqual(plan);
  fireEvent.click(screen.getByRole("button", { name: key("visual") }));
  expect(changed).toHaveBeenCalledTimes(calls);
});

it("preserves combined inputs and unrelated metadata when editing another field", () => {
  const changed = vi.fn();
  const initial = {
    requirement: "required",
    evidence: ["proof"],
    future: { retained: true },
    steps: [
      {
        id: "rename",
        op: "renameExtension",
        input: "download",
        extension: ".zip",
        storagePolicy: { retentionMode: "keep" },
      },
      { id: "extract", op: "extractArchive", inputs: ["download", "rename"], future: 3 },
    ],
  };
  render(<Editor initial={JSON.stringify(initial)} changed={changed} />);
  expect(screen.getByText(key("multipleInputs"))).toBeVisible();
  expect(screen.getAllByRole("button", { name: key("remove") })[0]).toBeDisabled();
  fireEvent.change(screen.getByLabelText(key("field.password")), { target: { value: "correct" } });
  expect(JSON.parse(changed.mock.lastCall![0])).toEqual({
    ...initial,
    steps: [initial.steps[0], { ...initial.steps[1], password: "correct" }],
  });
});

it("offers only earlier steps as inputs and keeps explicit single-input arrays", () => {
  const changed = vi.fn();
  const initial = {
    requirement: "required",
    steps: [
      { id: "first", op: "extractArchive", input: "download" },
      { id: "second", op: "extractArchive", inputs: ["first"], input: "legacy-value" },
    ],
  };
  render(<Editor initial={JSON.stringify(initial)} changed={changed} />);
  for (const options of screen.getAllByRole("button", { name: key("stepOptions") })) fireEvent.click(options);
  const inputs = screen.getAllByLabelText(key("input"));
  expect(within(inputs[0]).queryByRole("option", { name: /step 2/ })).not.toBeInTheDocument();
  fireEvent.change(inputs[1], { target: { value: "download" } });
  expect(JSON.parse(changed.mock.lastCall![0]).steps[1]).toEqual({
    ...initial.steps[1],
    inputs: ["download"],
  });
});

it("retains unsupported JSON and allows returning to the form only after it can be represented", () => {
  const initial =
    '{"requirement":"required","steps":[{"id":"custom","op":"futureOperation"}],"future":true}';
  const changed = vi.fn();
  render(<Editor initial={initial} changed={changed} />);
  expect(screen.getByLabelText("workflow.processing.plan")).toHaveValue(initial);
  expect(screen.getByRole("button", { name: key("visual") })).toBeDisabled();
  expect(changed).not.toHaveBeenCalled();
  fireEvent.change(screen.getByLabelText("workflow.processing.plan"), {
    target: { value: '{"requirement":"notRequired","steps":[],"future":true}' },
  });
  expect(screen.getByRole("radio", { name: key("requirement.notRequired") })).toBeChecked();
});

it("prevents changing a nonempty plan to no processing and disables mutations while saving", () => {
  render(
    <Editor
      disabled
      initial='{"requirement":"required","steps":[{"id":"a","op":"extractArchive"}]}'
    />,
  );
  expect(screen.getByRole("radio", { name: key("requirement.required") })).toBeDisabled();
  expect(screen.getByRole("radio", { name: key("requirement.notRequired") })).toBeDisabled();
  fireEvent.click(screen.getByRole("button", { name: key("stepOptions") }));
  expect(screen.getByLabelText(key("field.password"))).toBeDisabled();
  expect(screen.getByLabelText("workflow.processing.zipFileNameCodePage.label")).toBeDisabled();
  expect(screen.getByRole("button", { name: key("remove") })).toBeDisabled();
  expect(screen.getByRole("button", { name: key("operation.extractArchive") })).toBeDisabled();
});

it("hides operation controls for an explicitly unchanged input", () => {
  render(<Editor initial='{"requirement":"notRequired","steps":[]}' />);
  expect(screen.getByRole("radio", { name: key("requirement.notRequired") })).toBeChecked();
  expect(screen.queryByLabelText(key("addOperation"))).toBeNull();
  expect(screen.queryByRole("button", { name: key("operation.extractArchive") })).toBeNull();
  fireEvent.click(screen.getByRole("radio", { name: key("requirement.required") }));
  expect(screen.getByLabelText(key("addOperation"))).toBeVisible();
});

it("shows new and saved archive passwords as plain text without changing the plan", () => {
  const changed = vi.fn();
  render(<Editor initial='{"requirement":"required","steps":[{"id":"outer","op":"extractArchive","password":"already-saved"}]}' changed={changed} />);
  const password = screen.getByLabelText(key("field.password"));
  expect(password).toHaveValue("already-saved");
  expect(password).toHaveAttribute("type", "text");
  expect(changed).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: key("operation.extractArchive") }));
  expect(screen.getAllByLabelText(key("field.password"))[1]).toHaveAttribute("type", "text");
});

it("requires just the applicable rename or move parameters before submission", () => {
  const valid = (op: string, fields: Record<string, string> = {}) =>
    isCompleteProcessingPlan(JSON.stringify({ requirement: "required", steps: [{ id: "s", op, ...fields }] }));
  expect(valid("renameExtension")).toBe(false);
  expect(valid("renameExtension", { extension: ".tar.gz" })).toBe(true);
  expect(valid("renameFile")).toBe(false);
  expect(valid("renameLiteral", { findText: "prefix", replaceText: "" })).toBe(true);
  expect(valid("moveFile")).toBe(false);
  expect(valid("extractArchive")).toBe(true);
});
