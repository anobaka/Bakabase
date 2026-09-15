import { beforeEach, describe, expect, it, vi } from "vitest";

import { Api } from "../Api";

const notifications = vi.hoisted(() => ({ danger: vi.fn(), clientFailure: vi.fn() }));

vi.mock("@/components/bakaui", () => ({ toast: { danger: notifications.danger } }));
vi.mock("@/components/utils.tsx", () => ({
  buildLogger: () => () => {},
  extractErrorMessage: (error: unknown) => (error instanceof Error ? error.message : String(error)),
}));
vi.mock("@/core/clientFailures", () => ({ reportClientFailure: notifications.clientFailure }));

function pendingFetch() {
  return vi.fn<typeof fetch>().mockImplementation(
    (_input, init) =>
      new Promise<Response>((_resolve, reject) => {
        const signal = init?.signal;
        const rejectAborted = () =>
          reject(signal?.reason ?? new DOMException("Aborted", "AbortError"));

        if (signal?.aborted) rejectAborted();
        else signal?.addEventListener("abort", rejectAborted, { once: true });
      }),
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  notifications.clientFailure.mockReturnValue(false);
});

describe("API request error reporting", () => {
  it("rejects a cancelled workflow check without showing a global error", async () => {
    const client = new Api({ customFetch: pendingFetch() });
    const controller = new AbortController();
    const request = client.workflow.validateSavedWorkflow(5, { signal: controller.signal });
    const rejected = expect(request).rejects.toMatchObject({ name: "AbortError" });

    controller.abort();
    await rejected;
    expect(notifications.danger).not.toHaveBeenCalled();
    expect(notifications.clientFailure).not.toHaveBeenCalled();
  });

  it("handles cancellation reasons that are not DOM AbortError objects", async () => {
    const client = new Api({ customFetch: pendingFetch() });
    const controller = new AbortController();
    const request = client.workflow.validateSavedWorkflow(5, { signal: controller.signal });
    const reason = new Error("A newer configuration check replaced this request");
    const rejected = expect(request).rejects.toBe(reason);

    controller.abort(reason);
    await rejected;
    expect(notifications.danger).not.toHaveBeenCalled();
  });

  it("does not report an already cancelled request", async () => {
    const client = new Api({ customFetch: pendingFetch() });
    const controller = new AbortController();

    controller.abort();
    await expect(
      client.workflow.validateSavedWorkflow(5, { signal: controller.signal }),
    ).rejects.toMatchObject({ name: "AbortError" });
    expect(notifications.danger).not.toHaveBeenCalled();
  });

  it("keeps cancellation through SDK tokens silent while preserving rejection", async () => {
    const client = new Api({ customFetch: pendingFetch() });
    const request = client.workflow.validateSavedWorkflow(5, { cancelToken: "workflow-check" });
    const rejected = expect(request).rejects.toMatchObject({ name: "AbortError" });

    client.abortRequest("workflow-check");
    await rejected;
    expect(notifications.danger).not.toHaveBeenCalled();
  });

  it("does not turn cancellation while reading the response into a successful result", async () => {
    const controller = new AbortController();
    const response = new Response();
    let readingStarted!: () => void;
    const reading = new Promise<void>((resolve) => {
      readingStarted = resolve;
    });

    vi.spyOn(response, "json").mockImplementation(
      () =>
        new Promise((_resolve, reject) => {
          controller.signal.addEventListener("abort", () => reject(controller.signal.reason), {
            once: true,
          });
          readingStarted();
        }),
    );
    const client = new Api({ customFetch: vi.fn<typeof fetch>().mockResolvedValue(response) });
    const request = client.workflow.validateSavedWorkflow(5, { signal: controller.signal });
    const reason = new DOMException("Response read cancelled", "AbortError");
    const rejected = expect(request).rejects.toBe(reason);

    await reading;
    controller.abort(reason);
    await rejected;
    expect(notifications.danger).not.toHaveBeenCalled();
  });

  it("still reports an ordinary network failure exactly once", async () => {
    const error = new TypeError("Failed to fetch");
    const client = new Api({ customFetch: vi.fn<typeof fetch>().mockRejectedValue(error) });

    await expect(client.workflow.validateSavedWorkflow(5)).rejects.toBe(error);
    expect(notifications.danger).toHaveBeenCalledExactlyOnceWith({
      title: "GET /workflow/5/validation failed",
      description: "Failed to fetch",
    });
  });

  it("still reports HTTP failures exactly once despite the rejection handler", async () => {
    const response = new Response(JSON.stringify({ code: 500, message: "Server unavailable" }), {
      status: 500,
      headers: { "Content-Type": "application/json" },
    });
    const client = new Api({ customFetch: vi.fn<typeof fetch>().mockResolvedValue(response) });

    await expect(client.workflow.validateSavedWorkflow(5)).rejects.toBe(response);
    expect(notifications.danger).toHaveBeenCalledTimes(1);
  });

  it("lets background checks handle network errors without a global notification", async () => {
    const error = new TypeError("Failed to fetch");
    const client = new Api({ customFetch: vi.fn<typeof fetch>().mockRejectedValue(error) });

    await expect(client.workflow.validateSavedWorkflow(5, { showErrorToast: false })).rejects.toBe(
      error,
    );
    expect(notifications.danger).not.toHaveBeenCalled();
    expect(notifications.clientFailure).not.toHaveBeenCalled();
  });

  it("lets background checks handle HTTP errors without changing request rejection", async () => {
    const response = new Response(JSON.stringify({ code: 500, message: "Server unavailable" }), {
      status: 500,
      headers: { "Content-Type": "application/json" },
    });
    const client = new Api({ customFetch: vi.fn<typeof fetch>().mockResolvedValue(response) });

    await expect(client.workflow.validateSavedWorkflow(5, { showErrorToast: false })).rejects.toBe(
      response,
    );
    expect(notifications.danger).not.toHaveBeenCalled();
    expect(notifications.clientFailure).not.toHaveBeenCalled();
  });

  it("returns an API-level failure for inline rendering when notifications are disabled", async () => {
    const failure = { code: 500, message: "Settings could not be loaded" };
    const response = new Response(JSON.stringify(failure));
    const client = new Api({ customFetch: vi.fn<typeof fetch>().mockResolvedValue(response) });

    await expect(
      client.workflow.validateSavedWorkflow(5, { showErrorToast: false }),
    ).resolves.toEqual(failure);
    expect(notifications.danger).not.toHaveBeenCalled();
  });

  it("preserves the existing callback policy for API-level failures", async () => {
    const failure = { code: 409, message: "Configuration changed" };
    const policy = vi.fn().mockReturnValueOnce(false).mockReturnValueOnce(true);
    const client = new Api({
      customFetch: vi
        .fn<typeof fetch>()
        .mockImplementation(async () => new Response(JSON.stringify(failure))),
    });

    await expect(
      client.workflow.validateSavedWorkflow(5, { showErrorToast: policy }),
    ).resolves.toEqual(failure);
    expect(notifications.danger).not.toHaveBeenCalled();
    await expect(
      client.workflow.validateSavedWorkflow(5, { showErrorToast: policy }),
    ).resolves.toEqual(failure);
    expect(policy).toHaveBeenCalledTimes(2);
    expect(policy).toHaveBeenLastCalledWith(failure);
    expect(notifications.danger).toHaveBeenCalledExactlyOnceWith({
      title: "[409]GET /workflow/5/validation",
      description: failure.message,
    });
  });
});
