import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import cn from "@/locales/cn/pages/acquisition.json";
import en from "@/locales/en/pages/acquisition.json";
import SourceHealthBadge from "../SourceHealthBadge";
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, ...props }: any) => (
    <button aria-label={props["aria-label"]}>{children}</button>
  ),
  Chip: ({ children }: any) => <span>{children}</span>,
  Popover: ({ trigger, children }: any) => (
    <div>
      {trigger}
      {children}
    </div>
  ),
}));
afterEach(cleanup);
it.each(["2020-01-01T00:00:00Z", "invalid timestamp"])(
  "expired or invalid evidence does not advertise confirmed invalidity (%s)",
  (expiresAt) => {
    render(
      <SourceHealthBadge
        health={{ status: "confirmedUnavailable", expiresAt, checkedAt: "2020-01-01T00:00:00Z" }}
      />,
    );
    expect(screen.getByRole("button")).toHaveTextContent("sourceAvailability.status.unknown");
    expect(screen.getByText("sourceAvailability.expiredObservation")).toBeInTheDocument();
  },
);
it("shows scoped evidence and checked time separately from disabling", () => {
  render(
    <SourceHealthBadge
      health={{
        status: "accessRequired",
        code: "SOURCE_ACCESS_CODE_REQUIRED",
        checkedAt: "2026-01-01T00:00:00Z",
        evidence: { scope: "anonymous" },
      }}
      disabled
    />,
  );
  expect(screen.getByRole("button")).toHaveTextContent("sourceAvailability.status.accessRequired");
  expect(
    screen.getByText("sourceAvailability.reason.SOURCE_ACCESS_CODE_REQUIRED"),
  ).toBeInTheDocument();
  expect(screen.getByText("sourceAvailability.disabled")).toBeInTheDocument();
  expect(screen.getByText("sourceAvailability.checkedAt")).toBeInTheDocument();
});
it("does not claim a check occurred for a missing observation", () => {
  const { container } = render(<SourceHealthBadge />);
  expect(container).toBeEmptyDOMElement();
});
it("shows a reason once when the provider response repeats it, retaining other evidence", () => {
  render(
    <SourceHealthBadge
      health={{
        status: "confirmedUnavailable",
        code: "SOURCE_SHARE_EXPIRED",
        evidence: { response: "SOURCE_SHARE_EXPIRED", scope: "anonymous" },
      }}
    />,
  );
  expect(screen.getAllByText("sourceAvailability.reason.SOURCE_SHARE_EXPIRED")).toHaveLength(1);
  expect(screen.queryByText("sourceAvailability.evidence.response")).not.toBeInTheDocument();
  expect(screen.getByText("sourceAvailability.evidence.scope")).toBeInTheDocument();
});
it.each(["SOURCE_SHARE_EXPIRED", undefined])(
  "preserves a distinct provider response when the normalized reason is %s",
  (code) => {
    render(
      <SourceHealthBadge
        health={{
          status: "confirmedUnavailable",
          code,
          evidence: { response: "BAIDU_SHARE_EXPIRED" },
        }}
      />,
    );
    expect(screen.getByText("sourceAvailability.evidence.response")).toBeInTheDocument();
    expect(screen.getByText("sourceAvailability.reason.BAIDU_SHARE_EXPIRED")).toBeInTheDocument();
    if (code) {
      expect(screen.getByText(`sourceAvailability.reason.${code}`)).toBeInTheDocument();
    }
  },
);
it("explains blocked DNS checks without claiming the source is invalid", () => {
  const key = "sourceAvailability.reason.SOURCE_NETWORK_REQUIRES_PROXY";
  expect(cn[key]).toContain("这不代表链接失效");
  expect(en[key]).toContain("This does not mean the link is invalid");
  render(
    <SourceHealthBadge
      health={{ status: "temporarilyUnavailable", code: "SOURCE_NETWORK_REQUIRES_PROXY" }}
    />,
  );
  expect(screen.getByRole("button")).toHaveTextContent(
    "sourceAvailability.status.temporarilyUnavailable",
  );
  expect(screen.getByText(key)).toBeInTheDocument();
  expect(
    screen.queryByText("sourceAvailability.status.confirmedUnavailable"),
  ).not.toBeInTheDocument();
});
