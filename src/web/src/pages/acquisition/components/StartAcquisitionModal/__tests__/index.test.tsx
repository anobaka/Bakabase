import type { ComponentProps, ReactNode } from "react";
import type { AcquisitionRecipeVm } from "../../..";

import { createRoot } from "react-dom/client";
import { act } from "react-dom/test-utils";
import { HeroUIProvider } from "@heroui/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import StartAcquisitionModal from "..";

import ActualModal from "@/components/bakaui/components/Modal";

const { getOptions, createFromUrl, success, danger, started, destroyed } = vi.hoisted(() => ({
  getOptions: vi.fn(),
  createFromUrl: vi.fn(),
  success: vi.fn(),
  danger: vi.fn(),
  started: vi.fn(),
  destroyed: vi.fn(),
}));

// Only activity names are used here; the editor's configuration forms stay outside this test.
vi.mock("@/components/Workflow/Activities", () => ({ getWorkflowActivityUI: () => undefined }));
vi.mock("@/components/Workflow/Triggers", () => ({ getWorkflowTriggerUI: () => undefined }));

vi.mock("@/sdk/BApi", () => ({
  default: {
    acquisition: { getAcquisitionOptions: getOptions, createAcquisitionFromUrl: createFromUrl },
  },
}));

// Keep the production asynchronous modal footer: rejected requests must not
// close the dialog or announce success. Native form controls avoid testing layout.
vi.mock("@/components/bakaui", async () => ({
  Button: (await import("@/components/bakaui/components/Button")).Button,
  Modal: (props: ComponentProps<typeof ActualModal>) => <ActualModal {...props} />,
  Input: ({
    value,
    onValueChange,
    label,
    isInvalid,
    errorMessage,
  }: {
    value: string;
    onValueChange: (value: string) => void;
    label: string;
    isInvalid?: boolean;
    errorMessage?: string;
  }) => (
    <>
      <input
        aria-invalid={isInvalid}
        aria-label={label}
        value={value}
        onChange={(event) => onValueChange(event.target.value)}
      />
      {isInvalid && <span role="alert">{errorMessage}</span>}
    </>
  ),
  Select: ({
    dataSource,
    selectedKeys,
    onSelectionChange,
    label,
    isDisabled,
  }: {
    dataSource: { value: string; label: ReactNode }[];
    selectedKeys: string[];
    onSelectionChange: (keys: Set<string>) => void;
    label: string;
    isDisabled?: boolean;
  }) => (
    <select
      aria-label={label}
      disabled={isDisabled}
      value={selectedKeys[0] ?? ""}
      onChange={(event) => onSelectionChange(new Set([event.target.value]))}
    >
      <option value="">Choose</option>
      {dataSource.map((item) => (
        <option key={item.value} value={item.value}>
          {item.label}
        </option>
      ))}
    </select>
  ),
  toast: { success, danger },
}));

const recipes: AcquisitionRecipeVm[] = [
  {
    definitionId: 17,
    validation: { isValid: true, diagnostics: [] },
    applicableLeadKinds: [2],
    name: "Forum post + cloud drive",
    isBuiltin: true,
    stepKinds: [
      "acquisition.resolveSharedContent",
      "acquisition.waitForInbox",
      "acquisition.materialize",
    ],
  },
  {
    definitionId: 71,
    applicableLeadKinds: [2],
    description: "Wait for supplied files",
    validation: { isValid: true, diagnostics: [] },
    name: "My manual intake",
    isBuiltin: false,
    stepKinds: ["acquisition.waitForInbox", "acquisition.materialize"],
  },
  {
    definitionId: 9,
    applicableLeadKinds: [],
    validation: { isValid: true, diagnostics: [] },
    name: "Mixed direct download",
    isBuiltin: false,
    stepKinds: [
      "acquisition.fetchHttp",
      "acquisition.resolveSharedContent",
      "acquisition.materialize",
    ],
  },
  {
    definitionId: 11,
    applicableLeadKinds: [],
    validation: { isValid: true, diagnostics: [] },
    name: "Resolve without materialization",
    isBuiltin: false,
    stepKinds: ["acquisition.resolveSharedContent"],
  },
];

let container: HTMLDivElement;
let root: ReturnType<typeof createRoot>;

beforeEach(() => {
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  vi.clearAllMocks();
  getOptions.mockReset();
  createFromUrl.mockReset();
  getOptions.mockResolvedValue({ code: 0, data: { recipeByLeadKind: {} } });
  createFromUrl.mockResolvedValue({ code: 0, data: { id: 100 } });
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

async function open(available = recipes) {
  await act(async () =>
    root.render(
      <HeroUIProvider disableAnimation>
        <StartAcquisitionModal recipes={available} onDestroyed={destroyed} onStarted={started} />
      </HeroUIProvider>,
    ),
  );
}

const dialog = () => document.querySelector<HTMLElement>('[role="dialog"]')!;
const input = () => dialog().querySelector<HTMLInputElement>("input")!;
const selector = () => dialog().querySelector<HTMLSelectElement>("select")!;

function button(label = "acquisition.startFromUrl.createTask") {
  const found = [...dialog().querySelectorAll<HTMLButtonElement>("button")].find(
    (item) => item.textContent === label,
  );

  expect(found, label).toBeDefined();

  return found!;
}

async function setUrl(value: string) {
  await act(async () => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input(), value);
    input().dispatchEvent(new Event("input", { bubbles: true }));
  });
}

async function select(value: string) {
  await act(async () => {
    selector().value = value;
    selector().dispatchEvent(new Event("change", { bubbles: true }));
  });
}

async function click(element = button()) {
  await act(async () => element.click());
}

describe("start acquisition from a sharing page", () => {
  it.each(["SharedPage", "2"])(
    "selects the configured %s workflow and submits its visible id with a trimmed URL",
    async (key) => {
      getOptions.mockResolvedValueOnce({
        code: 0,
        data: { recipeByLeadKind: { [key]: "My manual intake" } },
      });
      await open();

      expect(getOptions).toHaveBeenCalledTimes(1);
      expect(selector()).toHaveValue("71");
      expect([...selector().options].map((item) => item.value)).toEqual(["", "17", "71"]);
      expect(dialog()).toHaveTextContent("Wait for supplied files");
      expect(dialog()).toHaveTextContent("workflow.diagnostics.passed");
      await setUrl("  https://example.invalid/posts/123  ");
      await click();

      expect(createFromUrl).toHaveBeenCalledExactlyOnceWith({
        url: "https://example.invalid/posts/123",
        recipeDefinitionId: 71,
      });
      expect(success).toHaveBeenCalledExactlyOnceWith("acquisition.started");
      expect(started).toHaveBeenCalledTimes(1);
      expect(dialog()).toBeNull();
    },
  );

  it("shows generic configuration diagnostics and cannot start an invalid workflow", async () => {
    await open(
      recipes.map((item) =>
        item.definitionId === 17
          ? {
              ...item,
              validation: {
                isValid: false,
                diagnostics: [
                  {
                    code: "missingSetting",
                    severity: "error",
                    message: "Required setting is missing",
                    nodeIndex: 0,
                  },
                ],
              },
            }
          : item,
      ),
    );
    await setUrl("https://example.invalid/post");

    expect(dialog()).toHaveTextContent("Required setting is missing");
    expect(button()).toBeDisabled();
    await click();
    expect(createFromUrl).not.toHaveBeenCalled();
    await select("71");
    expect(button()).toBeEnabled();
  });

  it("disables submission until defaults finish loading", async () => {
    let resolve!: (value: unknown) => void;

    getOptions.mockReturnValueOnce(
      new Promise((done) => {
        resolve = done;
      }),
    );
    await open();
    await setUrl("http://example.invalid/post");

    expect(selector()).toBeDisabled();
    expect(button()).toBeDisabled();
    await click();
    expect(createFromUrl).not.toHaveBeenCalled();
    await act(async () => resolve({ code: 0, data: { recipeByLeadKind: {} } }));
    expect(selector()).toHaveValue("17");
    expect(button()).toBeEnabled();
  });

  it("requires an explicit alternative when the configured default no longer exists", async () => {
    getOptions.mockResolvedValueOnce({
      code: 0,
      data: { recipeByLeadKind: { SharedPage: "Deleted workflow" } },
    });
    await open();
    await setUrl("https://example.invalid/post");

    expect(selector()).toHaveValue("");
    expect(dialog()).toHaveTextContent("acquisition.startFromUrl.defaultMissing");
    expect(button()).toBeDisabled();
    await select("71");
    await click();
    expect(createFromUrl).toHaveBeenCalledExactlyOnceWith({
      url: "https://example.invalid/post",
      recipeDefinitionId: 71,
    });
  });

  it.each(["network", "responseCode", "missingData"])(
    "allows retry after %s default failure without making up a selection",
    async (failure) => {
      if (failure === "network") getOptions.mockRejectedValueOnce(new Error("Offline"));
      else
        getOptions.mockResolvedValueOnce(failure === "responseCode" ? { code: 500 } : { code: 0 });
      await open();
      await setUrl("https://example.invalid/post");

      expect(selector()).toHaveValue("");
      expect(dialog()).toHaveTextContent("acquisition.startFromUrl.defaultLoadFailed");
      expect(button()).toBeDisabled();
      await click(button("acquisition.retry"));
      expect(getOptions).toHaveBeenCalledTimes(2);
      expect(selector()).toHaveValue("17");
      expect(input()).toHaveValue("https://example.invalid/post");
      expect(button()).toBeEnabled();
      expect(createFromUrl).not.toHaveBeenCalled();
    },
  );

  it("allows a deliberate workflow choice after defaults fail, and preserves it through retry", async () => {
    getOptions.mockRejectedValueOnce(new Error("Offline"));
    await open();
    await select("71");
    await setUrl("https://example.invalid/post");

    expect(button()).toBeEnabled();
    await click(button("acquisition.retry"));
    expect(selector()).toHaveValue("71");
    await click();
    expect(createFromUrl).toHaveBeenCalledExactlyOnceWith({
      url: "https://example.invalid/post",
      recipeDefinitionId: 71,
    });
  });

  it("explains when no sharing-page workflow can materialize the result and cannot submit", async () => {
    await open(recipes.slice(2));
    await setUrl("https://example.invalid/post");

    expect(selector()).toHaveValue("");
    expect([...selector().options].map((item) => item.value)).toEqual([""]);
    expect(dialog()).toHaveTextContent("acquisition.startFromUrl.noCompatibleRecipe");
    expect(button()).toBeDisabled();
    await click();
    expect(createFromUrl).not.toHaveBeenCalled();
  });

  it.each([
    "magnet:?xt=urn:btih:0123456789012345678901234567890123456789",
    "file:///tmp/file.zip",
    "A shared post and password",
    "https://example.invalid/path with spaces",
  ])("rejects the non-HTTP-page input %j", async (value) => {
    await open();
    await setUrl(value);

    expect(input()).toHaveAttribute("aria-invalid", "true");
    expect(dialog()).toHaveTextContent("acquisition.sourcePicker.validation.httpUrl");
    expect(button()).toBeDisabled();
    await click();
    expect(createFromUrl).not.toHaveBeenCalled();
  });

  it.each(["responseCode", "network"])(
    "keeps the form and permits retry after a %s creation error",
    async (failure) => {
      // The production footer logs the handled error; avoid obscuring the assertions.
      vi.spyOn(console, "error").mockImplementation(() => {});
      if (failure === "responseCode")
        createFromUrl.mockResolvedValueOnce({ code: 400, message: "Unavailable sharing page" });
      else createFromUrl.mockRejectedValueOnce(new Error("Connection lost"));
      await open();
      await setUrl("https://example.invalid/post");
      await select("71");
      await click();

      expect(dialog()).toBeInTheDocument();
      expect(input()).toHaveValue("https://example.invalid/post");
      expect(selector()).toHaveValue("71");
      expect(button()).toBeEnabled();
      expect(danger).toHaveBeenCalledTimes(1);
      expect(success).not.toHaveBeenCalled();
      expect(started).not.toHaveBeenCalled();
      expect(destroyed).not.toHaveBeenCalled();
      await click();
      expect(createFromUrl).toHaveBeenCalledTimes(2);
      expect(success).toHaveBeenCalledTimes(1);
      expect(started).toHaveBeenCalledTimes(1);
      expect(dialog()).toBeNull();
    },
  );
});
