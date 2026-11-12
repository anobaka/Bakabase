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
  it("opens the manual inbox selector with the loaded path and saves only the visible settings", async () => {
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
    expect(createPortal).toHaveBeenCalledExactlyOnceWith(
      FileSystemSelectorModal,
      expect.objectContaining({
        targetType: "folder",
        multiple: false,
        startPath: "/server/pending",
        defaultSelectedPath: "/server/pending",
      }),
    );
    const selection = createPortal.mock.calls[0][1] as DirectorySelection;
    await act(async () => selection.onSelected({ path: "/mnt/downloads/ready" }));
    expect(input("acquisition.setup.inbox.label")).toHaveValue("/mnt/downloads/ready");
    await next();
    expect(container.textContent).toContain("acquisition.setup.drives.label");
    expect(container.textContent).not.toContain("acquisition.setup.library");
    expect(container.textContent).not.toContain("acquisition.setup.template");
    await setInput(input("acquisition.setup.limit.label"), "8");
    await click(button("acquisition.setup.finish"));
    expect(setUpAcquisition).toHaveBeenCalledExactlyOnceWith({
      inboxDirectory: "/mnt/downloads/ready",
      preferredDriveKinds: [],
      autoPurchaseLimit: 8,
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
      inboxDirectory: defaultOptions.inboxDirectory,
      autoPurchaseLimit: defaultOptions.autoPurchaseLimit,
      preferredDriveKinds: [
        AcquisitionDriveKind.Baidu,
        AcquisitionDriveKind.PikPak,
        AcquisitionDriveKind.Mega,
      ],
    });
  });

  it("ignores legacy archive settings without overwriting them or blocking setup", async () => {
    getOptions.mockResolvedValueOnce({
      code: 0,
      data: {
        ...defaultOptions,
        libraryRootDirectory: "/old/resource-library",
        directoryTemplate: "../legacy/{Title}",
      },
    });
    await renderWizard();
    expect(container.textContent).not.toContain("acquisition.setup.library");
    await next();
    expect(container.textContent).not.toContain("acquisition.setup.template");
    expect(button("acquisition.setup.finish")).toBeEnabled();
    await click(button("acquisition.setup.back"));
    expect(input("acquisition.setup.inbox.label")).toHaveValue(defaultOptions.inboxDirectory);
    await next();
    await click(button("acquisition.setup.finish"));
    expect(setUpAcquisition).toHaveBeenCalledExactlyOnceWith({
      inboxDirectory: defaultOptions.inboxDirectory,
      preferredDriveKinds: [],
      autoPurchaseLimit: 5,
    });
    expect(setUpAcquisition.mock.calls[0][0]).not.toHaveProperty("libraryRootDirectory");
    expect(setUpAcquisition.mock.calls[0][0]).not.toHaveProperty("directoryTemplate");
  });

  it("completes a new setup without requiring legacy archive configuration", async () => {
    getOptions.mockResolvedValueOnce({ code: 0, data: {} });
    await renderWizard();
    await setInput(input("acquisition.setup.inbox.label"), "/manual/inbox");
    await next();
    await click(driveButton("baidu"));
    await click(button("acquisition.setup.finish"));
    expect(setUpAcquisition).toHaveBeenCalledExactlyOnceWith({
      inboxDirectory: "/manual/inbox",
      preferredDriveKinds: [AcquisitionDriveKind.Baidu],
      autoPurchaseLimit: 0,
    });
    expect(onDone).toHaveBeenCalledOnce();
  });
});
