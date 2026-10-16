import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import FileSystemEntries from "..";

const mocks = vi.hoisted(() => ({ request: vi.fn(), remote: true }));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
const api = { file: { getChildrenIwFsInfo: mocks.request } };
vi.mock("@/features/management/resourceContext", () => ({
  useResourceApi: () => api,
  useResourceOwner: () => (mocks.remote ? { isLocal: false } : undefined),
}));
vi.mock("../FileSystemEntry", () => ({
  default: ({ entry, onEnterDirectory }: any) => (
    <button onClick={() => onEnterDirectory(entry.path)}>{entry.name}</button>
  ),
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, isDisabled }: any) => (
    <button disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Spinner: () => null,
  Pagination: ({ onChange }: any) => <button onClick={() => onChange(2)}>page 2</button>,
}));

const entries = (start: number, length: number) =>
  Array.from({ length }, (_, index) => ({
    name: `file-${start + index}`,
    path: `/root/file-${start + index}`,
  }));

beforeEach(() => {
  mocks.request.mockReset();
  mocks.remote = true;
});
afterEach(cleanup);

describe("owner resource file pagination", () => {
  it("requests bounded remote pages and appends the exact continuation cursor", async () => {
    mocks.request.mockResolvedValueOnce({
      code: 0,
      data: { entries: entries(1, 36), hasMore: true, nextCursor: "file-36" },
    });
    mocks.request.mockResolvedValueOnce({
      code: 0,
      data: { entries: entries(37, 2), hasMore: false },
    });
    render(<FileSystemEntries path="/root" isFile={false} />);
    await screen.findByText("file-36");
    expect(mocks.request.mock.calls[0][0]).toEqual({
      root: "/root",
      pageSize: 36,
      cursor: undefined,
    });
    await act(async () => fireEvent.click(screen.getByText("management.loadMore")));
    expect(mocks.request.mock.calls[1][0]).toEqual({
      root: "/root",
      pageSize: 36,
      cursor: "file-36",
    });
    expect(screen.getByText("file-37")).toBeTruthy();
    expect(screen.queryByText("file-1")).toBeNull();
    expect(screen.queryByText("management.loadMore")).toBeNull();
  });

  it("retains local pagination and displays the second page instead of an empty slice", async () => {
    mocks.remote = false;
    mocks.request.mockResolvedValue({ code: 0, data: { entries: entries(1, 40) } });
    render(<FileSystemEntries path="/root" isFile={false} />);
    await screen.findByText("file-36");
    expect(mocks.request.mock.calls[0][0]).toEqual({ root: "/root" });
    fireEvent.click(screen.getAllByText("page 2")[0]);
    expect(screen.getByText("file-37")).toBeTruthy();
    expect(screen.getByText("file-40")).toBeTruthy();
  });
});
