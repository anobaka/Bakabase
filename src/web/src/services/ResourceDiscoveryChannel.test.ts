import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { ResourceDiscoveryChannel } from "./ResourceDiscoveryChannel";

import { DataOrigin, ResourceDataType } from "@/sdk/constants";

vi.mock("@/config/env", () => ({ default: { apiEndpoint: "" } }));

class FakeEventSource extends EventTarget {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSED = 2;
  static instances: FakeEventSource[] = [];
  readyState = FakeEventSource.CONNECTING;
  onopen: (() => void) | null = null;
  onerror: ((event: Event) => void) | null = null;
  constructor(_url: string) {
    super();
    FakeEventSource.instances.push(this);
  }
  open() {
    this.readyState = FakeEventSource.OPEN;
    this.onopen?.();
  }
  close() {
    this.readyState = FakeEventSource.CLOSED;
  }
  fail() {
    this.readyState = FakeEventSource.CLOSED;
    this.onerror?.(new Event("error"));
  }
  result(success = true, origin = DataOrigin.FileSystem) {
    const event = new MessageEvent(success ? "result" : "error", {
      data: JSON.stringify({
        resourceId: 42,
        origin,
        dataType: ResourceDataType.PlayableItem,
        success,
        error: success ? undefined : "NAS offline",
        playableItems: [],
      }),
    });

    if (!success) this.onerror?.(event);
    this.dispatchEvent(event);
  }
}

let channel: ResourceDiscoveryChannel;
const request = vi.fn();
const source = () => FakeEventSource.instances.at(-1)!;
const subscribe = (callback: ReturnType<typeof vi.fn>, origin = DataOrigin.FileSystem) =>
  channel.subscribe(42, origin, ResourceDataType.PlayableItem, callback);

beforeEach(() => {
  vi.useFakeTimers();
  vi.stubGlobal("EventSource", FakeEventSource);
  vi.stubGlobal("fetch", request);
  FakeEventSource.instances = [];
  request.mockReset().mockResolvedValue({ ok: true, json: async () => ({ code: 0 }) });
  channel = new ResourceDiscoveryChannel();
});
afterEach(() => {
  channel.disconnect();
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

describe("discovery connection and retries", () => {
  it("requeues a failed discovery without restarting an independent pending source", async () => {
    const failed = vi.fn();
    const other = vi.fn();
    const first = subscribe(failed);
    const second = subscribe(other, DataOrigin.Steam);

    source().open();
    await Promise.all([first, second]);
    await vi.advanceTimersByTimeAsync(50);
    source().result(false);
    expect(failed).toHaveBeenCalledWith(null, "NAS offline");
    expect(other).not.toHaveBeenCalled();
    const retry = vi.fn();

    await subscribe(retry);
    await vi.advanceTimersByTimeAsync(50);
    expect(request).toHaveBeenCalledTimes(2);
    expect(JSON.parse(request.mock.calls[1]![1].body)).toEqual([
      { resourceId: 42, origin: DataOrigin.FileSystem, dataType: ResourceDataType.PlayableItem },
    ]);
    source().result();
    expect(retry).toHaveBeenCalledWith(expect.objectContaining({ playableItems: [] }));
    expect(other).not.toHaveBeenCalled();
  });

  it.each([403, 500])("reports HTTP %s instead of leaving a pending scan", async (status) => {
    request.mockResolvedValueOnce({ ok: false, status });
    const complete = vi.fn();
    const pending = subscribe(complete);

    source().open();
    await pending;
    await vi.advanceTimersByTimeAsync(50);
    expect(complete).toHaveBeenCalledWith(null, `Discovery request failed (${status})`);
    await subscribe(vi.fn());
    await vi.advanceTimersByTimeAsync(50);
    expect(request).toHaveBeenCalledTimes(2);
  });

  it("times out a stalled subscription request without timing out the actual directory scan", async () => {
    request.mockImplementationOnce(
      (_url, options) =>
        new Promise((_resolve, reject) => {
          options.signal.addEventListener("abort", () => reject(new Error("aborted")));
        }),
    );
    const complete = vi.fn();
    const pending = subscribe(complete);

    source().open();
    await pending;
    await vi.advanceTimersByTimeAsync(15050);
    expect(complete).toHaveBeenCalledWith(null, "Discovery request timed out");
  });

  it("reports an API envelope refusal even when HTTP succeeds", async () => {
    request.mockResolvedValueOnce({
      ok: true,
      json: async () => ({ code: 403, message: "Access revoked" }),
    });
    const complete = vi.fn();
    const pending = subscribe(complete);

    source().open();
    await pending;
    await vi.advanceTimersByTimeAsync(50);
    expect(complete).toHaveBeenCalledWith(null, "Access revoked");
  });

  it("returns a failed state on connection refusal and reconnects on retry", async () => {
    const complete = vi.fn();
    const pending = subscribe(complete);

    source().fail();
    await pending;
    expect(complete).toHaveBeenCalledWith(null, "Could not connect to file discovery");
    const retry = subscribe(vi.fn());

    source().open();
    await retry;
    await vi.advanceTimersByTimeAsync(50);
    expect(FakeEventSource.instances).toHaveLength(2);
    expect(request).toHaveBeenCalledOnce();
  });

  it("ends a hung initial connection with a retryable error", async () => {
    const complete = vi.fn();
    const pending = subscribe(complete);

    await vi.advanceTimersByTimeAsync(15000);
    const unsubscribe = await pending;

    expect(complete).toHaveBeenCalledWith(null, "Discovery connection timed out");
    unsubscribe();
    expect(channel.subscriberCount).toBe(0);
  });

  it("removing one listener does not cancel another listener's queued request", async () => {
    const first = subscribe(vi.fn());
    const complete = vi.fn();
    const second = subscribe(complete);

    source().open();
    const unsubscribe = await first;

    await second;
    unsubscribe();
    await vi.advanceTimersByTimeAsync(50);
    expect(request).toHaveBeenCalledOnce();
    source().result();
    expect(complete).toHaveBeenCalledOnce();
  });

  it("preserves a completed SSE result when its enqueue HTTP response later fails", async () => {
    let rejectRequest!: (reason: Error) => void;

    request.mockImplementationOnce(
      () =>
        new Promise((_resolve, reject) => {
          rejectRequest = reject;
        }),
    );
    const complete = vi.fn();
    const pending = subscribe(complete);

    source().open();
    await pending;
    await vi.advanceTimersByTimeAsync(50);
    source().result();
    rejectRequest(new Error("connection reset"));
    await vi.advanceTimersByTimeAsync(1);
    expect(complete).toHaveBeenCalledOnce();
    expect(complete).toHaveBeenCalledWith(expect.objectContaining({ playableItems: [] }));
  });

  it("only fails in-flight requests when the stream disconnects, leaving completed data alone", async () => {
    const done = vi.fn();
    const pending = vi.fn();
    const first = subscribe(done);
    const second = subscribe(pending, DataOrigin.Steam);

    source().open();
    await Promise.all([first, second]);
    await vi.advanceTimersByTimeAsync(50);
    source().result();
    source().fail();
    expect(done).toHaveBeenCalledOnce();
    expect(pending).toHaveBeenCalledWith(null, "File discovery connection was interrupted");
  });
});
