import type { ReactNode } from "react";

import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import Panel from "../components/Panel";

import { IwFsType } from "@/sdk/constants";

const { createDirectory, explorer } = vi.hoisted(() => ({
  createDirectory: vi.fn(),
  explorer: { capabilities: [] as string[] },
}));

vi.mock("@/sdk/BApi", () => ({
  default: { file: { createDirectory } },
}));
vi.mock("@/components/utils", () => ({ buildLogger: () => () => {} }));
vi.mock("@/components/bakaui", () => ({
  Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
  Button: ({ children, onClick, disabled, isDisabled, title }: any) => (
    <button disabled={disabled || isDisabled} title={title} onClick={onClick}>
      {children}
    </button>
  ),
}));
vi.mock("@/components/FileExplorer", async () => {
  const { forwardRef, useEffect, useImperativeHandle, useState } = await import("react");
  const entry = (path: string, type = IwFsType.Directory) => ({
    path,
    type,
    isDirectoryOrDrive: [IwFsType.Directory, IwFsType.Drive].includes(type),
  });

  return {
    FileExplorer: forwardRef<any, any>(
      ({ capabilities, rootPath, onSelected, onInitialized }, ref) => {
        explorer.capabilities = capabilities;
        const [root, setRoot] = useState(entry(rootPath ?? ""));

        useImperativeHandle(ref, () => ({ root }), [root]);
        useEffect(() => onInitialized(), [root]);

        return (
          <>
            <button onClick={() => onSelected([entry("/parent/child")])}>Select folder</button>
            <button onClick={() => onSelected([entry("D:/", IwFsType.Drive)])}>Select drive</button>
            <button onClick={() => onSelected([entry("/parent/photo.jpg", IwFsType.Image)])}>
              Select file
            </button>
            <button onClick={() => onSelected([entry("/parent/one"), entry("/parent/two")])}>
              Select folders
            </button>
            <button onClick={() => onSelected([])}>Clear selection</button>
            <button
              onClick={() => {
                onSelected([]);
                setRoot(entry("/other"));
              }}
            >
              Enter other folder
            </button>
          </>
        );
      },
    ),
  };
});

beforeEach(() => {
  vi.clearAllMocks();
  createDirectory.mockResolvedValue({ code: 0 });
});
afterEach(cleanup);

const newFolderButton = () =>
  screen.getByRole("button", { name: "fileSystemSelector.action.newFolder" });

describe("filesystem picker operations", () => {
  it.each([false, true])(
    "creates inside the selected folder from the root list (multiple=%s)",
    async (multiple) => {
      render(<Panel multiple={multiple} targetType="folder" />);
      expect(newFolderButton()).toBeDisabled();
      fireEvent.click(screen.getByRole("button", { name: "Select folder" }));
      expect(newFolderButton()).toBeEnabled();
      expect(newFolderButton()).toHaveAttribute("title", "/parent/child");
      fireEvent.click(newFolderButton());
      await waitFor(() =>
        expect(createDirectory).toHaveBeenCalledWith(
          { parent: "/parent/child" },
          { showErrorToast: false },
        ),
      );
      expect(explorer.capabilities).toEqual(
        expect.arrayContaining(["rename", "delete", "create-directory"]),
      );
    },
  );

  it("can create in a drive selected from the root list", async () => {
    render(<Panel targetType="folder" />);
    fireEvent.click(screen.getByRole("button", { name: "Select drive" }));
    fireEvent.click(newFolderButton());
    await waitFor(() =>
      expect(createDirectory).toHaveBeenCalledWith({ parent: "D:/" }, { showErrorToast: false }),
    );
  });

  it("keeps folder operations available in a file picker without accepting the folder as a file", async () => {
    render(<Panel targetType="file" />);
    fireEvent.click(screen.getByRole("button", { name: "Select folder" }));
    expect(screen.getByRole("button", { name: "OK" })).toBeDisabled();
    fireEvent.click(newFolderButton());
    await waitFor(() =>
      expect(createDirectory).toHaveBeenCalledWith(
        { parent: "/parent/child" },
        { showErrorToast: false },
      ),
    );
  });

  it("falls back to the current directory for files or cleared selection and follows navigation", async () => {
    render(<Panel startPath="/parent" />);
    expect(newFolderButton()).toHaveAttribute("title", "/parent");
    fireEvent.click(screen.getByRole("button", { name: "Select folder" }));
    expect(newFolderButton()).toHaveAttribute("title", "/parent/child");
    fireEvent.click(screen.getByRole("button", { name: "Clear selection" }));
    expect(newFolderButton()).toHaveAttribute("title", "/parent");
    fireEvent.click(screen.getByRole("button", { name: "Select file" }));
    fireEvent.click(newFolderButton());
    await waitFor(() =>
      expect(createDirectory).toHaveBeenLastCalledWith(
        { parent: "/parent" },
        { showErrorToast: false },
      ),
    );
    fireEvent.click(screen.getByRole("button", { name: "Enter other folder" }));
    expect(newFolderButton()).toHaveAttribute("title", "/other");
  });

  it("does not arbitrarily choose one of several selected folders", () => {
    const view = render(<Panel multiple />);
    fireEvent.click(screen.getByRole("button", { name: "Select folders" }));
    expect(newFolderButton()).toBeDisabled();
    view.unmount();
    render(<Panel multiple startPath="/parent" />);
    fireEvent.click(screen.getByRole("button", { name: "Select folders" }));
    expect(newFolderButton()).toHaveAttribute("title", "/parent");
  });

  it("blocks duplicate creation while pending and allows retry after an API error", async () => {
    let finish!: (value: { code: number; message: string }) => void;

    createDirectory.mockReturnValueOnce(
      new Promise((resolve) => {
        finish = resolve;
      }),
    );
    render(<Panel startPath="/parent" />);
    fireEvent.click(newFolderButton());
    fireEvent.click(newFolderButton());
    expect(createDirectory).toHaveBeenCalledTimes(1);
    expect(newFolderButton()).toBeDisabled();
    await act(async () => finish({ code: 1, message: "Read-only file system" }));
    expect(screen.getByRole("alert")).toHaveTextContent("Read-only file system");
    expect(newFolderButton()).toBeEnabled();
    fireEvent.click(newFolderButton());
    await waitFor(() => expect(screen.queryByRole("alert")).toBeNull());
    expect(createDirectory).toHaveBeenCalledTimes(2);
  });
});
