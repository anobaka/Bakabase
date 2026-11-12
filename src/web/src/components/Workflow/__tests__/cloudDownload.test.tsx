import type { ReactNode } from "react";
import { fireEvent, render, screen, within } from "@testing-library/react";
import TaskContinuationModal from "@/components/Acquire/PlanTask/TaskContinuationModal";
import { describe, expect, it, vi } from "vitest";

import {
  cloudDownloadActivities,
  AcquisitionFetchSupportedUI,
  type CloudConfig,
} from "../Activities/CloudDownload";
import type { WorkflowActivityUI } from "../Activities/types";
import { DownloadSourcesTriggerUI } from "../Triggers/DownloadSources";
import { ResumeActivityContext } from "../ResumePresentation";
import WorksetResults from "@/components/CloudDownload/WorksetResults";
import cn from "@/locales/cn/pages/cloudDownload.json";
import en from "@/locales/en/pages/cloudDownload.json";

vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/i18n", () => ({
  default: { t: (key: string) => key },
  getEnumKey: (type: string, name: string) =>
    `enum.${type[0].toLowerCase() + type.slice(1)}.${name[0].toLowerCase() + name.slice(1)}`,
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));
vi.mock("@/components/ExternalLink", () => ({
  default: ({ children, href }: { children: ReactNode; href: string }) => (
    <a href={href}>{children}</a>
  ),
}));
vi.mock("@/components/CloudDownload/AccountSelect", () => ({
  default: () => <span>Account</span>,
}));
vi.mock("@/components/CloudDownload/RcloneAccountSelect", () => ({
  default: ({ providerId, onChange }: any) => (
    <button onClick={() => onChange(`${providerId}-account`)}>Choose {providerId} account</button>
  ),
}));
vi.mock("@/components/CloudDownload/RcloneAccountsPanel", () => ({
  default: () => <span>Rclone accounts</span>,
}));
vi.mock("@/components/ProxySelector/DownloadToolProxies", () => ({ default: () => null }));
vi.mock("@/components/ThirdPartyConfig/platforms/BaiduNetdiskConfig", () => ({
  default: () => <span>Accounts</span>,
}));
vi.mock("@/components/Dependency/DependencyManager", () => ({
  default: () => <span>Install</span>,
}));
vi.mock("@/components/ThirdPartyConfig/base/DownloadDirectoryField", () => ({
  default: () => <span>Directory</span>,
}));
vi.mock("@/components/ThirdPartyConfig/base/ProxyField", () => ({
  default: () => <span>Proxy</span>,
}));
vi.mock("@/components/bakaui", () => ({
  Accordion: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  AccordionItem: ({ children, title }: { children: ReactNode; title: ReactNode }) => (
    <section>
      {title}
      {children}
    </section>
  ),
  Button: ({ children, onPress, isDisabled, as, href, target, rel, ...props }: any) =>
    as === "a" ? (
      <a href={href} target={target} rel={rel}>
        {children}
      </a>
    ) : (
      <button
        aria-expanded={props["aria-expanded"]}
        aria-label={props["aria-label"]}
        disabled={isDisabled}
        onClick={onPress}
      >
        {children}
      </button>
    ),
  Checkbox: ({ children, isSelected, onValueChange, isDisabled, ...props }: any) => (
    <label>
      <input
        aria-label={props["aria-label"]}
        type="checkbox"
        checked={isSelected}
        disabled={isDisabled}
        onChange={(event) => onValueChange(event.target.checked)}
      />
      {children}
    </label>
  ),
  Input: () => null,
  Select: ({ label, selectedKeys, dataSource, isDisabled, onSelectionChange }: any) => (
    <select
      aria-label={label}
      value={selectedKeys[0] ?? ""}
      disabled={isDisabled}
      onChange={(event) => onSelectionChange(new Set([event.target.value]))}
    >
      {dataSource.map((item: any) => (
        <option key={item.value} value={item.value}>
          {item.label}
        </option>
      ))}
    </select>
  ),
  Spinner: () => null,
  Switch: ({
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
  Modal: ({ children, footer, title }: any) => <div role="dialog" aria-label={title}>{children}<footer>{footer}</footer></div>,
}));

describe("cloud source workflow UI", () => {
  it("keeps a storage-stopped download source and authorization with one outer submit footer", () => {
    const Form = cloudDownloadActivities.find(activity => activity.kind === "download.awaitCompletion")!.ResumeForm!;
    const submit = vi.fn();
    render(<TaskContinuationModal stale={false} onClose={vi.fn()} current={
      <ResumeActivityContext.Provider value="download.awaitCompletion">
        <Form fileTask submitting={false} onSubmit={submit} promptJson={JSON.stringify({ reason: "attention", targets: [{
          id: "part", title: "Current content", problem: "LOCAL_STORAGE_RESERVE_LOW", downloadTaskId: 8987,
          selectedSourceId: "source", sources: [{ id: "source", url: "https://pan.baidu.com/s/fixture" }],
        }] })} />
      </ResumeActivityContext.Provider>
    } />);
    expect(screen.getByText("Current content")).toBeVisible();
    expect(screen.getByRole("link", { name: /https:\/\/pan.baidu.com\/s\/fixture/ })).toBeVisible();
    expect(screen.queryByText("workflow.resume.guide.why")).toBeNull();
    expect(screen.queryByText("cloudDownload.resumeGuide.continueTitle")).toBeNull();
    const footer = within(screen.getByRole("dialog").querySelector("footer")!);
    const retry = footer.getByRole("button", { name: "cloudDownload.resumeGuide.retryDownload" });
    expect(footer.getAllByRole("button")).toHaveLength(2);
    expect(retry).toBeDisabled();
    fireEvent.click(screen.getByRole("checkbox", { name: "cloudDownload.resumeGuide.confirmDownloadRetry" }));
    fireEvent.click(retry);
    expect(submit).toHaveBeenCalledExactlyOnceWith("{}");
  });
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

  it("exposes all eight node forms and a managed source trigger with localized labels", () => {
    expect(DownloadSourcesTriggerUI.resolveOutputItemType({})).toBe("item.download.workset");
    expect(DownloadSourcesTriggerUI.runEntry?.path).toBe("/downloader");
    expect(DownloadSourcesTriggerUI.ManualRunForm).toBeUndefined();
    expect(cloudDownloadActivities).toHaveLength(8);
    for (const dictionary of [cn, en])
      for (const activity of cloudDownloadActivities) {
        expect(dictionary).toHaveProperty(activity.displayNameKey!);
        expect(activity.ConfigForm).toBeDefined();
        expect(activity.ResumeForm).toBeDefined();
      }
  });

  it("defaults download nodes to verified reuse and rejects an unknown policy", () => {
    for (const kind of ["baidu.download", "download.enqueueSupported"]) {
      const activity = cloudDownloadActivities.find(
        (item) => item.kind === kind,
      )! as WorkflowActivityUI<CloudConfig>;
      expect(activity.defaultConfig()).toMatchObject({ reusePolicy: "reuseVerified" });
      expect(activity.parseConfig("{}")).toMatchObject({ reusePolicy: "reuseVerified" });
      expect(activity.isValid(activity.parseConfig('{"reusePolicy":"force"}'))).toBe(true);
      expect(activity.isValid(activity.parseConfig('{"reusePolicy":"delete"}'))).toBe(false);
    }
    const activity = cloudDownloadActivities.find(
      (item) => item.kind === "download.enqueueSupported",
    )! as WorkflowActivityUI<CloudConfig>;
    const change = vi.fn();
    const Form = activity.ConfigForm;
    render(<Form value={activity.defaultConfig()} onChange={change} />);
    expect(screen.getByLabelText("cloudDownload.reuse.forceDownload")).not.toBeChecked();
    fireEvent.click(screen.getByLabelText("cloudDownload.reuse.forceDownload"));
    expect(change).toHaveBeenCalledWith({ reusePolicy: "force" });
  });

  it("configures the Baidu output layout while accepting existing configurations", () => {
    const activity = cloudDownloadActivities.find((item) => item.kind === "baidu.transfer")! as WorkflowActivityUI<CloudConfig>;
    expect(activity.isValid(activity.parseConfig("{}"))).toBe(true);
    expect(activity.isValid(activity.parseConfig('{"downloadLayout":"invalid"}'))).toBe(false);
    const change = vi.fn();
    const Form = activity.ConfigForm;
    render(<Form value={activity.defaultConfig()} onChange={change} />);
    const field = screen.getByLabelText("cloudDownload.downloadLayout.label");
    expect(field).toHaveValue("currentDirectory");
    expect(screen.getByRole("option", { name: "cloudDownload.downloadLayout.current" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "cloudDownload.downloadLayout.separate" })).toBeInTheDocument();
    fireEvent.change(field, { target: { value: "separateDirectory" } });
    expect(change).toHaveBeenCalledWith({ remoteDirectory: "/Bakabase", downloadLayout: "separateDirectory" });
  });

  it("opens dependency management at the matching provider component from a suspended run", () => {
    const ResumeForm = cloudDownloadActivities.find(
      (activity) => activity.kind === "baidu.transfer",
    )!.ResumeForm!;
    render(
      <ResumeForm
        submitting={false}
        onSubmit={vi.fn()}
        promptJson={JSON.stringify({
          reason: "cloudAttention",
          targets: ["baidu", "googleDrive", "gofile"].map((providerId) => ({
            id: providerId,
            selectedSourceId: providerId,
            sources: [{ id: providerId, providerId, url: "https://example.org/source" }],
          })),
        })}
      />,
    );
    screen
      .getAllByRole("button", { name: "acquire.preparation.open" })
      .forEach((button) => fireEvent.click(button));
    expect(
      screen
        .getAllByRole("link", { name: "menu.dependencies" })
        .map((link) => link.getAttribute("href")),
    ).toEqual([
      "#/configuration?tab=dependency&component=baidu-pcs-go-component-service",
      "#/configuration?tab=dependency&component=gdown-component-service",
      "#/configuration?tab=dependency&component=gallery-dl-component-service",
    ]);
    fireEvent.click(screen.getByRole("button", { name: "cloudDownload.resumeGuide.optionalSettings" }));
    expect(screen.getByRole("link", { name: "cloudDownload.dependencies" })).toHaveAttribute(
      "href",
      "#/configuration?tab=dependency",
    );
  });
});

it("resumes an account-remote source with a per-source account without changing a public sibling", () => {
  const ResumeForm = cloudDownloadActivities.find(
    (activity) => activity.kind === "download.enqueueSupported",
  )!.ResumeForm!;
  const submit = vi.fn();
  render(
    <ResumeForm
      submitting={false}
      onSubmit={submit}
      promptJson={JSON.stringify({
        reason: "cloudAttention",
        targets: [
          {
            id: "remote",
            selectedSourceId: "remote-source",
            sources: [
              {
                id: "remote-source",
                providerId: "oneDrive",
                kind: "remote",
                methodId: "oneDrive.rclone",
                url: "/folder",
              },
            ],
          },
          {
            id: "public",
            selectedSourceId: "public-source",
            sources: [
              {
                id: "public-source",
                providerId: "pikpak",
                kind: "share",
                methodId: "pikpak.share",
                url: "https://mypikpak.com/s/fixture",
              },
            ],
          },
        ],
      })}
    />,
  );
  fireEvent.click(screen.getByText("Choose oneDrive account"));
  expect(screen.queryByText("Choose pikpak account")).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "cloudDownload.resume.continue" }));
  expect(JSON.parse(submit.mock.calls[0][0])).toEqual({
    sourceAccounts: { "remote-source": "oneDrive-account" },
  });
});

it("registers acquisition shared fetching with the common account, reuse and resume protocol", () => {
  expect(AcquisitionFetchSupportedUI.kind).toBe("acquisition.fetchSupported");
  expect(AcquisitionFetchSupportedUI.defaultConfig()).toMatchObject({
    reusePolicy: "reuseVerified",
  });
  expect(AcquisitionFetchSupportedUI.ResumeForm).toBe(
    cloudDownloadActivities.find((item) => item.kind === "download.enqueueSupported")?.ResumeForm,
  );
  expect(cn).toHaveProperty(AcquisitionFetchSupportedUI.displayNameKey!);
  expect(en).toHaveProperty(AcquisitionFetchSupportedUI.displayNameKey!);
});

it("explains task 3's missing original without presenting login as a fix or silently restarting site:8470", () => {
  const Form = cloudDownloadActivities.find(
    (item) => item.kind === "download.awaitCompletion",
  )!.ResumeForm!;
  const submit = vi.fn();
  const promptJson = JSON.stringify({
    reason: "cloudAttention",
    targets: [
      {
        id: "0",
        title: "Content",
        selectedSourceId: "0",
        downloadTaskId: 8470,
        problem: "CloudProviderException: PIKPAK_ORIGINAL_FILE_UNAVAILABLE\n private-stack-value",
        sources: [
          { id: "0", providerId: "pikpak", kind: "share", url: "https://mypikpak.com/s/fixture" },
        ],
      },
    ],
  });
  render(
    <ResumeActivityContext.Provider value="download.awaitCompletion">
      <Form promptJson={promptJson} submitting={false} onSubmit={submit} />
    </ResumeActivityContext.Provider>,
  );
  expect(
    screen.getAllByText("cloudDownload.resumeGuide.originalUnavailable.why").length,
  ).toBeGreaterThan(0);
  expect(screen.getByText("cloudDownload.resumeGuide.originalUnavailable.action")).toBeVisible();
  expect(screen.getByRole("link", { name: "cloudDownload.resumeGuide.openTask" })).toHaveAttribute(
    "href",
    "#/downloader?id=site%3A8470",
  );
  expect(screen.queryByText(/private-stack-value/)).toBeNull();
  expect(screen.queryByText("Accounts")).toBeNull();
  const retry = screen.getByRole("button", { name: "cloudDownload.resumeGuide.retryDownload" });
  expect(retry).toBeDisabled();
  expect(submit).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "cloudDownload.resumeGuide.advancedRetry" }));
  fireEvent.click(
    screen.getByRole("checkbox", { name: "cloudDownload.resumeGuide.confirmUnavailableRetry" }),
  );
  expect(retry).toBeEnabled();
  fireEvent.click(retry);
  expect(submit).toHaveBeenCalledExactlyOnceWith("{}");
});

it.each([
  "LOCAL_STORAGE_FULL",
  "LOCAL_STORAGE_RESERVE_LOW",
  "LOCAL_STORAGE_WRITE_FAILED",
  "LOCAL_STORAGE_ACCESS_DENIED",
  "LOCAL_STORAGE_UNAVAILABLE",
  "BAIDU_OUTPUT_DIRECTORY_FAILED",
])("keeps download recovery explicit and retains the original task for %s", (problem) => {
  const Form = cloudDownloadActivities.find(
    (item) => item.kind === "download.awaitCompletion",
  )!.ResumeForm!;
  const submit = vi.fn();
  render(
    <ResumeActivityContext.Provider value="download.awaitCompletion">
      <Form
        promptJson={JSON.stringify({
          reason: "cloudAttention",
            targets: [{ id: "retained", downloadTaskId: 9024, problem, sources: [] }],
        })}
        submitting={false}
        onSubmit={submit}
      />
    </ResumeActivityContext.Provider>,
  );
  expect(screen.getByText(`cloudDownload.resumeGuide.${problem === "LOCAL_STORAGE_RESERVE_LOW" ? "storageReserve" : "localStorage"}.action`)).toBeVisible();
  expect(screen.queryByText("Accounts")).not.toBeInTheDocument();
  expect(screen.getByRole("link", { name: "cloudDownload.resumeGuide.openTask" })).toHaveAttribute(
    "href",
    "#/downloader?id=site%3A9024",
  );
  const retry = screen.getByRole("button", { name: "cloudDownload.resumeGuide.retryDownload" });
  expect(retry).toBeDisabled();
  expect(submit).not.toHaveBeenCalled();
  fireEvent.click(
    screen.getByRole("checkbox", { name: "cloudDownload.resumeGuide.confirmDownloadRetry" }),
  );
  fireEvent.click(retry);
  expect(submit).toHaveBeenCalledExactlyOnceWith("{}");
});

it("requires explicit transfer confirmation and resets approval when a different prompt arrives", () => {
  const Form = cloudDownloadActivities.find((item) => item.kind === "baidu.transfer")!.ResumeForm!;
  const submit = vi.fn();
  const renderForm = (target: string) => (
    <ResumeActivityContext.Provider value="baidu.transfer">
      <Form
        promptJson={JSON.stringify({
          reason: "cloudAttention",
          targets: [{ id: target, sources: [] }],
        })}
        submitting={false}
        onSubmit={submit}
      />
    </ResumeActivityContext.Provider>
  );
  const view = render(renderForm("one"));
  const button = () =>
    screen.getByRole("button", { name: "cloudDownload.resumeGuide.retryTransfer" });
  expect(button()).toBeDisabled();
  fireEvent.click(
    screen.getByRole("checkbox", { name: "cloudDownload.resumeGuide.confirmTransfer" }),
  );
  expect(button()).toBeEnabled();
  view.rerender(renderForm("two"));
  expect(button()).toBeDisabled();
  expect(submit).not.toHaveBeenCalled();
});

it("provides translated user guidance and consequences for every recovery form", () => {
  for (const dictionary of [cn, en]) {
    for (const mode of [
      "originalUnavailable",
      "chooseSources",
      "directory",
      "account",
      "dependency",
      "downloadStopped",
      "source",
      "password",
      "plan",
      "budget",
      "repeated",
      "shared",
      "processingUnknown",
      "publication",
      "cleanup",
    ]) {
      for (const role of ["why", "action", "next"])
        expect(dictionary).toHaveProperty(`cloudDownload.resumeGuide.${mode}.${role}`);
    }
  }
});

it("makes the affected share and access code immediately available for manual verification", () => {
  const Form = cloudDownloadActivities.find((item) => item.kind === "baidu.transfer")!.ResumeForm!;
  const submit = vi.fn();
  const url = "https://pan.baidu.com/s/review-share";
  render(
    <ResumeActivityContext.Provider value="baidu.transfer">
      <Form
        submitting={false}
        onSubmit={submit}
        promptJson={JSON.stringify({
          reason: "cloudAttention",
          targets: [
            {
              id: "main",
              problem: "SHARE_EXPIRED",
              selectedSourceId: "s1",
              sources: [{ id: "s1", providerId: "baidu", kind: "share", url, accessCode: "a1b2" }],
            },
          ],
        })}
      />
    </ResumeActivityContext.Provider>,
  );
  expect(screen.getByRole("link", { name: url })).toHaveAttribute("href", url);
  expect(screen.getByText("a1b2")).toBeVisible();
  expect(
    screen.getByRole("button", { name: "cloudDownload.resumeGuide.retryTransfer" }),
  ).toBeDisabled();
  expect(submit).not.toHaveBeenCalled();
});
