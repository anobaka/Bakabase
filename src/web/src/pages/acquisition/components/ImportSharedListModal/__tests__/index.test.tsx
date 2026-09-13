import type { ReactNode } from "react";
import type { Root } from "react-dom/client";

import { createRoot } from "react-dom/client";
import { act } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ImportSharedListModal from "..";
import { getSharedListTemplate } from "../template";

const { previewSharedList, importSharedList, getAllCollections } = vi.hoisted(() => ({
  previewSharedList: vi.fn(),
  importSharedList: vi.fn(),
  getAllCollections: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: {
    acquisition: { previewSharedList, importSharedList },
    collection: { getAllCollections },
  },
}));

vi.mock("@/components/bakaui", () => ({
  Button: ({
    as,
    children,
    href,
    download,
    onPress,
  }: {
    as?: string;
    children: ReactNode;
    href?: string;
    download?: string;
    onPress?: () => void;
  }) =>
    as === "a" ? (
      <a download={download} href={href}>
        {children}
      </a>
    ) : (
      <button type="button" onClick={onPress}>
        {children}
      </button>
    ),
  Modal: ({
    children,
    footer,
    onOk,
  }: {
    children: ReactNode;
    footer: { okProps: { children: ReactNode; isDisabled: boolean } };
    onOk: () => void;
  }) => (
    <div role="dialog">
      {children}
      <button disabled={footer.okProps.isDisabled} type="button" onClick={onOk}>
        {footer.okProps.children}
      </button>
    </div>
  ),
  Input: ({ value, onValueChange }: { value: string; onValueChange: (value: string) => void }) => (
    <input value={value} onChange={(event) => onValueChange(event.target.value)} />
  ),
  Switch: ({
    children,
    isSelected,
    onValueChange,
  }: {
    children?: ReactNode;
    isSelected: boolean;
    onValueChange: (value: boolean) => void;
  }) => (
    <button aria-pressed={isSelected} type="button" onClick={() => onValueChange(!isSelected)}>
      {children}
    </button>
  ),
  Select: () => null,
  Spinner: () => null,
  Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
  Table: ({ children }: { children: ReactNode }) => <table>{children}</table>,
  TableHeader: ({ children }: { children: ReactNode }) => (
    <thead>
      <tr>{children}</tr>
    </thead>
  ),
  TableColumn: ({ children }: { children: ReactNode }) => <th>{children}</th>,
  TableBody: ({ children }: { children: ReactNode }) => <tbody>{children}</tbody>,
  TableRow: ({ children }: { children: ReactNode }) => <tr>{children}</tr>,
  TableCell: ({ children }: { children: ReactNode }) => <td>{children}</td>,
  toast: { success: vi.fn(), danger: vi.fn() },
}));

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  vi.clearAllMocks();
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
  getAllCollections.mockResolvedValue({ data: [] });
  previewSharedList.mockResolvedValue({
    code: 0,
    data: [
      {
        title: "My resource",
        url: "https://example.com/share/actual",
        password: "p, q",
        lineNumber: 2,
        alreadyKnown: false,
      },
      { title: "Name only", lineNumber: 3, alreadyKnown: false },
    ],
  });
  importSharedList.mockResolvedValue({
    code: 0,
    data: { created: 2, matched: 0, started: 0, problems: [] },
  });
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

const mount = async () => {
  await act(async () => root.render(<ImportSharedListModal />));
};

const button = (token: string) => {
  const found = Array.from(container.querySelectorAll("button")).find(
    (element) => element.textContent === token,
  );

  if (!found) throw new Error(`Button not found: ${token}`);

  return found;
};

const choose = async (file: File) => {
  const input = container.querySelector<HTMLInputElement>('input[type="file"]')!;

  Object.defineProperty(input, "files", { configurable: true, value: [file] });
  await act(async () => input.dispatchEvent(new Event("change", { bubbles: true })));
};

const file = () =>
  new File(["Title,Download URL,Archive password"], "my-resources.csv", { type: "text/csv" });

const titleInput = () =>
  Array.from(container.querySelectorAll("input")).find((input) => input.value === "My resource");

describe("resource list template and preview", () => {
  it("offers a local CSV download without creating or previewing resources", async () => {
    await mount();
    const link = container.querySelector("a")!;

    expect(link.textContent).toBe("acquisition.sharedList.downloadTemplate");
    expect(link.getAttribute("href")).toBe(getSharedListTemplate("en").url);
    expect(link.getAttribute("download")).toBe("resource-list-template.csv");
    expect(container.textContent).toContain("acquisition.sharedList.templateHelp");
    expect(button("acquisition.sharedList.import").disabled).toBe(true);
    expect(previewSharedList).not.toHaveBeenCalled();
    expect(importSharedList).not.toHaveBeenCalled();
  });

  it("keeps upload as a preview and imports only after confirmation, with acquisition off", async () => {
    await mount();
    const uploaded = file();

    await choose(uploaded);
    expect(previewSharedList).toHaveBeenCalledWith({ file: uploaded });
    expect(importSharedList).not.toHaveBeenCalled();
    expect(button("acquisition.sharedList.startAcquiring").getAttribute("aria-pressed")).toBe(
      "false",
    );

    const input = titleInput()!;

    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(
        input,
        "Corrected title",
      );
      input.dispatchEvent(new Event("input", { bubbles: true }));
    });
    await act(async () => button("acquisition.sharedList.import").click());
    expect(importSharedList).toHaveBeenCalledWith({
      rows: [
        {
          title: "Corrected title",
          url: "https://example.com/share/actual",
          password: "p, q",
          lineNumber: 2,
        },
        { title: "Name only", url: undefined, password: undefined, lineNumber: 3 },
      ],
      collectionId: undefined,
      startAcquiring: false,
    });
  });

  it("clears an old preview after a read error and allows choosing the corrected file again", async () => {
    await mount();
    const uploaded = file();

    await choose(uploaded);
    expect(titleInput()).toBeDefined();
    previewSharedList.mockResolvedValueOnce({
      code: 400,
      message: "CSV line 2 has an unclosed quote",
    });
    await choose(uploaded);
    expect(container.querySelector('[role="alert"]')?.textContent).toBe(
      "CSV line 2 has an unclosed quote",
    );
    expect(titleInput()).toBeUndefined();
    expect(button("acquisition.sharedList.import").disabled).toBe(true);

    await choose(uploaded);
    expect(titleInput()).toBeDefined();
    expect(container.querySelector('[role="alert"]')).toBeNull();
    expect(importSharedList).not.toHaveBeenCalled();
  });

  it("selects the Chinese template for supported Chinese locale codes", () => {
    const chinese = getSharedListTemplate("cn");

    expect(chinese.fileName).toBe("资源清单模板.csv");
    expect(getSharedListTemplate("zh-CN")).toEqual(chinese);
    expect(getSharedListTemplate("en").url).not.toBe(chinese.url);
  });
});
