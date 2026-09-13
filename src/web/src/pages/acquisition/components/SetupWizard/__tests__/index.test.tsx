import type { ReactNode } from "react";
import type { Root } from "react-dom/client";

import { createRoot } from "react-dom/client";
import { act } from "react-dom/test-utils";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import SetupWizard from "..";

import { FileSystemSelectorModal } from "@/components/FileSystemSelector";
import { AcquisitionDriveKind } from "@/sdk/constants";

const { getOptions, setUpAcquisition, createPortal, toastSuccess } = vi.hoisted(() => ({
  getOptions: vi.fn(),
  setUpAcquisition: vi.fn(),
  createPortal: vi.fn(),
  toastSuccess: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: { acquisition: { getAcquisitionOptions: getOptions, setUpAcquisition } },
}));

vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal }),
}));

vi.mock("@/components/FileSystemSelector", () => ({
  FileSystemSelectorModal: () => null,
}));

vi.mock("@/components/HelpCenter", () => ({ HelpCenterButton: () => null }));

// Preserve native disabled, pressed, input and footer behavior while the real wizard
// owns loaded settings, directory callbacks, validation and the submitted payload.
vi.mock("@/components/bakaui", () => ({
  Button: ({
    children,
    startContent,
    endContent,
    isDisabled,
    onPress,
    "aria-label": label,
    "aria-pressed": pressed,
  }: {
    children?: ReactNode;
    startContent?: ReactNode;
    endContent?: ReactNode;
    isDisabled?: boolean;
    onPress?: () => void;
    "aria-label"?: string;
    "aria-pressed"?: boolean;
  }) => (
    <button
      aria-label={label}
      aria-pressed={pressed}
      disabled={isDisabled}
      type="button"
      onClick={onPress}
    >
      {startContent}
      {children}
      {endContent}
    </button>
  ),
  Input: ({
    label,
    value,
    onValueChange,
    endContent,
    isInvalid,
    errorMessage,
  }: {
    label: string;
    value: string;
    onValueChange: (value: string) => void;
    endContent?: ReactNode;
    isInvalid?: boolean;
    errorMessage?: string;
  }) => (
    <div>
      <input
        aria-invalid={isInvalid}
        aria-label={label}
        value={value}
        onChange={(event) => onValueChange(event.target.value)}
      />
      {endContent}
      {isInvalid && <p role="alert">{errorMessage}</p>}
    </div>
  ),
  NumberInput: ({
    label,
    value,
    onValueChange,
  }: {
    label: string;
    value: number;
    onValueChange: (value: number) => void;
  }) => (
    <input
      aria-label={label}
      type="number"
      value={value}
      onChange={(event) => onValueChange(Number(event.target.value))}
    />
  ),
  Modal: ({
    children,
    footer,
    title,
  }: {
    children: ReactNode;
    footer: ReactNode;
    title: string;
  }) => (
    <div aria-label={title} role="dialog">
      {children}
      {footer}
    </div>
  ),
  toast: { success: toastSuccess },
}));

interface DirectorySelection {
  targetType: string;
  multiple: boolean;
  startPath?: string;
  defaultSelectedPath?: string;
  onSelected: (entry: { path: string }) => void;
}

let container: HTMLDivElement;
let root: Root;
let onDone: ReturnType<typeof vi.fn>;
let onDestroyed: ReturnType<typeof vi.fn>;

const defaultOptions = {
  inboxDirectory: "/server/pending",
  libraryRootDirectory: "/server/library",
  directoryTemplate: "{Title}",
  preferredDriveKinds: [],
  autoPurchaseLimit: 5,
};

function button(label: string) {
  const found = Array.from(container.querySelectorAll("button")).find(
    (element) =>
      element.textContent?.trim() === label || element.getAttribute("aria-label") === label,
  );

  if (!found) throw new Error(`Button not found: ${label}`);

  return found;
}

function input(label: string) {
  const found = container.querySelector<HTMLInputElement>(`input[aria-label="${label}"]`);

  if (!found) throw new Error(`Input not found: ${label}`);

  return found;
}

function driveButton(name: string) {
  const group = container.querySelector('[aria-label="acquisition.setup.drives.label"]');
  const found = Array.from(group?.querySelectorAll("button") ?? []).find((element) =>
    element.textContent?.startsWith(`acquisition.drive.${name}`),
  );

  if (!found) throw new Error(`Drive button not found: ${name}`);

  return found;
}

const click = async (element: HTMLElement) => {
  await act(async () => element.click());
};

const next = () => click(button("acquisition.setup.next"));

const renderWizard = async () => {
  await act(async () => root.render(<SetupWizard onDestroyed={onDestroyed} onDone={onDone} />));
};

async function setInput(element: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!;

  await act(async () => {
    setter.call(element, value);
    element.dispatchEvent(new Event("input", { bubbles: true }));
  });
}

beforeEach(() => {
  (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
  vi.resetAllMocks();
  getOptions.mockResolvedValue({ code: 0, data: defaultOptions });
  setUpAcquisition.mockResolvedValue({ code: 0 });
  onDone = vi.fn();
  onDestroyed = vi.fn();
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
});

describe("SetupWizard", () => {
  it("opens single-folder selectors with asynchronously loaded server paths and saves their selections", async () => {
    let completeLoad!: (response: { code: number; data: typeof defaultOptions }) => void;

    getOptions.mockReturnValueOnce(
      new Promise((resolve) => {
        completeLoad = resolve;
      }),
    );
    await renderWizard();
    expect(input("acquisition.setup.inbox.label")).toHaveValue("");

    await act(async () => completeLoad({ code: 0, data: defaultOptions }));
    expect(input("acquisition.setup.inbox.label")).toHaveValue("/server/pending");
    await click(button("acquisition.setup.inbox.browse"));
    expect(createPortal).toHaveBeenNthCalledWith(
      1,
      FileSystemSelectorModal,
      expect.objectContaining({
        targetType: "folder",
        multiple: false,
        startPath: "/server/pending",
        defaultSelectedPath: "/server/pending",
      }),
    );

    const pendingSelector = createPortal.mock.calls[0][1] as DirectorySelection;

    await act(async () => pendingSelector.onSelected({ path: "/mnt/downloads/ready" }));
    expect(input("acquisition.setup.inbox.label")).toHaveValue("/mnt/downloads/ready");
    await next();
    expect(input("acquisition.setup.library.label")).toHaveValue("/server/library");
    await click(button("acquisition.setup.library.browse"));
    expect(createPortal).toHaveBeenNthCalledWith(
      2,
      FileSystemSelectorModal,
      expect.objectContaining({
        targetType: "folder",
        multiple: false,
        startPath: "/server/library",
        defaultSelectedPath: "/server/library",
      }),
    );

    const librarySelector = createPortal.mock.calls[1][1] as DirectorySelection;

    await act(async () => librarySelector.onSelected({ path: "/mnt/resources" }));
    expect(input("acquisition.setup.library.label")).toHaveValue("/mnt/resources");
    await next();
    await next();
    await click(button("acquisition.setup.finish"));

    expect(setUpAcquisition).toHaveBeenCalledExactlyOnceWith({
      ...defaultOptions,
      inboxDirectory: "/mnt/downloads/ready",
      libraryRootDirectory: "/mnt/resources",
    });
    expect(onDone).toHaveBeenCalledOnce();
    expect(onDestroyed).toHaveBeenCalledOnce();
    expect(toastSuccess).toHaveBeenCalledWith("acquisition.setup.done");
  });

  it("discards Unknown and duplicate preferences, then saves click order with reselected drives last", async () => {
    getOptions.mockResolvedValueOnce({
      code: 0,
      data: {
        ...defaultOptions,
        preferredDriveKinds: [
          AcquisitionDriveKind.Unknown,
          AcquisitionDriveKind.Mega,
          AcquisitionDriveKind.Mega,
          AcquisitionDriveKind.Baidu,
          AcquisitionDriveKind.Unknown,
        ],
      },
    });
    await renderWizard();
    await next();
    await next();
    await next();

    expect(container.textContent).not.toContain("acquisition.drive.unknown");
    expect(driveButton("mega")).toHaveAttribute("aria-pressed", "true");
    expect(driveButton("mega").textContent).toBe("acquisition.drive.mega1");
    expect(driveButton("baidu").textContent).toBe("acquisition.drive.baidu2");

    await click(driveButton("mega"));
    expect(driveButton("mega")).toHaveAttribute("aria-pressed", "false");
    expect(driveButton("baidu").textContent).toBe("acquisition.drive.baidu1");
    await click(driveButton("pikpak"));
    await click(driveButton("mega"));
    expect(driveButton("pikpak").textContent).toBe("acquisition.drive.pikpak2");
    expect(driveButton("mega").textContent).toBe("acquisition.drive.mega3");
    await click(button("acquisition.setup.finish"));

    expect(setUpAcquisition).toHaveBeenCalledExactlyOnceWith({
      ...defaultOptions,
      preferredDriveKinds: [
        AcquisitionDriveKind.Baidu,
        AcquisitionDriveKind.PikPak,
        AcquisitionDriveKind.Mega,
      ],
    });
  });

  it("previews a nested preset and preserves the complete custom relative template when saving", async () => {
    await renderWizard();
    await next();
    await next();
    await click(button("{LeadKind}/{Date}/{Title}"));

    expect(input("acquisition.setup.template.label")).toHaveValue("{LeadKind}/{Date}/{Title}");
    expect(container.querySelector("code")?.textContent).toMatch(
      /^\/server\/library\/SharedPage\/\d{4}-\d{2}-\d{2}\/acquisition\.setup\.template\.exampleTitle$/,
    );

    const template = "Games/{LeadKind}/{Date}/{Title}";

    await setInput(input("acquisition.setup.template.label"), template);
    expect(container.querySelector("code")?.textContent).toContain(
      "/server/library/Games/SharedPage/",
    );
    await next();
    await click(button("acquisition.setup.finish"));

    expect(setUpAcquisition).toHaveBeenCalledExactlyOnceWith({
      ...defaultOptions,
      directoryTemplate: template,
    });
  });

  it.each([
    "/outside/{Title}",
    "C:\\outside\\{Title}",
    "../{Title}",
    "{LeadKind}/../{Title}",
    "{LeadKind}\\..\\{Title}",
  ])("blocks advancing with an absolute or parent-traversing template: %s", async (template) => {
    await renderWizard();
    await next();
    await next();
    await setInput(input("acquisition.setup.template.label"), template);

    expect(input("acquisition.setup.template.label")).toHaveAttribute("aria-invalid", "true");
    expect(container.querySelector('[role="alert"]')).toHaveTextContent(
      "acquisition.setup.template.invalid",
    );
    expect(button("acquisition.setup.next")).toBeDisabled();
    await next();
    expect(input("acquisition.setup.template.label")).toHaveValue(template);
    expect(setUpAcquisition).not.toHaveBeenCalled();

    await setInput(input("acquisition.setup.template.label"), "Games/{Title}");
    expect(button("acquisition.setup.next")).toBeEnabled();
    await next();
    expect(button("acquisition.setup.finish")).toBeEnabled();
  });
});
