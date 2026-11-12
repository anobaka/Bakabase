import { beforeEach, describe, expect, it, vi } from "vitest";

import { loadPostParserExportTasks } from "../exportTasks";

const api = vi.hoisted(() => ({ search: vi.fn(), fullFetch: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({
  default: {
    postParser: { searchPostParserTasks: api.search, getAllPostParserTasks: api.fullFetch },
  },
}));
beforeEach(() => vi.clearAllMocks());

describe("explicit full post parser export", () => {
  it("reads all unfiltered records through bounded server pages only after export is requested", async () => {
    api.search.mockImplementation(async ({ pageIndex, pageSize }) => ({
      code: 0,
      data: {
        totalCount: 205,
        items: Array.from(
          { length: Math.min(pageSize, 205 - (pageIndex - 1) * pageSize) },
          (_, index) => ({ id: (pageIndex - 1) * pageSize + index + 1 }),
        ),
      },
    }));
    expect(api.search).not.toHaveBeenCalled();
    const tasks = await loadPostParserExportTasks();
    expect(tasks.map((task) => task.id)).toEqual(
      Array.from({ length: 205 }, (_, index) => index + 1),
    );
    expect(api.search.mock.calls.map(([query]) => query)).toEqual(
      [1, 2, 3].map((pageIndex) => ({ pageIndex, pageSize: 100, status: "all" })),
    );
    expect(api.fullFetch).not.toHaveBeenCalled();
  });

  it("retains the chosen filters, including attention reason, on every explicit export page", async () => {
    api.search.mockResolvedValue({ code: 0, data: { totalCount: 101, items: [{ id: 14 }] } });
    await loadPostParserExportTasks({
      keyword: "sample",
      status: "attention",
      reasonCode: "priceUnknown",
    });
    expect(api.search.mock.calls.map(([query]) => query)).toEqual(
      [1, 2].map((pageIndex) => ({
        keyword: "sample",
        status: "attention",
        reasonCode: "priceUnknown",
        pageIndex,
        pageSize: 100,
      })),
    );
    expect(api.fullFetch).not.toHaveBeenCalled();
  });

  it("fails an incomplete export instead of silently delivering only successful pages", async () => {
    api.search
      .mockResolvedValueOnce({ code: 0, data: { totalCount: 200, items: [{ id: 1 }] } })
      .mockResolvedValueOnce({ code: 500, message: "Export unavailable" });
    await expect(loadPostParserExportTasks()).rejects.toThrow("Export unavailable");
  });
});
