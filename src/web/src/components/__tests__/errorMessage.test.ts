import { describe, expect, it } from "vitest";

import { extractErrorMessage } from "../utils";

describe("readable SDK response errors", () => {
  it("preserves parsed API errors instead of displaying an object tag", () => {
    const response = Object.assign(new Response(null, { status: 400 }), {
      error: {
        code: 400,
        message: "Choose a folder inside a mounted storage location. E:/Downloads",
      },
    });

    expect(extractErrorMessage(response)).toBe(response.error.message);
  });

  it("reads problem details and falls back to an HTTP status when the body is unavailable", () => {
    expect(
      extractErrorMessage({ error: { detail: "Storage is unavailable", title: "Bad Request" } }),
    ).toBe("Storage is unavailable");
    expect(
      extractErrorMessage(new Response(null, { status: 503, statusText: "Service Unavailable" })),
    ).toBe("HTTP 503 Service Unavailable");
  });

  it("preserves ordinary network and string errors", () => {
    expect(extractErrorMessage(new TypeError("Failed to fetch"))).toBe("Failed to fetch");
    expect(extractErrorMessage("Network unavailable")).toBe("Network unavailable");
  });
});
