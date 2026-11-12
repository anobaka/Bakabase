import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import TailingOperations from "../components/TailingOperations";
import { IwFsType } from "@/sdk/constants";

const mocks = vi.hoisted(() => ({ native: false, open: vi.fn() }));
vi.mock("@/stores/remoteAccess", () => ({ useUserSideActionsRunHere: () => mocks.native }));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/sdk/BApi", () => ({ default: { tool: { openFileOrDirectory: mocks.open } } }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));
vi.mock("../components/ExtractModal", () => ({ default: () => null }));
vi.mock("../components/CreateDirectoryModal", () => ({ default: () => null }));
vi.mock("../components/OperationButton", () => ({
  default: ({ children, onClick, "aria-label": label }: any) => (
    <button aria-label={label} onClick={onClick}>
      {children}
    </button>
  ),
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onClick }: any) => <button onClick={onClick}>{children}</button>,
  Tooltip: ({ children }: any) => <>{children}</>,
}));

const entry = {
  path: "/remote/result",
  type: IwFsType.Directory,
  isDirectory: true,
  isDirectoryOrDrive: true,
} as any;
beforeEach(() => {
  vi.clearAllMocks();
  mocks.native = false;
});

it("omits unsupported OS folder actions in a browser", () => {
  render(<TailingOperations entry={entry} capabilities={["enter-directory"]} />);
  expect(
    screen.queryByRole("button", { name: "fileExplorer.contextMenu.openInFileManager" }),
  ).toBeNull();
  expect(mocks.open).not.toHaveBeenCalled();
});

it("keeps the original server path for a supported desktop or relay action", () => {
  mocks.native = true;
  render(<TailingOperations entry={entry} capabilities={["enter-directory"]} />);
  fireEvent.click(
    screen.getByRole("button", { name: "fileExplorer.contextMenu.openInFileManager" }),
  );
  expect(mocks.open).toHaveBeenCalledWith({ path: "/remote/result", openInDirectory: false });
});
