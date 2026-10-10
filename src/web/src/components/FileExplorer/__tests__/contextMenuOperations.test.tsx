import type { ReactNode } from "react";
import type { Entry } from "@/core/models/FileExplorer/Entry";
import type { EditableFileNameRef } from "../components/EditableFileName";

import { createRef } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ContextMenu from "../components/ContextMenu";
import EditableFileName from "../components/EditableFileName";
import DeleteConfirmationModal from "../components/DeleteConfirmationModal";
import CreateDirectoryModal from "../components/CreateDirectoryModal";

import { EntryStatus } from "@/core/models/FileExplorer/Entry";
import { IwFsType } from "@/sdk/constants";

const { createPortal, renameFile, removeFiles } = vi.hoisted(() => ({
  createPortal: vi.fn(),
  renameFile: vi.fn(),
  removeFiles: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({ default: { file: { renameFile, removeFiles } } }));
vi.mock("@/components/utils", () => ({
  buildLogger: () => () => {},
  useTraceUpdate: () => {},
  splitPathIntoSegments: (path: string) => path.split("/").filter(Boolean),
  createSelection: (input: HTMLInputElement, start: number, end: number) => {
    input.focus();
    input.setSelectionRange(start, end);
  },
  forceFocus: (element: HTMLElement) => element.focus(),
  getFileNameWithoutExtension: (name: string) => name.slice(0, name.lastIndexOf(".")),
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal }),
}));
vi.mock("@szhsin/react-menu", () => ({
  MenuItem: ({ children, onClick }: any) => (
    <button role="menuitem" onClick={onClick}>
      {children}
    </button>
  ),
}));
vi.mock("auto-text-size", () => ({
  AutoTextSize: ({ children }: { children: ReactNode }) => <>{children}</>,
}));
vi.mock("../components/ExtractModal", () => ({ default: () => null }));
vi.mock("../components/WrapModal", () => ({ default: () => null }));
vi.mock("../components/DeleteItemsWithSameNamesModal", () => ({ default: () => null }));
vi.mock("../components/GroupModal", () => ({ default: () => null }));
vi.mock("../components/FileSystemEntryChangeExampleItem", () => ({ default: () => null }));
vi.mock("../components/FileSystemEntryChangeExampleMiscellaneousItem", () => ({
  default: () => null,
}));
vi.mock("@/components/FolderSelector", () => ({ default: () => null }));
vi.mock("@/components/FileNameModifierModal", () => ({ default: () => null }));
vi.mock("@/components/BulkDecompressionToolModal", () => ({ default: () => null }));
vi.mock("@/components/bakaui", async () => {
  const { forwardRef } = await import("react");

  return {
    toast: {},
    Input: forwardRef<HTMLInputElement, any>(({ value, onValueChange, onKeyDown, onBlur }, ref) => (
      <input
        ref={ref}
        value={value}
        onChange={(event) => onValueChange(event.target.value)}
        onKeyDown={onKeyDown}
        onBlur={onBlur}
      />
    )),
    Modal: ({ children, onOk }: any) => (
      <div role="dialog">
        {children}
        <button onClick={onOk}>Confirm deletion</button>
      </div>
    ),
  };
});

const entry = (path: string, type = IwFsType.Image, props: Partial<Entry> = {}) =>
  ({
    path,
    name: path.slice(path.lastIndexOf("/") + 1),
    type,
    isDirectory: type === IwFsType.Directory,
    isDirectoryOrDrive: type === IwFsType.Directory || type === IwFsType.Drive,
    isDrive: type === IwFsType.Drive,
    actions: [],
    status: EntryStatus.Default,
    ...props,
  }) as Entry;

beforeEach(() => {
  vi.clearAllMocks();
  renameFile.mockResolvedValue({ code: 0 });
  removeFiles.mockResolvedValue({ code: 0 });
});
afterEach(cleanup);

describe("filesystem context menu operations", () => {
  it("opens the same inline rename editor as F2 and sends the complete new filename", async () => {
    const ref = createRef<EditableFileNameRef>();
    const selected = entry("/parent/photo.jpg", IwFsType.Image, {
      ref: { beginRename: () => ref.current?.beginRename() } as Entry["ref"],
    });

    render(
      <>
        <EditableFileName ref={ref} isDirectory={false} name={selected.name} path={selected.path} />
        <ContextMenu capabilities={["rename"]} selectedEntries={[selected]} />
      </>,
    );
    fireEvent.click(screen.getByRole("menuitem", { name: "fileExplorer.contextMenu.rename" }));
    const input = screen.getByRole("textbox") as HTMLInputElement;

    expect(input).toHaveFocus();
    expect(input.selectionStart).toBe(0);
    expect(input.selectionEnd).toBe("photo".length);
    fireEvent.change(input, { target: { value: "renamed.jpg" } });
    fireEvent.keyDown(input, { key: "Enter" });
    await waitFor(() =>
      expect(renameFile).toHaveBeenCalledWith({ fullname: selected.path, newName: "renamed.jpg" }),
    );
    await waitFor(() => expect(screen.queryByRole("textbox")).toBeNull());
    fireEvent.keyDown(screen.getByRole("button", { name: "renamed.jpg" }), { key: "F2" });
    expect(screen.getByRole("textbox")).toHaveValue("renamed.jpg");
    fireEvent.keyDown(screen.getByRole("textbox"), { key: "Escape" });
    expect(renameFile).toHaveBeenCalledTimes(1);
  });

  it.each(["no capability", "multiple entries", "drive", "passive", "error"])(
    "omits simple rename for %s",
    (scenario) => {
      const selected = entry("/parent/photo.jpg");
      const entries = [selected];

      if (scenario === "multiple entries") entries.push(entry("/parent/second.jpg"));
      if (scenario === "drive") Object.assign(selected, { isDrive: true });
      if (scenario === "passive") selected.passive = true;
      if (scenario === "error") Object.assign(selected, { status: EntryStatus.Error });
      render(
        <ContextMenu
          capabilities={scenario === "no capability" ? [] : ["rename"]}
          selectedEntries={entries}
        />,
      );
      expect(
        screen.queryByRole("menuitem", { name: "fileExplorer.contextMenu.rename" }),
      ).toBeNull();
    },
  );

  it("keeps rename unavailable through the editor ref when disabled", () => {
    const ref = createRef<EditableFileNameRef>();

    render(<EditableFileName ref={ref} disabled isDirectory name="mounted" path="/mounted" />);
    ref.current?.beginRename();
    fireEvent.keyDown(screen.getByRole("button"), { key: "F2" });
    expect(screen.queryByRole("textbox")).toBeNull();
  });

  it("cancels a changed filename without blur submitting it", () => {
    render(<EditableFileName isDirectory={false} name="photo.jpg" path="/parent/photo.jpg" />);
    fireEvent.keyDown(screen.getByRole("button"), { key: "F2" });
    fireEvent.change(screen.getByRole("textbox"), { target: { value: "cancelled.jpg" } });
    fireEvent.keyDown(screen.getByRole("textbox"), { key: "Escape" });
    expect(screen.getByRole("button", { name: "photo.jpg" })).toBeInTheDocument();
    expect(renameFile).not.toHaveBeenCalled();
  });

  it("does not submit twice when Enter is followed by blur while the rename is pending", async () => {
    let finish!: (value: { code: number }) => void;

    renameFile.mockReturnValueOnce(
      new Promise((resolve) => {
        finish = resolve;
      }),
    );
    render(<EditableFileName isDirectory={false} name="photo.jpg" path="/parent/photo.jpg" />);
    fireEvent.keyDown(screen.getByRole("button"), { key: "F2" });
    const input = screen.getByRole("textbox");

    fireEvent.change(input, { target: { value: "renamed.jpg" } });
    fireEvent.keyDown(input, { key: "Enter" });
    fireEvent.blur(input);
    expect(renameFile).toHaveBeenCalledTimes(1);
    await act(async () => finish({ code: 0 }));
    expect(screen.getByRole("button", { name: "renamed.jpg" })).toBeInTheDocument();
  });

  it("creates in a selected folder without changing the working directory", () => {
    const selected = entry("/parent/child", IwFsType.Directory);

    render(
      <ContextMenu
        capabilities={["create-directory"]}
        root={entry("/parent", IwFsType.Directory)}
        selectedEntries={[selected]}
      />,
    );
    fireEvent.click(
      screen.getByRole("menuitem", { name: "fileExplorer.contextMenu.createNewFolder" }),
    );
    expect(createPortal).toHaveBeenCalledWith(CreateDirectoryModal, { parentPath: selected.path });
  });

  it("requires deletion confirmation and sends both files and directories while excluding navigation entries", async () => {
    const file = entry("/parent/photo.jpg");
    const folder = entry("/parent/child", IwFsType.Directory);
    const drive = entry("D:/", IwFsType.Drive);
    const passive = entry("/parent", IwFsType.Directory, { passive: true });
    const invalid = entry("/missing", IwFsType.Invalid);

    render(
      <ContextMenu
        capabilities={["delete"]}
        selectedEntries={[file, folder, drive, passive, invalid]}
      />,
    );
    fireEvent.click(screen.getByRole("menuitem", { name: "fileExplorer.contextMenu.deleteItems" }));
    expect(removeFiles).not.toHaveBeenCalled();
    expect(createPortal).toHaveBeenCalledWith(DeleteConfirmationModal, {
      entries: [file, folder],
      rootPath: undefined,
      onDeleted: undefined,
    });
    const deleted = vi.fn();

    render(<DeleteConfirmationModal entries={[file, folder]} onDeleted={deleted} />);
    fireEvent.click(screen.getByRole("button", { name: "Confirm deletion" }));
    await waitFor(() =>
      expect(removeFiles).toHaveBeenCalledWith({ paths: [file.path, folder.path] }),
    );
    expect(deleted).toHaveBeenCalledTimes(1);
  });

  it("keeps selection after a rejected deletion", async () => {
    removeFiles.mockResolvedValueOnce({ code: 1, message: "Read-only file system" });
    const deleted = vi.fn();

    render(<DeleteConfirmationModal entries={[entry("/parent/photo.jpg")]} onDeleted={deleted} />);
    fireEvent.click(screen.getByRole("button", { name: "Confirm deletion" }));
    await waitFor(() => expect(removeFiles).toHaveBeenCalledTimes(1));
    expect(deleted).not.toHaveBeenCalled();
  });

  it.each(["drive", "passive", "invalid"])(
    "does not offer deletion for a %s target",
    (scenario) => {
      const selected = entry("/parent", IwFsType.Directory);

      if (scenario === "drive") Object.assign(selected, { isDrive: true });
      if (scenario === "passive") selected.passive = true;
      if (scenario === "invalid") selected.type = IwFsType.Invalid;
      render(<ContextMenu capabilities={["delete"]} selectedEntries={[selected]} />);
      expect(
        screen.queryByRole("menuitem", { name: "fileExplorer.contextMenu.deleteItems" }),
      ).toBeNull();
    },
  );
});
