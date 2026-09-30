import { beforeEach, describe, expect, it, vi } from "vitest";

const request = vi.hoisted(() => vi.fn());

vi.mock("@/sdk/BApi", () => ({ default: { request } }));
vi.mock("@/sdk/Api", () => ({ ContentType: { Json: "application/json" } }));
import { movePanelApi, MoveRequestRejected } from "../api";
beforeEach(() => request.mockReset());
describe("typed resource move requests", () => {
  it("includes complete source references and panel origin in preview", async () => {
    const refs = [{ nodeId: "node", libraryEpoch: "library", resourceId: 1 }];

    request.mockResolvedValue({ code: 0, data: { items: [] } });
    await movePanelApi.preview([1], "/destination", refs);
    expect(request).toHaveBeenCalledWith(
      expect.objectContaining({
        path: "/resource-move/preview",
        body: {
          resourceIds: [1],
          resourceRefs: refs,
          destDir: "/destination",
          origin: "move-panel",
        },
      }),
    );
  });
  it("uses the configured client, preserving revision and response data", async () => {
    const options = { revision: 3, autoOverwrite: true, destinations: [] };

    request.mockResolvedValue({ code: 0, data: { ...options, revision: 4 } });
    expect(await movePanelApi.saveOptions(options)).toMatchObject({ revision: 4 });
    expect(request).toHaveBeenCalledWith(
      expect.objectContaining({
        path: "/resource-move/panel-options",
        method: "PUT",
        body: options,
      }),
    );
  });
  it("distinguishes domain/HTTP refusals from unknown transport or server outcomes", async () => {
    request.mockResolvedValueOnce({ code: 409, message: "previewChanged: target exists" });
    await expect(
      movePanelApi.preview([1], "/x", [{ nodeId: "n", libraryEpoch: "e", resourceId: 1 }]),
    ).rejects.toBeInstanceOf(MoveRequestRejected);
    request.mockRejectedValueOnce({ status: 409, error: { message: "revision conflict" } });
    await expect(movePanelApi.options()).rejects.toBeInstanceOf(MoveRequestRejected);
    const network = new TypeError("offline");

    request.mockRejectedValueOnce(network);
    await expect(movePanelApi.options()).rejects.toBe(network);
    const server = { status: 500 };

    request.mockRejectedValueOnce(server);
    await expect(movePanelApi.options()).rejects.toBe(server);
  });
});
