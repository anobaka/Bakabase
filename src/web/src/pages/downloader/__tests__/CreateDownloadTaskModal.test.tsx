import type { ReactNode } from "react";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import CreateDownloadTaskModal from "../components/CreateDownloadTaskModal";
import { ThirdPartyId } from "@/sdk/constants";

const mocks = vi.hoisted(() => ({
  definitions: vi.fn(),
  create: vi.fn(),
  upload: vi.fn(),
  created: vi.fn(),
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    downloadTask: { getAllDownloaderDefinitions: mocks.definitions },
    downloadCenter: {
      createDownloadCenterTask: mocks.create,
      uploadDownloadCenterTorrent: mocks.upload,
    },
  },
}));
vi.mock("@/stores/options", () => ({
  useDownloaderGlobalOptionsStore: (selector: (value: unknown) => unknown) =>
    selector({ data: { defaultDownloadPath: "/output" } }),
}));
vi.mock("@/components/ThirdPartyIcon", () => ({
  default: ({ thirdPartyId }: { thirdPartyId: number }) => <svg data-site={thirdPartyId} />,
}));
vi.mock("@/components/ThirdPartyConfig/base/DownloadDirectoryField", () => ({
  default: ({ value, onChange }: any) => (
    <input
      aria-label="directory"
      value={value}
      onChange={(event) => onChange(event.target.value)}
    />
  ),
}));
vi.mock("@/components/Acquire/AcquireDownloadForm", async () => {
  const { useEffect } = await import("react");
  return {
    default: ({ providerId, formId, onStateChange, onCreated }: any) => {
      useEffect(() => onStateChange({ canSubmit: true, busy: false }), [onStateChange]);
      return (
        <form
          id={formId}
          data-testid="cloud-form"
          onSubmit={(event) => {
            event.preventDefault();
            onCreated();
          }}
        >
          download-only:{providerId}
        </form>
      );
    },
  };
});
vi.mock("../components/TaskDetailModal", async () => {
  const { useEffect } = await import("react");
  return {
    SiteDownloadForm: ({ thirdPartyId, formId, onStateChange }: any) => {
      useEffect(() => onStateChange({ canSubmit: false, busy: false }), [onStateChange]);
      return (
        <form id={formId} data-testid="site-form">
          site:{thirdPartyId}
        </form>
      );
    },
  };
});
vi.mock("@/components/bakaui", () => ({
  Modal: ({
    children,
    footer,
    visible,
  }: {
    children: ReactNode;
    footer: ReactNode;
    visible: boolean;
  }) =>
    visible ? (
      <section role="dialog">
        {children}
        {footer}
      </section>
    ) : null,
  Button: ({ children, onPress, isDisabled, isLoading, color, variant, size, ...props }: any) => (
    <button {...props} type={props.type ?? "button"} disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Input: ({ label, value, onValueChange, onChange, type, accept }: any) => (
    <input
      aria-label={label}
      type={type}
      accept={accept}
      value={value}
      onChange={(event) => {
        onChange?.(event);
        onValueChange?.(event.target.value);
      }}
    />
  ),
  Spinner: () => <span>loading-sites</span>,
  Chip: ({ children }: any) => <span>{children}</span>,
  Tooltip: ({ children }: any) => <>{children}</>,
}));

beforeEach(() => {
  vi.clearAllMocks();
  mocks.definitions.mockResolvedValue({
    code: 0,
    data: [
      { thirdPartyId: ThirdPartyId.ExHentai, taskType: 1, name: "ExHentai" },
      { thirdPartyId: ThirdPartyId.Bilibili, taskType: 1, name: "Bilibili" },
      { thirdPartyId: ThirdPartyId.Pixiv, taskType: 1, name: "Pixiv" },
      { thirdPartyId: ThirdPartyId.BaiduNetdisk, taskType: 1, name: "Baidu" },
    ],
  });
  mocks.create.mockResolvedValue({ code: 0 });
  mocks.upload.mockResolvedValue({ code: 0 });
});

describe("unified download creation", () => {
  it("shows all download sources as peers under one visible title with icons and one selection", async () => {
    render(<CreateDownloadTaskModal onCreated={mocks.created} />);
    await screen.findByText("enum.thirdPartyId.exHentai");
    const group = screen.getByRole("group", { name: "downloadCenter.add.source" });
    expect(screen.getByRole("heading", { name: "downloadCenter.add.source" })).toBeVisible();
    expect(screen.queryByRole("group", { name: "downloader.label.site" })).not.toBeInTheDocument();
    expect(within(group).queryByRole("group")).not.toBeInTheDocument();
    expect(screen.queryByText("downloader.label.site")).not.toBeInTheDocument();
    const methods = within(group).getAllByRole("button");
    expect(methods).toHaveLength(14);
    expect(
      within(group).getByRole("button", { name: /enum.thirdPartyId.teraBox/ }),
    ).toHaveTextContent("cloudDownload.integration.unavailable");
    methods.forEach((method) =>
      expect(method.querySelector("[data-download-method-icon] svg")).not.toBeNull(),
    );
    expect(within(group).getByRole("button", { pressed: true })).toHaveTextContent(
      "downloadCenter.provider.http",
    );
    fireEvent.click(within(group).getByRole("button", { name: "enum.thirdPartyId.baiduNetdisk" }));
    expect(within(group).getByRole("button", { pressed: true })).toHaveTextContent(
      "enum.thirdPartyId.baiduNetdisk",
    );
    expect(
      within(group).getByRole("button", { name: "downloadCenter.provider.http" }),
    ).toHaveAttribute("aria-pressed", "false");
    expect(screen.getAllByRole("dialog")).toHaveLength(1);
    expect(screen.queryByText("acquire.mode.library")).not.toBeInTheDocument();
    expect(screen.queryByText("acquire.mode.lead")).not.toBeInTheDocument();
    expect(screen.queryByText("cloudDownload.title")).not.toBeInTheDocument();
  });

  it("switches every cloud site inline with a fixed provider and keeps one form", async () => {
    render(<CreateDownloadTaskModal onCreated={mocks.created} />);
    await screen.findByText("enum.thirdPartyId.exHentai");
    for (const [label, provider] of [
      ["BaiduNetdisk", "baidu"],
      ["GoogleDrive", "googleDrive"],
      ["Gofile", "gofile"],
    ]) {
      fireEvent.click(
        screen.getByText(`enum.thirdPartyId.${label[0].toLowerCase()}${label.slice(1)}`),
      );
      expect(screen.getByTestId("cloud-form")).toHaveTextContent(`download-only:${provider}`);
      expect(document.querySelectorAll("form")).toHaveLength(1);
      expect(screen.getAllByRole("dialog")).toHaveLength(1);
    }
    fireEvent.click(screen.getByText("enum.thirdPartyId.exHentai"));
    expect(screen.getByTestId("site-form")).toHaveTextContent(`site:${ThirdPartyId.ExHentai}`);
    expect(screen.queryByTestId("cloud-form")).not.toBeInTheDocument();
    expect(screen.getByText("downloader.action.createTask")).toBeDisabled();
  });

  it("submits HTTP directly and prevents switching or duplicate submission while pending", async () => {
    let resolve!: (value: unknown) => void;
    mocks.create.mockReturnValue(
      new Promise((r) => {
        resolve = r;
      }),
    );
    render(<CreateDownloadTaskModal onCreated={mocks.created} />);
    await screen.findByText("enum.thirdPartyId.exHentai");
    fireEvent.change(screen.getByLabelText("downloadCenter.add.httpUrl"), {
      target: { value: "https://example.test/archive.zip" },
    });
    fireEvent.click(screen.getByText("downloader.action.createTask"));
    expect(mocks.create).toHaveBeenCalledExactlyOnceWith(
      {
        provider: "http",
        url: "https://example.test/archive.zip",
        name: undefined,
        fileName: undefined,
      },
      { outputDirectory: "/output" },
    );
    within(screen.getByRole("group", { name: "downloadCenter.add.source" }))
      .getAllByRole("button")
      .forEach((method) => expect(method).toBeDisabled());
    expect(screen.getByText("common.action.cancel")).toBeDisabled();
    fireEvent.click(screen.getByText("downloader.action.createTask"));
    expect(mocks.create).toHaveBeenCalledOnce();
    await act(async () => resolve({ code: 0 }));
    expect(mocks.created).toHaveBeenCalledOnce();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("resets source and validity when switching between HTTP, magnet and torrent file", async () => {
    render(<CreateDownloadTaskModal onCreated={mocks.created} />);
    await screen.findByText("enum.thirdPartyId.exHentai");
    fireEvent.change(screen.getByLabelText("downloadCenter.add.httpUrl"), {
      target: { value: "https://example.test/a.zip" },
    });
    fireEvent.click(screen.getByText("downloadCenter.provider.bt"));
    expect(screen.getByLabelText("bt.add.source")).toHaveValue("");
    expect(screen.getByText("downloader.action.createTask")).toBeDisabled();
    fireEvent.change(screen.getByLabelText("bt.add.source"), {
      target: { value: "magnet:?xt=urn:btih:abc" },
    });
    fireEvent.click(screen.getByRole("button", { name: "bt.add.file" }));
    expect(screen.getByText("downloader.action.createTask")).toBeDisabled();
    const file = new File(["torrent"], "a.torrent", { type: "application/x-bittorrent" });
    fireEvent.change(screen.getByLabelText("bt.add.file"), { target: { files: [file] } });
    fireEvent.click(screen.getByText("downloader.action.createTask"));
    await waitFor(() =>
      expect(mocks.upload).toHaveBeenCalledExactlyOnceWith({
        file,
        name: undefined,
        outputDirectory: "/output",
      }),
    );
    expect(mocks.create).not.toHaveBeenCalled();
  });

  it("shows a retryable site-list failure while general and cloud methods stay usable", async () => {
    mocks.definitions.mockRejectedValueOnce(new Error("sites unavailable"));
    render(<CreateDownloadTaskModal onCreated={mocks.created} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("sites unavailable");
    fireEvent.click(screen.getByText("enum.thirdPartyId.baiduNetdisk"));
    expect(screen.getByTestId("cloud-form")).toHaveTextContent("download-only:baidu");
    fireEvent.click(screen.getByText("common.action.retry"));
    await screen.findByText("enum.thirdPartyId.exHentai");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});
