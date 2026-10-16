import type { ManagementSource } from "../api";

import { describe, expect, it } from "vitest";

import { canReadSourceMetadata, sourceSearchRevision } from "../metadata";

export const replicaSource = (options: Partial<ManagementSource> = {}): ManagementSource => ({
  serverId: "remote",
  libraryEpoch: "library",
  name: "NAS",
  isLocal: false,
  available: false,
  contractVersion: 1,
  metadata: {
    identity: { serverId: "remote", libraryEpoch: "library" },
    name: "NAS",
    data: {
      activeGeneration: "generation",
      resourceCount: 10,
      activeSequence: 7,
      stagedResourceCount: 0,
    },
    sync: { paused: false, isOffline: true },
    denied: false,
  },
  ...options,
});

describe("metadata replica read boundary", () => {
  it("allows a completed exact-owner replica offline, but not an incomplete, denied or other-library copy", () => {
    const source = replicaSource();

    expect(canReadSourceMetadata(source)).toBe(true);
    expect(canReadSourceMetadata({ ...source, libraryEpoch: "other" })).toBe(false);
    expect(
      canReadSourceMetadata({ ...source, metadata: { ...source.metadata!, denied: true } }),
    ).toBe(false);
    expect(
      canReadSourceMetadata({
        ...source,
        available: true,
        metadata: {
          ...source.metadata!,
          data: { ...source.metadata!.data, activeGeneration: undefined },
        },
      }),
    ).toBe(false);
  });
  it("keeps ordinary local and pre-replica sources on their existing availability behavior", () => {
    expect(canReadSourceMetadata(replicaSource({ isLocal: true, available: true }))).toBe(true);
    expect(canReadSourceMetadata(replicaSource({ metadata: undefined, available: true }))).toBe(
      true,
    );
    expect(canReadSourceMetadata(replicaSource({ metadata: undefined }))).toBe(false);
  });
  it("only changes the search revision when readable data or access changes, not while a staging page advances", () => {
    const source = replicaSource();
    const progress = {
      ...source,
      metadata: {
        ...source.metadata!,
        data: { ...source.metadata!.data, stagedResourceCount: 100 },
        sync: { ...source.metadata!.sync, lastSyncedAtUtc: "2026-10-15T10:00:00Z" },
      },
    };

    expect(sourceSearchRevision(progress)).toEqual(sourceSearchRevision(source));
    progress.metadata.data.activeSequence++;
    expect(sourceSearchRevision(progress)).not.toEqual(sourceSearchRevision(source));
  });
});
