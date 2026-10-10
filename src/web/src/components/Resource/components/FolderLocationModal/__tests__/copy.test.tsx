import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import FolderLocationModal from "..";

const mocks = vi.hoisted(() => ({ copy: vi.fn(), success: vi.fn(), danger: vi.fn() }));

vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/core/clipboard", () => ({ copyTextToClipboard: mocks.copy }));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ children }: any) => <div role="dialog">{children}</div>,
  Button: ({ children, onPress }: any) => <button onClick={onPress}>{children}</button>,
  toast: { success: mocks.success, danger: mocks.danger },
}));

beforeEach(() => vi.clearAllMocks());
afterEach(cleanup);

describe("browser download folder location", () => {
  it("copies the actual server path and displays success", async () => {
    mocks.copy.mockResolvedValue(undefined);
    render(<FolderLocationModal path="/media/downloads/book" />);
    await act(async () => fireEvent.click(screen.getByText("resource.folderLocation.copyPath")));
    expect(mocks.copy).toHaveBeenCalledExactlyOnceWith("/media/downloads/book");
    expect(mocks.success).toHaveBeenCalledExactlyOnceWith("resource.folderLocation.copied");
    expect(document.querySelector('[aria-live="polite"]')).toHaveTextContent(
      "resource.folderLocation.copied",
    );
  });

  it("displays a manual copy fallback when the clipboard refuses", async () => {
    mocks.copy.mockRejectedValue(new Error("Clipboard denied"));
    render(<FolderLocationModal path="/media/downloads/book" />);
    await act(async () => fireEvent.click(screen.getByText("resource.folderLocation.copyPath")));
    expect(mocks.danger).toHaveBeenCalledExactlyOnceWith("resource.folderLocation.copyFailed");
    expect(mocks.success).not.toHaveBeenCalled();
    expect(screen.getByLabelText("resource.folderLocation.serverPath")).toHaveValue(
      "/media/downloads/book",
    );
    expect(document.querySelector('[aria-live="polite"]')).toHaveTextContent(
      "resource.folderLocation.copyFailed",
    );
  });
});
