import { cleanup, render, screen } from "@testing-library/react";
import { afterAll, afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import SourceAvailabilityReviewModal from "../SourceAvailabilityReviewModal";
import SourceHealthBadge from "../SourceHealthBadge";

const api = vi.hoisted(() => ({ preview: vi.fn(), batch: vi.fn(), apply: vi.fn() }));
vi.mock("../api", () => ({ sourceAvailabilityApi: api }));
vi.mock("@/components/Acquire/AcquisitionSourceDetails", () => ({
  default: ({ source }: any) => <p>{source.url}</p>,
}));
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options: any = {}) =>
      key === "sourceAvailability.checkedAt" ? String(options.time) : key,
  }),
}));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ children, footer }: any) => (
    <section role="dialog">
      {children}
      {footer}
    </section>
  ),
  Button: ({ children, onPress, isDisabled, isLoading }: any) => (
    <button disabled={isDisabled || isLoading} onClick={onPress}>
      {children}
    </button>
  ),
  Checkbox: ({ isSelected, isDisabled }: any) => (
    <input type="checkbox" checked={isSelected} disabled={isDisabled} readOnly />
  ),
  Chip: ({ children }: any) => <span>{children}</span>,
  Tooltip: ({ children }: any) => children,
  Popover: ({ trigger, children }: any) => (
    <div>
      {trigger}
      {children}
    </div>
  ),
  Spinner: () => null,
  Progress: () => null,
  Tabs: ({ children }: any) => <div>{children}</div>,
  Tab: () => null,
  Pagination: () => null,
}));

const previousZone = process.env.TZ;
process.env.TZ = "Asia/Shanghai";
afterAll(() => {
  if (previousZone === undefined) delete process.env.TZ;
  else process.env.TZ = previousZone;
});
const now = Date.UTC(2026, 9, 25, 9, 17, 58, 699);
const checkedAt = "2026-10-25 09:17:58.699";
const preview = {
  batchId: "fixed",
  taskCount: 1,
  sourceCount: 1,
  status: "ready",
  processed: 1,
  total: 1,
  expiresAt: "2026-10-25 09:37:58.699",
  rows: [
    {
      key: "a",
      taskId: 401,
      partId: "p",
      sourceId: "s",
      url: "https://example.com/source",
      actionable: true,
      disabled: false,
      inUse: false,
      health: { status: "confirmedUnavailable", checkedAt, expiresAt: "2026-10-25 15:17:58.699" },
    },
  ],
};
beforeEach(() => {
  vi.spyOn(Date, "now").mockReturnValue(now);
  api.preview.mockResolvedValue(preview);
});
afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe("source availability in a UTC+8 browser with actual Newtonsoft wire timestamps", () => {
  it("keeps a fresh server preview usable instead of expiring it eight hours early", async () => {
    expect(new Date(2026, 9, 25).getTimezoneOffset()).toBe(-480);
    render(<SourceAvailabilityReviewModal taskIds={[401]} />);
    await screen.findByRole("checkbox");
    expect(
      screen.getByRole("button", { name: "sourceAvailability.review.action.disable" }),
    ).not.toBeDisabled();
    expect(screen.queryByText("sourceAvailability.review.expired")).not.toBeInTheDocument();
    expect(
      screen.getAllByText(new Date("2026-10-25T09:17:58.699Z").toLocaleString()).length,
    ).toBeGreaterThan(0);
    expect(api.apply).not.toHaveBeenCalled();
  });
  it("preserves a current probe status and displays the local time of its UTC observation", () => {
    render(
      <SourceHealthBadge
        health={{
          status: "temporarilyUnavailable",
          checkedAt,
          expiresAt: "2026-10-25 09:19:58.699",
        }}
      />,
    );
    expect(screen.getByRole("button")).toHaveTextContent(
      "sourceAvailability.status.temporarilyUnavailable",
    );
    expect(screen.queryByText("sourceAvailability.expiredObservation")).not.toBeInTheDocument();
    expect(
      screen.getByText(new Date("2026-10-25T09:17:58.699Z").toLocaleString()),
    ).toBeInTheDocument();
  });
  it("still locks a deadline that has actually elapsed", async () => {
    api.preview.mockResolvedValue({ ...preview, expiresAt: "2026-10-25 09:16:58.699" });
    render(<SourceAvailabilityReviewModal taskIds={[401]} />);
    await screen.findByRole("checkbox");
    expect(
      screen.getByRole("button", { name: "sourceAvailability.review.action.disable" }),
    ).toBeDisabled();
    expect(screen.getByText("sourceAvailability.review.expired")).toBeInTheDocument();
  });
});
