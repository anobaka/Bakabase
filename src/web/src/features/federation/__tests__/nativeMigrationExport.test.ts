import { afterEach, describe, expect, it, vi } from "vitest";

import { clientApi } from "@/core/clientApi";

afterEach(() => vi.unstubAllGlobals());

describe("optional native connection hint export", () => {
  it.each(["saved", "cancelled", "unavailable"])(
    "reads %s without sending a path or document",
    async (outcome) => {
      const fetch = vi
        .fn()
        .mockResolvedValue(new Response(JSON.stringify({ code: 0, data: { outcome } })));

      vi.stubGlobal("fetch", fetch);
      expect(await clientApi.exportMigrationHints()).toEqual({ outcome });
      expect(fetch).toHaveBeenCalledWith("/client/migration-hints/export", { method: "POST" });
    },
  );
  it("allows the browser fallback for an older client without the endpoint", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 404 })));
    expect(await clientApi.exportMigrationHints()).toEqual({ outcome: "unavailable" });
  });
  it.each([400, 403, 500])(
    "does not hide a %s failure behind a browser download",
    async (status) => {
      vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status })));
      await expect(clientApi.exportMigrationHints()).rejects.toThrow(String(status));
    },
  );
});
