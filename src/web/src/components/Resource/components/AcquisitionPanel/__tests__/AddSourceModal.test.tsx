import type { ReactNode } from "react";
import type { Root } from "react-dom/client";

import { createRoot } from "react-dom/client";
import { act } from "react-dom/test-utils";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import AddSourceModal from "../AddSourceModal";

import { AcquisitionLeadKind, AcquisitionLeadOrigin } from "@/sdk/constants";

const { addLead, addTorrent, createAcquisition } = vi.hoisted(() => ({
  addLead: vi.fn(),
  addTorrent: vi.fn(),
  createAcquisition: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: {
    resource: { addResourceAcquisitionLead: addLead, addResourceAcquisitionTorrent: addTorrent },
    acquisition: { createAcquisition },
  },
}));

// Keep method selection, validation and saving real; replace only UI primitives.
vi.mock("@/components/bakaui", () => {
  type FieldProps = {
    label: string;
    value: string;
    onValueChange: (value: string) => void;
    isDisabled?: boolean;
    isInvalid?: boolean;
    errorMessage?: string;
    maxLength?: number;
  };

  const field = (multiline: boolean) => {
    const Field = ({
      label,
      value,
      onValueChange,
      isDisabled,
      isInvalid,
      errorMessage,
      maxLength,
    }: FieldProps) => {
      const props = {
        "aria-label": label,
        "aria-invalid": isInvalid,
        disabled: isDisabled,
        maxLength,
        value,
        onChange: (event: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) =>
          onValueChange(event.target.value),
      };

      return (
        <div>
          {multiline ? <textarea {...props} /> : <input {...props} />}
          {isInvalid && <p role="alert">{errorMessage}</p>}
        </div>
      );
    };

    return Field;
  };

  return {
    Button: ({
      children,
      startContent,
      isDisabled,
      isLoading,
      onPress,
      "aria-label": label,
      "aria-pressed": pressed,
    }: {
      children?: ReactNode;
      startContent?: ReactNode;
      isDisabled?: boolean;
      isLoading?: boolean;
      onPress?: () => void;
      "aria-label"?: string;
      "aria-pressed"?: boolean;
    }) => (
      <button
        aria-label={label}
        aria-pressed={pressed}
        disabled={isDisabled || isLoading}
        type="button"
        onClick={onPress}
      >
        {startContent}
        {children}
      </button>
    ),
    Chip: ({ children }: { children?: ReactNode }) => <span>{children}</span>,
    Input: field(false),
    Textarea: field(true),
    Modal: ({
      children,
      footer,
      title,
      visible,
    }: {
      children: ReactNode;
      footer: ReactNode;
      title: string;
      visible: boolean;
    }) =>
      visible ? (
        <div aria-label={title} role="dialog">
          {children}
          {footer}
        </div>
      ) : null,
  };
});

let container: HTMLDivElement;
let root: Root;
let onAdded: ReturnType<typeof vi.fn>;
let onDestroyed: ReturnType<typeof vi.fn>;

const k = (key: string) => `acquisition.sourcePicker.${key}`;

function button(key: string) {
  const found = Array.from(container.querySelectorAll("button")).find(
    (element) => element.getAttribute("aria-label") === k(key) || element.textContent === k(key),
  );

  if (!found) throw new Error(`Button not found: ${key}`);

  return found;
}

function field() {
  const found = container.querySelector<HTMLInputElement | HTMLTextAreaElement>("input, textarea");

  if (!found) throw new Error("No source field is visible");

  return found;
}

const click = async (element: HTMLElement) => {
  await act(async () => element.click());
};

async function type(value: string) {
  const element = field();
  const prototype =
    element instanceof HTMLTextAreaElement
      ? HTMLTextAreaElement.prototype
      : HTMLInputElement.prototype;
  const setter = Object.getOwnPropertyDescriptor(prototype, "value")!.set!;

  await act(async () => {
    setter.call(element, value);
    element.dispatchEvent(new Event("input", { bubbles: true }));
  });
}

const renderModal = async () => {
  await act(async () =>
    root.render(<AddSourceModal resourceId={42} onAdded={onAdded} onDestroyed={onDestroyed} />),
  );
};

beforeEach(() => {
  (globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
  vi.resetAllMocks();
  addLead.mockResolvedValue({ code: 0, data: { id: 9 } });
  addTorrent.mockResolvedValue({ code: 0, data: { id: 10 } });
  onAdded = vi.fn();
  onDestroyed = vi.fn();
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
});

describe("AddSourceModal", () => {
  const chooseFile = async (file: File) => {
    const input = container.querySelector<HTMLInputElement>('input[type="file"]')!;

    await act(async () => {
      Object.defineProperty(input, "files", { value: [file], configurable: true });
      input.dispatchEvent(new Event("change", { bubbles: true }));
    });
  };

  it("uploads torrent metadata without starting a download", async () => {
    await renderModal();
    await click(button("torrent.title"));
    expect(button("save")).toBeDisabled();
    const file = new File(["torrent metadata"], "demo.torrent");

    await chooseFile(file);
    expect(addTorrent).not.toHaveBeenCalled();
    await click(button("save"));
    expect(addTorrent).toHaveBeenCalledExactlyOnceWith(42, { file });
    expect(addLead).not.toHaveBeenCalled();
    expect(createAcquisition).not.toHaveBeenCalled();
    expect(onAdded).toHaveBeenCalledOnce();
  });

  it("saves a torrent URL with its type instead of uploading a file", async () => {
    await renderModal();
    await click(button("torrent.title"));
    await click(button("torrent.url"));
    await type("https://example.com/download?id=42");
    await click(button("save"));
    expect(addLead).toHaveBeenCalledExactlyOnceWith(42, {
      kind: AcquisitionLeadKind.Torrent,
      value: "https://example.com/download?id=42",
      origin: AcquisitionLeadOrigin.User,
      isResolved: false,
    });
    expect(addTorrent).not.toHaveBeenCalled();
    expect(createAcquisition).not.toHaveBeenCalled();
  });

  it.each([0, 4 * 1024 * 1024 + 1])("rejects a torrent of %s bytes before upload", async (size) => {
    await renderModal();
    await click(button("torrent.title"));
    await chooseFile(new File([new Uint8Array(size)], "demo.torrent"));
    expect(button("save")).toBeDisabled();
    expect(container).toHaveTextContent(k("validation.torrentSize"));
    expect(addTorrent).not.toHaveBeenCalled();
  });

  it("retains the chosen torrent after a failed upload so it can be retried", async () => {
    addTorrent.mockRejectedValueOnce(new Error("Upload failed"));
    await renderModal();
    await click(button("torrent.title"));
    const file = new File(["torrent metadata"], "demo.torrent");

    await chooseFile(file);
    await click(button("save"));
    expect(container).toHaveTextContent("Upload failed");
    expect(button("save")).toBeEnabled();
    await click(button("save"));
    expect(addTorrent).toHaveBeenLastCalledWith(42, { file });
    expect(onAdded).toHaveBeenCalledOnce();
  });

  it("requires choosing a method before showing a field or allowing a save", async () => {
    await renderModal();
    expect(container.querySelector("input, textarea")).toBeNull();
    expect(container.querySelector('[aria-pressed="true"]')).toBeNull();
    expect(button("save")).toBeDisabled();
    await click(button("save"));
    expect(addLead).not.toHaveBeenCalled();
    expect(createAcquisition).not.toHaveBeenCalled();
  });

  it.each([
    ["directUrl", AcquisitionLeadKind.DirectUrl, "https://files.example.com/work.zip", "INPUT"],
    ["sharedPage", AcquisitionLeadKind.SharedPage, "https://example.com/posts/42", "INPUT"],
    [
      "sharedDocument",
      AcquisitionLeadKind.SharedDocument,
      "下载地址：https://example.com/work.zip\n密码：example",
      "TEXTAREA",
    ],
    ["magnet", AcquisitionLeadKind.Magnet, `magnet:?xt=urn:btih:${"01234567".repeat(5)}`, "INPUT"],
  ])(
    "saves %s with its selected kind and trimmed input without starting acquisition",
    async (id, kind, value, tag) => {
      await renderModal();
      await click(button(`${id}.title`));
      expect(field().tagName).toBe(tag);
      expect(field()).toHaveAccessibleName(k(`${id}.label`));
      expect(button("save")).toBeDisabled();
      await type(`  ${value}\n`);
      await click(button("save"));

      expect(addLead).toHaveBeenCalledExactlyOnceWith(42, {
        kind,
        value,
        origin: AcquisitionLeadOrigin.User,
        isResolved: false,
      });
      expect(createAcquisition).not.toHaveBeenCalled();
      expect(onAdded).toHaveBeenCalledOnce();
      expect(container.querySelector('[role="dialog"]')).toBeNull();
    },
  );

  it.each([
    ["directUrl", "https://pan.baidu.com/s/example", "sharingPage"],
    ["directUrl", "magnet:?xt=urn:btih:invalid", "httpUrl"],
    ["sharedPage", "file:///srv/work.zip", "httpUrl"],
    ["sharedDocument", "https://example.com/work.zip", "textOnlyUrl"],
    ["sharedDocument", "short", "textTooShort"],
    ["magnet", "magnet:?xt=urn:btih:invalid", "magnet"],
  ])("keeps incompatible %s input out of the API", async (id, value, error) => {
    await renderModal();
    await click(button(`${id}.title`));
    await type(value);
    expect(field()).toHaveAttribute("aria-invalid", "true");
    expect(container.querySelector('[role="alert"]')).toHaveTextContent(k(`validation.${error}`));
    expect(button("save")).toBeDisabled();
    await click(button("save"));
    expect(addLead).not.toHaveBeenCalled();
    expect(onAdded).not.toHaveBeenCalled();
  });

  it.each(["response", "network"])(
    "retains the selected method and input after a %s failure, allowing retry",
    async (failure) => {
      if (failure === "response") {
        addLead.mockResolvedValueOnce({ code: 1, message: "Could not save this source" });
      } else {
        addLead.mockRejectedValueOnce(new Error("Could not save this source"));
      }
      await renderModal();
      await click(button("sharedPage.title"));
      await type("https://example.com/posts/42");
      await click(button("save"));

      expect(container.querySelector('[role="alert"]')).toHaveTextContent(
        "Could not save this source",
      );
      expect(field()).toHaveValue("https://example.com/posts/42");
      expect(button("sharedPage.title")).toHaveAttribute("aria-pressed", "true");
      expect(button("save")).toBeEnabled();
      expect(onAdded).not.toHaveBeenCalled();
      expect(onDestroyed).not.toHaveBeenCalled();
      await click(button("sharedPage.title"));
      expect(field()).toHaveValue("https://example.com/posts/42");
      await click(button("save"));
      expect(addLead).toHaveBeenCalledTimes(2);
      expect(onAdded).toHaveBeenCalledOnce();
      expect(container.querySelector('[role="dialog"]')).toBeNull();
    },
  );
});
