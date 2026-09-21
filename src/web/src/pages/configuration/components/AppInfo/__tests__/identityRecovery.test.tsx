import { cleanup, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import IdentityRecoveryLink from "../IdentityRecoveryLink";

const context = vi.hoisted(() => ({ initialized: true, isLocal: true, pureClient: false }));

vi.mock("@/stores/remoteAccess", () => ({
  useRemoteAccessStore: (selector: (state: unknown) => unknown) => selector(context),
  useIsPureClient: () => context.pureClient,
}));
afterEach(cleanup);

describe("AppData recovery navigation", () => {
  it.each([
    [true, false, true],
    [false, false, false],
    [true, true, false],
  ])(
    "offers local recovery only on the unified library host (local: %s, client: %s)",
    (isLocal, pureClient, linked) => {
      Object.assign(context, { isLocal, pureClient });
      render(
        <MemoryRouter>
          <IdentityRecoveryLink />
        </MemoryRouter>,
      );
      if (linked) {
        expect(screen.getByRole("link")).toHaveAttribute(
          "href",
          "/federation/devices?section=identity",
        );
      } else {
        expect(screen.queryByRole("link")).not.toBeInTheDocument();
        expect(
          screen.getByText("configuration.appInfo.identityRecovery.onHost"),
        ).toBeInTheDocument();
      }
    },
  );
});
