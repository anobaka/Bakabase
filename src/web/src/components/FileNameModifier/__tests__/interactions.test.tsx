import type { ReactNode } from "react";
import type { OperationWithId } from "../useFileNameModifier";

import { useState } from "react";
import { HeroUIProvider } from "@heroui/react";
import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { beforeEach, afterEach, describe, expect, it, vi } from "vitest";

import FileNameModifier from "..";
import OperationCard from "../OperationCard";

import { FileNameModifierOperationType as OperationType } from "@/sdk/constants";

const api = vi.hoisted(() => ({
  preview: vi.fn(),
  modify: vi.fn(),
  portal: vi.fn(),
  navigate: vi.fn(),
  copy: vi.fn(),
  remove: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: {
    fileNameModifier: { previewFileNameModification: api.preview, modifyFileNames: api.modify },
  },
}));
vi.mock("react-router-dom", () => ({ useNavigate: () => api.navigate }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: api.portal }),
}));
vi.mock("@/components/FileSystemSelector", () => ({ FileSystemSelectorModal: () => null }));
vi.mock("@/components/Workflow/CanvasEditor/templates", () => ({
  EDITOR_SEED_STORAGE_KEY: "rename-workflow-test",
}));
vi.mock("../PreviewList", () => ({
  default: ({ results }: { results: { originalPath: string; modifiedPath: string }[] }) => (
    <div data-testid="preview">
      {results.map((result, index) => (
        <div key={index} data-original={result.originalPath}>
          {result.modifiedPath}
        </div>
      ))}
    </div>
  ),
}));
vi.mock("@/components/bakaui", async () => {
  const hero = await import("@heroui/react");

  return {
    ...hero,
    Button: (await import("@/components/bakaui/components/Button")).Button,
    Select: ({
      dataSource,
      ...props
    }: {
      dataSource: { value: string | number; label: string }[];
    }) => (
      <hero.Select {...props}>
        {dataSource.map((item) => (
          <hero.SelectItem key={item.value} textValue={item.label}>
            {item.label}
          </hero.SelectItem>
        ))}
      </hero.Select>
    ),
    Modal: () => null,
    Tooltip: ({ children }: { children: ReactNode }) => <>{children}</>,
  };
});

let container: HTMLDivElement;
let root: Root;
const paths = ["/library/a.jpg"];
const initialRule: OperationWithId = {
  id: "one",
  target: 2,
  operation: OperationType.Replace,
  position: 1,
  positionIndex: 0,
  targetText: "old",
  text: "new",
  deleteCount: 1,
  deleteStartPosition: 0,
  caseType: 2,
  alphabetStartChar: "A",
  alphabetCount: 1,
  dateTimeFormat: "yyyy",
  replaceEntire: false,
  regex: true,
};
let lastRule = initialRule;

function RuleHarness({ disabled = false, errors = "" }: { disabled?: boolean; errors?: string }) {
  const [value, setValue] = useState(initialRule);

  lastRule = value;

  return (
    <OperationCard
      errors={errors}
      index={0}
      isDisabled={disabled}
      operation={value}
      onChange={(next) => setValue({ ...next, id: value.id })}
      onCopy={api.copy}
      onDelete={api.remove}
    />
  );
}
async function render(content: ReactNode) {
  await act(async () => root.render(<HeroUIProvider disableAnimation>{content}</HeroUIProvider>));
}
const button = (name: string) =>
  Array.from(container.querySelectorAll<HTMLButtonElement>("button")).find(
    (item) => item.getAttribute("aria-label") === name || item.textContent?.trim() === name,
  )!;

async function click(name: string) {
  const target = button(name);

  expect(target).toBeTruthy();
  await act(async () => target.click());
}
async function type(element: HTMLInputElement | HTMLTextAreaElement, value: string) {
  await act(async () => {
    const prototype =
      element.tagName === "TEXTAREA" ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;

    Object.getOwnPropertyDescriptor(prototype, "value")!.set!.call(element, value);
    element.dispatchEvent(new Event("input", { bubbles: true }));
  });
}
async function choose(select: HTMLSelectElement, value: number) {
  await act(async () => {
    select.value = String(value);
    select.dispatchEvent(new Event("change", { bubbles: true }));
  });
}
const tick = async (ms = 300) => {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });
};
const textInput = () =>
  Array.from(
    container.querySelectorAll<HTMLInputElement>(
      'input:not([type="hidden"]):not([type="checkbox"])',
    ),
  ).find((item) => item.type !== "number")!;

beforeEach(() => {
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  vi.clearAllMocks();
  vi.useFakeTimers();
  sessionStorage.clear();
  api.preview.mockImplementation(async (input: { filePaths: string[] }) => ({
    code: 0,
    data: input.filePaths.map((path) => path.replace(".jpg", "-new.jpg")),
  }));
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});
afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe("rename workspace interactions", () => {
  it("keeps path edits as a draft until confirmed and cancels without changing the file list", async () => {
    await render(<FileNameModifier initialFilePaths={paths} />);
    await click("FileNameModifier.EditFileList");
    await type(container.querySelector("textarea")!, "/library/other.jpg");
    expect(container.querySelector('[data-original="/library/a.jpg"]')).not.toBeNull();
    await click("FileNameModifier.Cancel");
    expect(container.querySelector('[data-original="/library/a.jpg"]')).not.toBeNull();
    await click("FileNameModifier.EditFileList");
    expect(container.querySelector("textarea")!.value).toBe(paths[0]);
    await type(container.querySelector("textarea")!, " /library/new.jpg \n\n/library/new.jpg");
    await click("FileNameModifier.Deduplicate");
    await click("FileNameModifier.ConfirmPaths");
    expect(container.querySelector('[data-original="/library/new.jpg"]')).not.toBeNull();
    expect(container.querySelector('[data-original="/library/a.jpg"]')).toBeNull();
    expect(api.modify).not.toHaveBeenCalled();
  });

  it("merges file-system selections once, and leaves picker changes in the draft while editing", async () => {
    await render(<FileNameModifier initialFilePaths={paths} />);
    await click("FileNameModifier.AddFromFileSystem");
    await act(async () =>
      api.portal.mock.calls
        .at(-1)![1]
        .onMultipleSelected([{ path: paths[0] }, { path: "/library/b.jpg" }]),
    );
    expect(container.querySelectorAll("[data-original]")).toHaveLength(2);
    await click("FileNameModifier.EditFileList");
    await click("FileNameModifier.AddFromFileSystem");
    await act(async () =>
      api.portal.mock.calls.at(-1)![1].onMultipleSelected([{ path: "/library/c.jpg" }]),
    );
    expect(container.querySelector("textarea")!.value).toContain("/library/c.jpg");
    await click("FileNameModifier.Cancel");
    expect(container.querySelectorAll("[data-original]")).toHaveLength(2);
  });

  it("preserves duplicated rule configuration in workflow seeds and removes local rule IDs", async () => {
    await render(<FileNameModifier initialFilePaths={paths} />);
    await click("FileNameModifier.AddFirstOperation");
    await type(textInput(), "Prefix-");
    await click("fileNameModifier.rules.copy");
    await tick();
    expect(container.querySelectorAll(".operation-card")).toHaveLength(2);
    await click("FileNameModifier.UpgradeToWorkflow");
    const seed = JSON.parse(sessionStorage.getItem("rename-workflow-test")!);
    const rules = JSON.parse(seed.activities[0].configJson).operations;

    expect(rules).toHaveLength(2);
    expect(rules[0].text).toBe("Prefix-");
    expect(rules[1]).toEqual(rules[0]);
    expect(rules[0].id).toBeUndefined();
    expect(seed.activities.map((item: { kind: string }) => item.kind)).toEqual([
      "transform.fs.fileNameOp",
      "transform.text.trim",
      "action.fs.saveName",
    ]);
    expect(api.navigate).toHaveBeenCalledWith("/workflows/editor?seed=1");
  });

  it("disables execution while editing paths and restores only the previous input list", async () => {
    await render(<FileNameModifier initialFilePaths={paths} />);
    await click("FileNameModifier.AddFirstOperation");
    await type(textInput(), "Prefix-");
    await tick();
    expect(button("fileNameModifier.execution.apply")).toBeEnabled();
    await click("FileNameModifier.EditFileList");
    expect(button("fileNameModifier.execution.apply")).toBeDisabled();
    await click("FileNameModifier.Cancel");
    api.modify.mockResolvedValue({
      code: 0,
      data: [{ oldPath: paths[0], newPath: "/library/a-new.jpg", success: true }],
    });
    await click("fileNameModifier.execution.apply");
    expect(api.modify).toHaveBeenCalledOnce();
    expect(container.querySelector('[data-original="/library/a-new.jpg"]')).not.toBeNull();
    await click("FileNameModifier.RestoreOriginalPaths");
    expect(container.querySelector('[data-original="/library/a.jpg"]')).not.toBeNull();
    expect(api.modify).toHaveBeenCalledOnce();
  });

  it("filters the preview without changing the input sequence submitted for execution", async () => {
    const allPaths = [...paths, "/library/unchanged.jpg"];

    api.preview.mockResolvedValue({ code: 0, data: ["/library/a-new.jpg", allPaths[1]] });
    await render(<FileNameModifier initialFilePaths={allPaths} />);
    await click("FileNameModifier.AddFirstOperation");
    await type(textInput(), "Prefix-");
    await tick();
    expect(container.querySelectorAll("[data-original]")).toHaveLength(2);
    const filter = Array.from(
      container.querySelectorAll<HTMLInputElement>('input[type="checkbox"]'),
    ).find((item) =>
      item.closest("label")?.textContent?.includes("fileNameModifier.preview.onlyChanges"),
    )!;

    await act(async () => filter.click());
    expect(container.querySelectorAll("[data-original]")).toHaveLength(1);
    api.modify.mockResolvedValue({
      code: 0,
      data: allPaths.map((path, index) => ({
        oldPath: path,
        newPath: index === 0 ? "/library/a-new.jpg" : path,
        success: true,
      })),
    });
    await click("fileNameModifier.execution.apply");
    expect(api.modify.mock.calls[0][0].filePaths).toEqual(allPaths);
  });
});

describe("shared rule card", () => {
  it("retains hidden settings when collapsing, and keeps validation errors visible", async () => {
    await render(<RuleHarness errors="Fix this rule" />);
    await click("fileNameModifier.rules.collapse");
    expect(container.querySelector("fieldset")).toBeNull();
    expect(container.querySelector('[role="alert"]')).toHaveTextContent("Fix this rule");
    expect(container.textContent).toContain("old → new");
    await click("fileNameModifier.rules.expand");
    expect(lastRule).toEqual(initialRule);
  });

  it("supports all seven operations and all four target scopes", async () => {
    await render(<RuleHarness />);
    for (const target of [1, 2, 3, 4]) {
      await choose(container.querySelectorAll("select")[0], target);
      expect(lastRule.target).toBe(target);
    }
    for (const operation of [1, 2, 3, 4, 5, 6, 7]) {
      await choose(container.querySelectorAll("select")[1], operation);
      expect(lastRule.operation).toBe(operation);
    }
    expect(container.textContent).toContain("fileNameModifier.rules.noParameters");
  });

  it("uses selectable checkbox labels and preserves the regex setting while replacing the whole name", async () => {
    await render(<RuleHarness />);
    let checks = container.querySelectorAll<HTMLInputElement>('input[type="checkbox"]');

    expect(checks[0]).toBeChecked();
    await act(async () => checks[1].closest("label")!.click());
    expect(lastRule.replaceEntire).toBe(true);
    checks = container.querySelectorAll<HTMLInputElement>('input[type="checkbox"]');
    expect(checks[0]).toBeDisabled();
    expect(checks[0]).not.toBeChecked();
    await act(async () => checks[1].closest("label")!.click());
    expect(lastRule.replaceEntire).toBe(false);
    expect(lastRule.regex).toBe(true);
  });

  it("keeps rule controls disabled during execution", async () => {
    await render(<RuleHarness disabled />);
    expect(button("fileNameModifier.rules.copy")).toBeDisabled();
    expect(button("fileNameModifier.rules.delete")).toBeDisabled();
    expect(container.querySelector("fieldset")).toBeDisabled();
    await click("fileNameModifier.rules.copy");
    await click("fileNameModifier.rules.delete");
    expect(api.copy).not.toHaveBeenCalled();
    expect(api.remove).not.toHaveBeenCalled();
  });
});
