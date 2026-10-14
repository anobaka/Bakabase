import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { ResourceOwnerScopeProvider } from "../resourceContext";
import { useOwnerResourceDisplayOptions } from "../useOwnerResourceDisplayOptions";
import BApi from "@/sdk/BApi";
import { PropertyPool } from "@/sdk/constants";

const mocks = vi.hoisted(() => ({ request: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({ default: {} }));
vi.mock("../api", () => ({
  sourceKey: (source: any) => JSON.stringify([source.serverId, source.libraryEpoch]),
  ownerClient: (source: any) => (operation: string) => mocks.request(source, operation),
}));

function Probe({ label }: { label: string }) {
  const options = useOwnerResourceDisplayOptions();

  return (
    <span data-testid={label}>
      {JSON.stringify(options.data?.uiResource?.displayProperties ?? [])}
    </span>
  );
}
function Owned({ serverId, epoch = "epoch", children }: any) {
  return (
    <ResourceOwnerScopeProvider
      scope={{
        source: {
          serverId,
          libraryEpoch: epoch,
          name: serverId,
          available: true,
          isLocal: false,
          contractVersion: 1,
        },
        key: serverId,
        api: BApi,
      }}
    >
      {children}
    </ResourceOwnerScopeProvider>
  );
}

afterEach(cleanup);

describe("owner-scoped custom-property presentation options", () => {
  it("shares one request between cards but keeps devices and library epochs separate", async () => {
    mocks.request.mockImplementation(async (source) => ({
      code: 0,
      data: {
        uiResource: {
          displayProperties: [{ pool: PropertyPool.Custom, id: source.serverId === "one" ? 7 : 9 }],
        },
      },
    }));
    const view = render(
      <>
        <Owned serverId="one">
          <Probe label="one-a" />
          <Probe label="one-b" />
        </Owned>
        <Owned serverId="two">
          <Probe label="two" />
        </Owned>
      </>,
    );

    await waitFor(() => expect(screen.getByTestId("one-a")).toHaveTextContent('"id":7'));
    expect(screen.getByTestId("one-b")).toHaveTextContent('"id":7');
    expect(screen.getByTestId("two")).toHaveTextContent('"id":9');
    expect(mocks.request).toHaveBeenCalledTimes(2);
    view.rerender(
      <Owned serverId="one" epoch="replacement">
        <Probe label="new" />
      </Owned>,
    );
    await waitFor(() => expect(mocks.request).toHaveBeenCalledTimes(3));
    expect(mocks.request).toHaveBeenLastCalledWith(
      expect.objectContaining({ libraryEpoch: "replacement" }),
      "resourceDisplayOptions",
    );
  });

  it("does not fetch any other server's options for an ordinary local card", () => {
    mocks.request.mockClear();
    render(<Probe label="local" />);
    expect(mocks.request).not.toHaveBeenCalled();
    expect(screen.getByTestId("local")).toHaveTextContent("[]");
  });
});
