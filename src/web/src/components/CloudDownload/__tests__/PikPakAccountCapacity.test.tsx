import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import PikPakAccountCapacity from "../PikPakAccountCapacity";

const api = vi.hoisted(() => ({ getPikPakCliCapacity: vi.fn() }));
vi.mock("@/components/ExternalLink", () => ({
  default: ({ href, children }: any) => <a href={href}>{children}</a>,
}));
vi.mock("@/sdk/BApi", () => ({ default: { pikpakCli: api } }));
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    i18n: { language: "en" },
    t: (key: string, vars?: { size?: string; defaultValue?: string }) =>
      vars?.size ? `${key} ${vars.size}` : key,
  }),
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, isLoading }: any) => (
    <button disabled={isLoading} onClick={onPress}>
      {children}
    </button>
  ),
}));
const owner = { accountId: "example", accountVersion: "v1" };
const checkedAt = "2026-01-01T12:34:00+00:00";
const fresh = {
  ...owner,
  status: "fresh",
  checkedAt,
  usedBytes: { minimum: 1995000000, maximum: 2005000000 },
  totalBytes: { minimum: 999500000, maximum: 1000500000 },
  availableBytes: { minimum: 0, maximum: 0 },
  trashBytes: undefined,
};
const read = () =>
  fireEvent.click(screen.getByRole("button", { name: "pikpakCli.capacity.refresh" }));
beforeEach(() => api.getPikPakCliCapacity.mockReset());
afterEach(cleanup);

it("requests account storage only on demand and distinguishes rounded, exact zero, and absent values", async () => {
  api.getPikPakCliCapacity.mockResolvedValue({ code: 0, data: fresh });
  const view = render(<PikPakAccountCapacity {...owner} />);
  expect(api.getPikPakCliCapacity).not.toHaveBeenCalled();
  read();
  await screen.findByText("pikpakCli.capacity.approximate 2.00 GB");
  expect(screen.getByText("pikpakCli.capacity.approximate 1.00 GB")).toBeTruthy();
  expect(screen.getByText("0 B")).toBeTruthy();
  expect(screen.getByText("pikpakCli.capacity.unreported")).toBeTruthy();
  expect(view.container.querySelector("time")).toHaveAttribute("datetime", checkedAt);
  expect(api.getPikPakCliCapacity).toHaveBeenCalledExactlyOnceWith(
    {
      expectedAccountId: owner.accountId,
      expectedAccountVersion: owner.accountVersion,
    },
    expect.objectContaining({ sensitiveResponse: true, signal: expect.any(AbortSignal) }),
  );
});

it.each(["busy", "unavailable"])(
  "keeps the last successful values and timestamp when %s",
  async (status) => {
    api.getPikPakCliCapacity.mockResolvedValueOnce({ code: 0, data: fresh }).mockResolvedValueOnce({
      code: 0,
      data: {
        ...owner,
        status,
        problem: status === "busy" ? undefined : "PIKPAK_CAPACITY_UNAVAILABLE",
        checkedAt: "2026-01-02T00:00:00Z",
      },
    });
    const view = render(<PikPakAccountCapacity {...owner} />);
    read();
    await screen.findByText("0 B");
    read();
    const notice = await screen.findByRole("status");
    expect(notice.textContent).toBe(
      status === "busy"
        ? "pikpakCli.capacity.busy"
        : "cloudDownload.problemCode.PIKPAK_CAPACITY_UNAVAILABLE",
    );
    expect(screen.getByText("pikpakCli.capacity.approximate 2.00 GB")).toBeTruthy();
    expect(view.container.querySelector("time")).toHaveAttribute("datetime", checkedAt);
  },
);

it("does not attach a late response to a different connected account", async () => {
  let resolve!: (value: unknown) => void;
  api.getPikPakCliCapacity.mockImplementationOnce(
    () =>
      new Promise((r) => {
        resolve = r;
      }),
  );
  const view = render(<PikPakAccountCapacity key="v1" {...owner} />);
  read();
  await waitFor(() => expect(api.getPikPakCliCapacity).toHaveBeenCalledOnce());
  const signal = api.getPikPakCliCapacity.mock.calls[0][1].signal;
  view.rerender(<PikPakAccountCapacity key="v2" accountId="other" accountVersion="v2" />);
  expect(signal.aborted).toBe(true);
  await act(async () => {
    resolve({ code: 0, data: fresh });
  });
  expect(screen.queryByText("0 B")).toBeNull();
  expect(view.container.querySelector("time")).toBeNull();
});

it("shows an explicit unlimited plan separately from missing capacity", async () => {
  api.getPikPakCliCapacity.mockResolvedValue({
    code: 0,
    data: { ...fresh, unlimited: true, totalBytes: null, availableBytes: null, trashBytes: null },
  });
  render(<PikPakAccountCapacity {...owner} />);
  read();
  expect(await screen.findAllByText("pikpakCli.capacity.unlimited")).toHaveLength(2);
  expect(screen.getAllByText("pikpakCli.capacity.unreported")).toHaveLength(1);
  expect(screen.getByRole("link", { name: "pikpakCli.capacity.openDrive" })).toHaveAttribute(
    "href",
    "https://mypikpak.com/drive/",
  );
});
