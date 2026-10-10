import { beforeEach, describe, expect, it, vi } from "vitest";

import { DownloadFolderUnavailableError, openDownloadTaskFolder } from "../openDownloadTaskFolder";

import { ResponseCode, ThirdPartyId } from "@/sdk/constants";

const { request, openFileOrDirectory } = vi.hoisted(() => ({
  request: vi.fn(),
  openFileOrDirectory: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({ default: { request, tool: { openFileOrDirectory } } }));

beforeEach(() => {
  request.mockReset();
  openFileOrDirectory.mockReset();
  openFileOrDirectory.mockResolvedValue({ code: ResponseCode.Success });
});

const native = { userSideActionsRunHere: true, showLocation: vi.fn() };

describe("download task folder opening", () => {
  it.each([
    { path: "/downloads/actual-gallery.torrent", openInDirectory: true },
    { path: "/downloads/Category/Gallery/Images", openInDirectory: false },
  ])("opens the recorded target $path instead of the configured root", async (target) => {
    request.mockResolvedValue({ code: ResponseCode.Success, data: target });

    await openDownloadTaskFolder(
      {
        id: 17,
        thirdPartyId: ThirdPartyId.ExHentai,
        downloadPath: "/obsolete-root",
      },
      native,
    );

    expect(request).toHaveBeenCalledExactlyOnceWith({
      path: "/download-task/17/open-target",
      method: "GET",
      format: "json",
      showErrorToast: false,
    });
    expect(openFileOrDirectory).toHaveBeenCalledExactlyOnceWith(target, { showErrorToast: false });
  });

  it("preserves other downloaders' configured-folder behavior", async () => {
    await openDownloadTaskFolder(
      {
        id: 17,
        thirdPartyId: ThirdPartyId.Bilibili,
        downloadPath: "/downloads/videos",
      },
      native,
    );

    expect(request).not.toHaveBeenCalled();
    expect(openFileOrDirectory).toHaveBeenCalledExactlyOnceWith(
      { path: "/downloads/videos" },
      { showErrorToast: false },
    );
  });

  it("does not guess a folder when the task has no available output", async () => {
    request.mockResolvedValue({ code: ResponseCode.NotFound });

    await expect(
      openDownloadTaskFolder({ id: 17, thirdPartyId: ThirdPartyId.ExHentai }, native),
    ).rejects.toBeInstanceOf(DownloadFolderUnavailableError);

    expect(openFileOrDirectory).not.toHaveBeenCalled();
  });

  it("leaves transport failures to the existing API error reporting", async () => {
    const error = new TypeError("Network unavailable");

    request.mockRejectedValue(error);
    await expect(
      openDownloadTaskFolder({ id: 17, thirdPartyId: ThirdPartyId.ExHentai }, native),
    ).rejects.toBe(error);
    expect(openFileOrDirectory).not.toHaveBeenCalled();
  });

  it.each([
    ["/downloads/gallery.torrent", true, "/downloads"],
    ["/downloads/Gallery/Images", false, "/downloads/Gallery/Images"],
    ["C:\\Downloads\\gallery.torrent", true, "C:\\Downloads"],
  ])(
    "shows the server folder for browser callers without launching on Docker: %s",
    async (path, openInDirectory, folder) => {
      request.mockResolvedValue({ code: ResponseCode.Success, data: { path, openInDirectory } });
      const showLocation = vi.fn();

      await openDownloadTaskFolder(
        { id: 17, thirdPartyId: ThirdPartyId.ExHentai },
        { userSideActionsRunHere: false, showLocation },
      );

      expect(showLocation).toHaveBeenCalledExactlyOnceWith(folder);
      expect(openFileOrDirectory).not.toHaveBeenCalled();
    },
  );

  it("shows other downloaders' paths in browser mode", async () => {
    const showLocation = vi.fn();

    await openDownloadTaskFolder(
      { id: 17, thirdPartyId: ThirdPartyId.Bilibili, downloadPath: "/media/videos" },
      {
        userSideActionsRunHere: false,
        showLocation,
      },
    );
    expect(showLocation).toHaveBeenCalledExactlyOnceWith("/media/videos");
    expect(openFileOrDirectory).not.toHaveBeenCalled();
  });

  it("does not report a native operation as successful when it is refused", async () => {
    openFileOrDirectory.mockResolvedValue({
      code: 403,
      message: "No local directory mapping is configured",
    });
    await expect(
      openDownloadTaskFolder(
        { id: 17, thirdPartyId: ThirdPartyId.Bilibili, downloadPath: "/media/videos" },
        native,
      ),
    ).rejects.toThrow("No local directory mapping is configured");
  });
});
