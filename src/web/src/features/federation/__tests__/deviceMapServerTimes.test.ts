import { afterAll, describe, expect, it } from "vitest";

import { buildDeviceGraph } from "../map/graph";

import {
  access,
  managementRequestIn,
  managementRequestOut,
  servers,
  sharingRequest,
  status,
} from "./deviceMapFixtures";

/*
 * The device map east of UTC, where most of its users are.
 *
 * `/remote-access/settings` writes its times the way every Newtonsoft endpoint does —
 * `yyyy-MM-dd HH:mm:ss.fff`, UTC with nothing saying so. A browser reads that shape as
 * local time, so at UTC+8 a request with ten minutes to run looked eight hours expired and
 * was left off the map, while the devices page listed it with Approve and Reject.
 *
 * The zone is set for this file only, and checked, so the tests cannot pass by running on
 * a UTC machine.
 */
const zone = process.env.TZ;

process.env.TZ = "Asia/Shanghai";
afterAll(() => {
  if (zone === undefined) delete process.env.TZ;
  else process.env.TZ = zone;
});

/** A time the way Newtonsoft writes `DateTime.UtcNow`: no `T`, no zone. */
const naked = (at: number) => new Date(at).toISOString().replace("T", " ").replace("Z", "");
const MINUTE = 60_000;

describe("device map: times the server writes without a zone", () => {
  it("runs east of UTC", () => {
    expect(new Date(2026, 8, 24).getTimezoneOffset()).toBe(-480);
    expect(naked(Date.UTC(2026, 8, 24, 8, 49, 45, 288))).toBe("2026-09-24 08:49:45.288");
  });

  it("draws a live request to manage this device, and not one that expired", () => {
    const now = Date.UTC(2026, 8, 24, 8, 40);
    const graph = buildDeviceGraph(
      {
        access: access({
          pendingRequests: [
            managementRequestIn("live", "Phone of Jax", {
              requestedAt: naked(now - MINUTE),
              expiresAt: naked(now + 10 * MINUTE),
            }),
            managementRequestIn("over", "Old tablet", {
              requestedAt: naked(now - 20 * MINUTE),
              expiresAt: naked(now - MINUTE),
            }),
          ],
        }),
      },
      now,
    );

    expect(graph.edges.map((edge) => [edge.id, edge.in])).toEqual([
      ["management:manager-request:live", "pending"],
    ]);
  });

  it("reads zoned times as they say, whichever listing they come from", () => {
    const now = Date.UTC(2026, 8, 24, 8, 40);
    const graph = buildDeviceGraph(
      {
        status: status({
          requests: [
            // Library sharing writes offsets (System.Text.Json, DateTimeOffset).
            sharingRequest("asker", "incoming", {
              expiresAt: new Date(now + 10 * MINUTE).toISOString(),
            }),
            sharingRequest("late", "incoming", {
              expiresAt: "2026-09-24T16:39:00+08:00",
            }),
          ],
        }),
        servers: servers({
          requests: [managementRequestOut("out", { expiresAt: naked(now + 10 * MINUTE) })],
        }),
      },
      now,
    );

    expect(graph.edges.map((edge) => edge.id).sort()).toEqual([
      "management:server-request:out",
      "sharing:sharing-request:incoming-asker",
    ]);
  });
});
