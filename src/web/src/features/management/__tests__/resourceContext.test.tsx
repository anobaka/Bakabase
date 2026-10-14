import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import BApi from "@/sdk/BApi";
import {
  ResourceOwnerProvider,
  ResourceOwnerScopeProvider,
  useResourceApi,
  useResourceOwnerScope,
  type ResourceOwnerScope,
} from "../resourceContext";
import type { ManagementSource } from "../api";

vi.mock("@/sdk/BApi", async () => {
  const { Api } = await import("@/sdk/Api");

  return { default: Object.assign(new Api({ baseUrl: "http://manager" }), { request: vi.fn() }) };
});
vi.mock("@/config/env", () => ({ default: { apiEndpoint: "http://manager" } }));

const source = (serverId: string): ManagementSource => ({
  serverId,
  libraryEpoch: `epoch-${serverId}`,
  name: serverId,
  available: true,
  isLocal: false,
  contractVersion: 1,
});

describe("owner scope of detached resource modals", () => {
  it("retains the captured owner when the original card switches device", () => {
    let captured: ResourceOwnerScope | undefined;
    function Capture() {
      const scope = useResourceOwnerScope();

      captured ??= scope;

      return null;
    }
    function Show() {
      const scope = useResourceOwnerScope();

      return (
        <span>
          {scope?.source.serverId}:{scope?.source.libraryEpoch}:{scope?.resourceId}
        </span>
      );
    }

    const card = render(
      <ResourceOwnerProvider source={source("a")} resourceId={7}>
        <Capture />
      </ResourceOwnerProvider>,
    );

    card.rerender(
      <ResourceOwnerProvider source={source("b")} resourceId={7}>
        <Capture />
      </ResourceOwnerProvider>,
    );
    render(
      <ResourceOwnerScopeProvider scope={captured}>
        <Show />
      </ResourceOwnerScopeProvider>,
    );
    expect(screen.getByText("a:epoch-a:7")).toBeTruthy();
    expect(Object.isFrozen(captured)).toBe(true);
    expect(Object.isFrozen(captured?.source)).toBe(true);
  });

  it("explicitly restores an unscoped portal instead of borrowing an unrelated owner", () => {
    let api: typeof BApi | undefined;
    function CaptureApi() {
      api = useResourceApi();

      return null;
    }

    render(
      <ResourceOwnerProvider source={source("b")}>
        <ResourceOwnerScopeProvider scope={undefined}>
          <CaptureApi />
        </ResourceOwnerScopeProvider>
      </ResourceOwnerProvider>,
    );
    expect(api).toBe(BApi);
  });
});
