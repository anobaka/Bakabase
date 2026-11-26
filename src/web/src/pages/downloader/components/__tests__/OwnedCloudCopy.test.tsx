import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { ThirdPartyId } from "@/sdk/constants";
import { OwnedCloudCopyButton, OwnedCloudCopyModal } from "../OwnedCloudCopy";

const { get, retry, portal } = vi.hoisted(() => ({
  get: vi.fn(),
  retry: vi.fn(),
  portal: vi.fn(),
}));
vi.mock("@/components/ExternalLink", () => ({
  default: ({ href, children }: any) => <a href={href}>{children}</a>,
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    downloadCenter: { getCloudDownloadOwnedCopy: get, retryCloudDownloadOwnedCopy: retry },
  },
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: portal }),
}));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, isDisabled, isLoading, "aria-label": label }: any) => (
    <button aria-label={label} disabled={isDisabled || isLoading} onClick={onPress}>
      {children}
    </button>
  ),
  Modal: ({ children, title, footer }: any) => (
    <section aria-label={title}>
      {children}
      {footer}
    </section>
  ),
}));
const copy = (status: string, canRetry = false) => ({ downloadTaskId: 14, status, canRetry });
beforeEach(() => vi.resetAllMocks());
afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

it("shows the projected state without loading every row or submitting cleanup", () => {
  render(<OwnedCloudCopyButton copy={copy("waitingDownload")} />);
  expect(screen.getByText("downloadCenter.ownedCopy.state.waitingDownload")).toBeTruthy();
  expect(get).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button"));
  expect(portal).toHaveBeenCalledWith(OwnedCloudCopyModal, { taskId: 14 });
  expect(retry).not.toHaveBeenCalled();
});

it.each([
  "waitingDownload",
  "pending",
  "reviewRequired",
  "retained",
  "trashed",
  "released",
  "notApplicable",
])("shows %s and the corrective action offered by the provider", async (status) => {
  get.mockResolvedValue({ code: 0, data: copy(status) });
  render(<OwnedCloudCopyModal taskId={14} />);
  expect(await screen.findByText(`downloadCenter.ownedCopy.hint.${status}`)).toBeTruthy();
  expect(screen.queryByText("downloadCenter.ownedCopy.retry")).toBeNull();
  expect(retry).not.toHaveBeenCalled();
});

it("shows and submits a manual retry only when authorized by the current receipt", async () => {
  get.mockResolvedValue({ code: 0, data: copy("retryable", true) });
  retry.mockResolvedValue({ code: 0, data: copy("trashed") });
  render(<OwnedCloudCopyModal taskId={14} />);
  fireEvent.click(await screen.findByText("downloadCenter.ownedCopy.retry"));
  expect(await screen.findByText("downloadCenter.ownedCopy.hint.trashed")).toBeTruthy();
  expect(retry).toHaveBeenCalledTimes(1);
  expect(retry).toHaveBeenCalledWith(14, expect.objectContaining({ showErrorToast: false }));
  expect(screen.queryByText("downloadCenter.ownedCopy.state.released")).toBeNull();
  expect(screen.queryByText("downloadCenter.ownedCopy.retry")).toBeNull();
});

it("refreshes the recorded outcome after an uncertain response without issuing another retry", async () => {
  get
    .mockResolvedValueOnce({ code: 0, data: copy("retryable", true) })
    .mockResolvedValueOnce({ code: 0, data: copy("trashed") });
  retry.mockRejectedValue(new Error("connection lost"));
  render(<OwnedCloudCopyModal taskId={14} />);
  fireEvent.click(await screen.findByText("downloadCenter.ownedCopy.retry"));
  await screen.findByRole("alert");
  expect(screen.queryByText("downloadCenter.ownedCopy.retry")).toBeNull();
  fireEvent.click(screen.getByText("cloudDownload.account.refresh"));
  await waitFor(() =>
    expect(screen.getByText("downloadCenter.ownedCopy.hint.trashed")).toBeTruthy(),
  );
  expect(retry).toHaveBeenCalledTimes(1);
});

it("follows a pending cleanup to its recorded outcome while the dialog is open", async () => {
  vi.useFakeTimers();
  get
    .mockResolvedValueOnce({ code: 0, data: copy("pending") })
    .mockResolvedValueOnce({ code: 0, data: copy("trashed") });
  await act(async () => {
    render(<OwnedCloudCopyModal taskId={14} />);
  });
  expect(screen.getByText("downloadCenter.ownedCopy.state.pending")).toBeTruthy();
  await act(async () => {
    await vi.advanceTimersByTimeAsync(3000);
  });
  expect(screen.getByText("downloadCenter.ownedCopy.hint.trashed")).toBeTruthy();
  await act(async () => {
    await vi.advanceTimersByTimeAsync(9000);
  });
  expect(get).toHaveBeenCalledTimes(2);
  expect(retry).not.toHaveBeenCalled();
});

it("offers a trash recheck for an unconfirmed PikPak command and a link to the official drive", async () => {
  get.mockResolvedValue({
    code: 0,
    data: { ...copy("retryable", true), reasonCode: "PIKPAK_OWNED_COPY_TRASH_UNCONFIRMED" },
  });
  retry.mockResolvedValue({ code: 0, data: copy("trashed") });
  render(<OwnedCloudCopyModal taskId={14} thirdPartyId={ThirdPartyId.PikPak} />);
  fireEvent.click(await screen.findByText("downloadCenter.ownedCopy.recheckTrash"));
  expect(await screen.findByText("downloadCenter.ownedCopy.hint.trashed")).toBeTruthy();
  expect(screen.getByRole("link", { name: "downloadCenter.ownedCopy.openTrash" })).toHaveAttribute(
    "href",
    "https://mypikpak.com/drive/trash",
  );
  expect(screen.queryByText("downloadCenter.ownedCopy.retry")).toBeNull();
  expect(retry).toHaveBeenCalledOnce();
});
