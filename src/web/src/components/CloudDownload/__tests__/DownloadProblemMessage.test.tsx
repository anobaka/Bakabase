import { render, screen } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import DownloadProblemMessage from "../DownloadProblemMessage";
import WorksetResults from "../WorksetResults";
import cn from "@/locales/cn/pages/cloudDownload.json";

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: { defaultValue?: string }) =>
      (cn as Record<string, string>)[key] ?? options?.defaultValue ?? key,
  }),
}));
vi.mock("@/components/bakaui", () => ({ Button: () => null }));

it("adds a cancelled-share explanation while keeping the original run diagnostic", () => {
  const diagnostic = "CloudProviderException: BAIDU_SHARE_CANCELLED";
  render(<DownloadProblemMessage message={diagnostic} />);
  expect(screen.getByText(cn["cloudDownload.problemCode.BAIDU_SHARE_CANCELLED"])).toBeVisible();
  expect(screen.getByText(diagnostic)).toBeVisible();
});

it("keeps unknown and historical neutral diagnostics without inventing a cause", () => {
  const view = render(<DownloadProblemMessage message="Unclassified error" />);
  expect(screen.getByText("Unclassified error")).toBeVisible();
  expect(screen.queryByText(cn["cloudDownload.problemCode.BAIDU_SHARE_CANCELLED"])).toBeNull();
  view.rerender(<DownloadProblemMessage message="BAIDU_SHARE_VERIFICATION_FAILED" />);
  expect(
    screen.getByText(cn["cloudDownload.problemCode.BAIDU_SHARE_VERIFICATION_FAILED"]),
  ).toBeVisible();
  expect(screen.getByText("BAIDU_SHARE_VERIFICATION_FAILED")).toBeVisible();
  expect(screen.queryByText(cn["cloudDownload.problemCode.BAIDU_SHARE_CANCELLED"])).toBeNull();
});

it("uses the same cancelled classification for a cloud workset result", () => {
  render(
    <WorksetResults
      json={JSON.stringify([{ targets: [{ id: "part", problem: "BAIDU_SHARE_CANCELLED" }] }])}
    />,
  );
  expect(screen.getByText(cn["cloudDownload.problemCode.BAIDU_SHARE_CANCELLED"])).toBeVisible();
  expect(screen.queryByText(/提取码/)).toBeNull();
});

it("shows the safe TeraBox upstream error in both download and workflow output surfaces", () => {
  const diagnostic = "CloudProviderException: TERABOX_DOWNLOAD_IDENTITY_REJECTED";
  const view = render(<DownloadProblemMessage message={diagnostic} />);
  expect(screen.getByText(/HTTP 403.*31045/)).toBeVisible();
  expect(screen.getByText(diagnostic)).toBeVisible();
  view.unmount();
  render(<WorksetResults json={JSON.stringify([{ targets: [{ id: "part", problem: "TERABOX_DOWNLOAD_IDENTITY_REJECTED" }] }])} />);
  expect(screen.getByText(/HTTP 403.*31045/)).toBeVisible();
});
