import type { ReactNode } from "react";

import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import Panel from "../components/Panel";

import FolderSelectorInner from "@/components/FolderSelector/components/FolderSelectorInner";
import { storageError } from "@/stores/userStorage";
import { IwFsType } from "@/sdk/constants";

const { validatePaths, saveRecent, selection } = vi.hoisted(() => ({
  validatePaths: vi.fn(),
  saveRecent: vi.fn(),
  selection: { paths: ["/media/readonly"] },
}));

vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/sdk/BApi", () => ({
  default: {
    file: { validateUserStoragePaths: validatePaths },
    options: { addLatestMovingDestination: saveRecent },
  },
}));
vi.mock("@/components/utils", () => ({ buildLogger: () => () => {} }));
vi.mock("@/components/bakaui", () => ({
  Accordion: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  AccordionItem: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
  Button: ({ children, onClick, disabled, isDisabled }: any) => (
    <button disabled={disabled || isDisabled} onClick={onClick}>
      {children}
    </button>
  ),
}));
vi.mock("@/components/FolderSelector/components/CustomPathSelectorInner", () => ({
  default: ({ onSelect }: { onSelect: (path: string) => void }) => (
    <button onClick={() => onSelect(selection.paths[0])}>Confirm typed path</button>
  ),
}));
vi.mock("@/components/FolderSelector/components/MediaLibraryPathSelectorInner", () => ({
  default: () => null,
}));
vi.mock("@/components/FileExplorer", async () => {
  const { forwardRef } = await import("react");

  return {
    FileExplorer: forwardRef<unknown, any>(({ onSelected }, _ref) => (
      <button
        onClick={() =>
          onSelected(
            selection.paths.map((path) => ({
              path,
              type: IwFsType.Directory,
              readOnly: true,
            })),
          )
        }
      >
        Select readonly folders
      </button>
    )),
  };
});

beforeEach(() => {
  vi.clearAllMocks();
  selection.paths = ["/media/readonly"];
  validatePaths.mockResolvedValue({ code: 0 });
  saveRecent.mockResolvedValue({ code: 0 });
});
afterEach(cleanup);

describe("storage selection confirmation", () => {
  it("translates the storage boundary error while preserving paths and filesystem errors", () => {
    const translate = (key: string) =>
      key === "fileExplorer.storage.pathRejected" ? "请选择挂载目录" : key;

    expect(
      storageError(new Error("Choose a folder inside a mounted storage location. /etc"), translate)
        .message,
    ).toBe("请选择挂载目录 /etc");
    expect(storageError(new Error("Read-only file system: /media/file"), translate).message).toBe(
      "Read-only file system: /media/file",
    );
  });

  it("validates a manual path before saving it or closing and accepts readonly locations", async () => {
    let finish!: (value: { code: number }) => void;

    validatePaths.mockReturnValue(
      new Promise((resolve) => {
        finish = resolve;
      }),
    );
    const onSelect = vi.fn();

    render(<FolderSelectorInner sources={["custom"]} onSelect={onSelect} />);
    fireEvent.click(screen.getByRole("button", { name: "Confirm typed path" }));
    fireEvent.click(screen.getByRole("button", { name: "Confirm typed path" }));
    expect(validatePaths).toHaveBeenCalledTimes(1);
    expect(saveRecent).not.toHaveBeenCalled();
    expect(onSelect).not.toHaveBeenCalled();
    await act(async () => finish({ code: 0 }));
    expect(saveRecent).toHaveBeenCalledWith("/media/readonly");
    expect(onSelect).toHaveBeenCalledWith("/media/readonly");
  });

  it.each([
    ["rejected path", () => Promise.resolve({ code: 1, message: "Outside storage" })],
    ["network failure", () => Promise.reject(new Error("Offline"))],
  ])("keeps manual selection open after %s and allows retry", async (_name, response) => {
    validatePaths.mockImplementationOnce(response);
    const onSelect = vi.fn();

    render(<FolderSelectorInner sources={["custom"]} onSelect={onSelect} />);
    fireEvent.click(screen.getByRole("button", { name: "Confirm typed path" }));
    await screen.findByRole("alert");
    expect(saveRecent).not.toHaveBeenCalled();
    expect(onSelect).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Confirm typed path" }));
    await waitFor(() => expect(onSelect).toHaveBeenCalledWith("/media/readonly"));
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("does not interpret an empty manual path as a selectable filesystem root", async () => {
    selection.paths = [" "];
    const onSelect = vi.fn();

    render(<FolderSelectorInner sources={["custom"]} onSelect={onSelect} />);
    fireEvent.click(screen.getByRole("button", { name: "Confirm typed path" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("fileExplorer.storage.choosePath");
    expect(validatePaths).not.toHaveBeenCalled();
    expect(onSelect).not.toHaveBeenCalled();
  });

  it("revalidates every batch selection and keeps readonly rows selectable on retry", async () => {
    selection.paths = ["/media/readonly", "/media/other"];
    validatePaths.mockResolvedValueOnce({ code: 1, message: "Outside storage" });
    const onMultipleSelected = vi.fn();
    const onCancel = vi.fn();

    render(
      <Panel
        multiple
        targetType="folder"
        onCancel={onCancel}
        onMultipleSelected={onMultipleSelected}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: "Select readonly folders" }));
    fireEvent.click(screen.getByRole("button", { name: "OK" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Outside storage");
    expect(validatePaths).toHaveBeenCalledWith(
      { paths: selection.paths },
      { showErrorToast: false },
    );
    expect(onMultipleSelected).not.toHaveBeenCalled();
    expect(onCancel).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "OK" }));
    await waitFor(() => expect(onMultipleSelected).toHaveBeenCalledTimes(1));
    expect(
      onMultipleSelected.mock.calls[0][0].map((entry: { path: string }) => entry.path),
    ).toEqual(selection.paths);
  });
});
