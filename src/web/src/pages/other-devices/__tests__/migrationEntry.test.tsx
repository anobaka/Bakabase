import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import OtherDevicesPage from "../index";

import BApi from "@/sdk/BApi";
import { clientApi } from "@/core/clientApi";

vi.mock("@/sdk/BApi", () => ({ default: { otherDevices: { getOtherDeviceDownloads: vi.fn() } } }));
vi.mock("@/core/clientApi", () => ({ clientApi: { migrationHints: vi.fn() } }));
vi.mock("@/stores/remoteAccess", () => ({ useIsPureClient: () => true }));
vi.mock("@/components/bakaui", () => ({
  Chip: ({ children }: { children: React.ReactNode }) => <span>{children}</span>,
}));
vi.mock("@/components/ExternalLink", () => ({
  default: ({ children, href }: { children: React.ReactNode; href: string }) => (
    <a href={href}>{children}</a>
  ),
}));
const OriginalURL = URL;

beforeEach(() => {
  vi.clearAllMocks();
  vi.useFakeTimers();
  vi.stubGlobal(
    "URL",
    class extends OriginalURL {
      static createObjectURL = vi.fn(() => "blob:migration-fixture");
      static revokeObjectURL = vi.fn();
    },
  );
  vi.mocked(clientApi.migrationHints).mockResolvedValue({
    format: "bakabase-client-connection-hints",
    version: 1,
    servers: [],
  });
});
afterEach(() => {
  cleanup();
  vi.runOnlyPendingTimers();
  vi.useRealTimers();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("legacy migration remains available independently of download manifests", () => {
  it("exports local connection hints while the unrelated download manifest is still pending", async () => {
    vi.mocked(BApi.otherDevices.getOtherDeviceDownloads).mockReturnValue(new Promise(() => {}));
    const download = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});

    render(<OtherDevicesPage />);
    expect(screen.getByRole("status")).toHaveTextContent("otherDevices.loading");
    expect(screen.queryByText("otherDevices.unavailable")).not.toBeInTheDocument();
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "federation.migration.export" }));
    });
    expect(download).toHaveBeenCalledOnce();
    expect(clientApi.migrationHints).toHaveBeenCalledOnce();
    expect(URL.createObjectURL).toHaveBeenCalledOnce();
  });
  it("keeps the migration action when the download manifest fails", async () => {
    vi.mocked(BApi.otherDevices.getOtherDeviceDownloads).mockRejectedValue(
      new Error("Manifest unavailable"),
    );
    await act(async () => {
      render(<OtherDevicesPage />);
    });
    expect(screen.getByText("otherDevices.unavailable")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "federation.migration.export" })).not.toBeDisabled();
    expect(screen.queryByText("otherDevices.loading")).not.toBeInTheDocument();
  });
});
