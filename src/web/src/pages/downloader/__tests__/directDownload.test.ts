import { beforeEach, describe, expect, it, vi } from "vitest";

import { downloadTaskDirectly } from "../directDownload";

import { DownloadTaskActionOnConflict, ResponseCode } from "@/sdk/constants";

const { request } = vi.hoisted(() => ({ request: vi.fn() }));

vi.mock("@/sdk/BApi", () => ({ default: { request } }));

beforeEach(() => {
  request.mockReset();
  request.mockResolvedValue({ code: ResponseCode.Success });
});

describe("direct download request", () => {
  it("targets one task with the server-side option update and start action", async () => {
    await downloadTaskDirectly(17);

    expect(request).toHaveBeenCalledExactlyOnceWith({
      path: "/download-task/17/direct-download",
      method: "POST",
      body: { actionOnConflict: DownloadTaskActionOnConflict.NotSet },
      type: "application/json",
      format: "json",
      showErrorToast: false,
    });
  });

  it.each([DownloadTaskActionOnConflict.StopOthers, DownloadTaskActionOnConflict.Ignore])(
    "passes the user's conflict choice %s to the same task action",
    async (actionOnConflict) => {
      await downloadTaskDirectly(17, actionOnConflict);

      expect(request).toHaveBeenCalledWith(
        expect.objectContaining({
          path: "/download-task/17/direct-download",
          body: { actionOnConflict },
        }),
      );
    },
  );
});
