import type { ReactNode } from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { cloudDownloadActivities } from "../Activities/CloudDownload";
import { DownloadSourcesTriggerUI } from "../Triggers/DownloadSources";
import WorksetResults from "@/components/CloudDownload/WorksetResults";
import cn from "@/locales/cn/pages/cloudDownload.json";
import en from "@/locales/en/pages/cloudDownload.json";

vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));
vi.mock("@/components/CloudDownload/AccountSelect", () => ({
  default: () => <span>Account</span>,
}));
vi.mock("@/components/ThirdPartyConfig/platforms/BaiduNetdiskConfig", () => ({
  default: () => <span>Accounts</span>,
}));
vi.mock("@/pages/configuration/components/Dependency/components/Component", () => ({
  default: () => <span>Install</span>,
}));
vi.mock("@/components/ThirdPartyConfig/base/DownloadDirectoryField", () => ({
  default: () => <span>Directory</span>,
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({
    children,
    onPress,
    isDisabled,
  }: {
    children: ReactNode;
    onPress?: () => void;
    isDisabled?: boolean;
  }) => (
    <button disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Checkbox: ({
    children,
    isSelected,
    onValueChange,
  }: {
    children: ReactNode;
    isSelected: boolean;
    onValueChange: (value: boolean) => void;
  }) => (
    <label>
      <input
        type="checkbox"
        checked={isSelected}
        onChange={(event) => onValueChange(event.target.checked)}
      />
      {children}
    </label>
  ),
  Input: () => null,
  Switch: () => null,
  Modal: () => null,
}));

describe("cloud source workflow UI", () => {
  it("replaces a mirror choice while preserving independent content and submits explicit source IDs", () => {
    const ResumeForm = cloudDownloadActivities.find(
      (activity) => activity.kind === "download.selectSources",
    )!.ResumeForm!;
    const submit = vi.fn();
    render(
      <ResumeForm
        submitting={false}
        onSubmit={submit}
        promptJson={JSON.stringify({
          reason: "chooseSources",
          targets: [
            {
              id: "main",
              sources: [
                { id: "baidu", url: "https://pan.baidu.com/s/main" },
                { id: "google", url: "https://drive.google.com/folders/main" },
              ],
            },
            { id: "extras", sources: [{ id: "extra", url: "https://gofile.io/d/extra" }] },
          ],
        })}
      />,
    );
    const continueButton = screen.getByRole("button", { name: "cloudDownload.resume.continue" });
    expect(continueButton).toBeDisabled();
    fireEvent.click(screen.getByLabelText("https://pan.baidu.com/s/main"));
    fireEvent.click(screen.getByLabelText("https://gofile.io/d/extra"));
    fireEvent.click(screen.getByLabelText("https://drive.google.com/folders/main"));
    expect(screen.getByLabelText("https://pan.baidu.com/s/main")).not.toBeChecked();
    expect(screen.getByLabelText("https://gofile.io/d/extra")).toBeChecked();
    fireEvent.click(continueButton);
    expect(JSON.parse(submit.mock.calls[0][0])).toEqual({ sourceIds: ["extra", "google"] });
  });

  it("links cloud and native task results through the download-center ID contract", () => {
    render(
      <WorksetResults
        json={JSON.stringify([
          {
            targets: [
              { id: "cloud", title: "Cloud", downloadTaskId: 41, files: ["/downloads/a.zip"] },
              {
                id: "native",
                title: "HTTP",
                transportTaskId: "http:51",
                files: ["/downloads/b.zip"],
              },
            ],
          },
        ])}
      />,
    );
    expect(screen.getAllByRole("link").map((link) => link.getAttribute("href"))).toEqual([
      "#/downloader?id=site%3A41",
      "#/downloader?id=http%3A51",
    ]);
    expect(screen.getByText("/downloads/a.zip")).toBeInTheDocument();
  });

  it("exposes all six node forms and a managed source trigger with localized labels", () => {
    expect(DownloadSourcesTriggerUI.resolveOutputItemType({})).toBe("item.download.workset");
    expect(DownloadSourcesTriggerUI.runEntry?.path).toBe("/downloader");
    expect(DownloadSourcesTriggerUI.ManualRunForm).toBeUndefined();
    expect(cloudDownloadActivities).toHaveLength(6);
    for (const dictionary of [cn, en])
      for (const activity of cloudDownloadActivities) {
        expect(dictionary).toHaveProperty(activity.displayNameKey!);
        expect(activity.ConfigForm).toBeDefined();
        expect(activity.ResumeForm).toBeDefined();
      }
  });
});
