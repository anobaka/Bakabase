import type { ResourceProfile } from "../sourceProfiles";

import { beforeEach, describe, expect, it, vi } from "vitest";

import { resourceScopeApi, type ResourceScope } from "../scopeApi";

import { ResourceProfileRuleKind } from "@/sdk/constants";
const request = vi.hoisted(() => vi.fn());

vi.mock("@/sdk/BApi", () => ({ default: { request } }));
const profile = {
  id: 7,
  updatedAt: "2026-10-15T01:02:03Z",
  name: "Local player",
  priority: 8,
  sourceDeviceId: "owner",
  sourceLibraryEpoch: "epoch",
} as ResourceProfile;
const scope: ResourceScope = {
  id: 4,
  name: "Films",
  revision: 8,
  ruleCount: 2,
  sourceDeviceId: "owner",
  sourceLibraryEpoch: "epoch",
  search: {
    page: 1,
    pageSize: 100,
    group: {
      disabled: false,
      combinator: 1,
      filters: [
        { disabled: false, propertyPool: 4, propertyId: 9, operation: 1, dbValue: '"foreign-id"' },
      ],
    },
  },
};

beforeEach(() => {
  request.mockReset();
});
describe("reusable conditions and independent rule transport", () => {
  it("updates conditions with their immutable owner identity and expected revision", async () => {
    await resourceScopeApi.update(scope);
    expect(request).toHaveBeenCalledWith(
      expect.objectContaining({
        path: "/resource-scope/4",
        method: "PUT",
        body: expect.objectContaining({
          sourceDeviceId: "owner",
          sourceLibraryEpoch: "epoch",
          expectedRevision: 8,
          search: expect.objectContaining({
            group: expect.objectContaining({
              filters: [expect.objectContaining({ propertyId: 9, dbValue: '"foreign-id"' })],
            }),
          }),
        }),
      }),
    );
  });
  it("binds or unlinks without sending unrelated rule values", async () => {
    await resourceScopeApi.bind(profile, 4);
    expect(request.mock.calls[0][0].body).toEqual({
      scopeId: 4,
      expectedUpdatedAt: profile.updatedAt,
    });
    await resourceScopeApi.bind(profile, null);
    expect(request.mock.calls[1][0].body).toEqual({
      scopeId: null,
      expectedUpdatedAt: profile.updatedAt,
    });
  });
  it("writes one category only, preserves explicit empty players, and sends null for inherit", async () => {
    await resourceScopeApi.rule(profile, "playerOptions", { players: [] });
    expect(request.mock.calls[0][0]).toMatchObject({
      path: "/resource-scope/profile/7/rule",
      method: "PATCH",
      body: {
        kind: ResourceProfileRuleKind.Players,
        valueJson: '{"players":[]}',
        expectedUpdatedAt: profile.updatedAt,
      },
    });
    expect(Object.keys(request.mock.calls[0][0].body)).toHaveLength(3);
    await resourceScopeApi.rule(profile, "enhancerOptions", undefined);
    expect(request.mock.calls[1][0].body.valueJson).toBeNull();
  });
  it("preserves conflict rejection for the modal to retain its draft", async () => {
    request.mockRejectedValue(new Error("scope changed"));
    await expect(resourceScopeApi.update(scope)).rejects.toThrow("scope changed");
    await expect(resourceScopeApi.remove(scope)).rejects.toThrow("scope changed");
    expect(request).toHaveBeenLastCalledWith(
      expect.objectContaining({ path: "/resource-scope/4?expectedRevision=8", method: "DELETE" }),
    );
  });
});
