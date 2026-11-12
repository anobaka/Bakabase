import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import FileSystemSelectorButton from "../components/Button";

const portal = vi.hoisted(() => vi.fn());
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: portal }),
}));
vi.mock("../components/Modal", () => ({ default: () => null }));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, isLoading }: any) => (
    <button disabled={isLoading} onClick={onPress}>
      {children}
    </button>
  ),
  toast: { danger: vi.fn() },
}));
beforeEach(() => vi.clearAllMocks());

it("loads the picker on demand and replaces its button label with the selected path", async () => {
  const selected = vi.fn();
  render(
    <FileSystemSelectorButton
      fileSystemSelectorProps={{ targetType: "folder", multiple: false, onSelected: selected }}
    >
      Choose folder
    </FileSystemSelectorButton>,
  );
  expect(portal).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "Choose folder" }));
  await waitFor(() => expect(portal).toHaveBeenCalledTimes(1));
  await act(async () => portal.mock.calls[0][1].onSelected({ path: "/nas/Downloads" }));
  expect(screen.getByRole("button", { name: "/nas/Downloads" })).toBeVisible();
  expect(selected).toHaveBeenCalledWith({ path: "/nas/Downloads" });
});
