import { beforeEach, describe, expect, it, vi } from "vitest";

import { openDownloadTaskFolder } from "../openDownloadTaskFolder";

import { ResponseCode, ThirdPartyId } from "@/sdk/constants";

const { request, openFileOrDirectory } = vi.hoisted(() => ({
  request: vi.fn(),
  openFileOrDirectory: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({ default: { request, tool: { openFileOrDirectory } } }));

beforeEach(() => {
  request.mockReset();
  openFileOrDirectory.mockReset();
});

describe("download task folder opening", () => {
  it.each([
    { path: "/downloads/actual-gallery.torrent", openInDirectory: true },
    { path: "/downloads/Category/Gallery/Images", openInDirectory: false },
  ])("opens the recorded target $path instead of the configured root", async (target) => {
    request.mockResolvedValue({ code: ResponseCode.Success, data: target });

    await openDownloadTaskFolder({
      id: 17,
      thirdPartyId: ThirdPartyId.ExHentai,
      downloadPath: "/obsolete-root",
    });

    expect(request).toHaveBeenCalledExactlyOnceWith({
      path: "/download-task/17/open-target",
      method: "GET",
      format: "json",
    });
    expect(openFileOrDirectory).toHaveBeenCalledExactlyOnceWith(target);
  });

  it("preserves other downloaders' configured-folder behavior", async () => {
    await openDownloadTaskFolder({
      id: 17,
      thirdPartyId: ThirdPartyId.Bilibili,
      downloadPath: "/downloads/videos",
    });

    expect(request).not.toHaveBeenCalled();
    expect(openFileOrDirectory).toHaveBeenCalledExactlyOnceWith({ path: "/downloads/videos" });
  });

  it("does not guess a folder when the task has no available output", async () => {
    request.mockResolvedValue({ code: ResponseCode.NotFound });

    await openDownloadTaskFolder({ id: 17, thirdPartyId: ThirdPartyId.ExHentai });

    expect(openFileOrDirectory).not.toHaveBeenCalled();
  });

  it("leaves transport failures to the existing API error reporting", async () => {
    const error = new TypeError("Network unavailable");

    request.mockRejectedValue(error);
    await expect(
      openDownloadTaskFolder({ id: 17, thirdPartyId: ThirdPartyId.ExHentai }),
    ).rejects.toBe(error);
    expect(openFileOrDirectory).not.toHaveBeenCalled();
  });
});
