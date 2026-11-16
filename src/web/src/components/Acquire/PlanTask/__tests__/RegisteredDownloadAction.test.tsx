import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import RegisteredDownloadAction from "../RegisteredDownloadAction";
import { planTaskApi } from "../api";

vi.mock("../api", () => ({ planTaskApi: { registeredDownloads: vi.fn(), useRegisteredDownload: vi.fn() } }));
vi.mock("@/components/utils", () => ({ bytesToSize: (n: number) => String(n) }));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, isDisabled, ...p }: any) =>
    <button disabled={isDisabled} aria-pressed={p["aria-pressed"]} onClick={onPress}>{children}</button>,
  Modal: ({ children, footer }: any) => <div role="dialog">{children}{footer}</div>,
  Spinner: () => <span>loading</span>,
}));
const row = { sourceId: "one", sourceTitle: "Original files", stage: "download", downloadTaskId: 2, runId: 5, fileCount: 4,
  totalBytes: 70, directory: "/mounted/files", token: "reviewed" };
function open() {
  const applied = vi.fn();
  render(<RegisteredDownloadAction taskId={8} revision={3} disabled={false} onApplied={applied} />);
  fireEvent.click(screen.getByText("acquisition.registered.title"));
  return applied;
}
beforeEach(() => { vi.resetAllMocks(); });
it("registers only the reviewed download at the visible revision", async () => {
  vi.mocked(planTaskApi.registeredDownloads).mockResolvedValue({ candidates: [row, { ...row, token: "other", sourceTitle: "Other" }] });
  const detail = {} as Awaited<ReturnType<typeof planTaskApi.useRegisteredDownload>>;
  vi.mocked(planTaskApi.useRegisteredDownload).mockResolvedValue(detail);
  const applied = open();
  await screen.findByRole("button", { name: /Original files/ });
  const confirm = screen.getByText("acquisition.registered.use");
  expect(confirm).toBeDisabled();
  fireEvent.click(screen.getByRole("button", { name: /Original files/ }));
  fireEvent.click(confirm);
  await waitFor(() => expect(applied).toHaveBeenCalledExactlyOnceWith(detail));
  expect(planTaskApi.useRegisteredDownload).toHaveBeenCalledExactlyOnceWith(8, 3, row.token);
});
it("requires a fresh choice after a failed application", async () => {
  vi.mocked(planTaskApi.registeredDownloads).mockResolvedValue({ candidates: [row] });
  vi.mocked(planTaskApi.useRegisteredDownload).mockRejectedValue(new Error("stale"));
  open();
  fireEvent.click(await screen.findByRole("button", { name: /Original files/ }));
  fireEvent.click(screen.getByText("acquisition.registered.use"));
  await screen.findByRole("alert");
  expect(screen.getByText("acquisition.registered.use")).toBeDisabled();
  fireEvent.click(screen.getByText("common.action.retry"));
  await screen.findByRole("button", { name: /Original files/ });
  expect(screen.getByText("acquisition.registered.use")).toBeDisabled();
});
