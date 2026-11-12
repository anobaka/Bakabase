import type { ReactNode } from "react";
import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import CreateDownloadTaskModal from "../components/CreateDownloadTaskModal";
import type { AcquirePreflight, AcquireRequest } from "@/components/Acquire/models";
import { useDependentComponentContextsStore } from "@/stores/dependentComponentContexts";

const api = vi.hoisted(() => ({ preflight: vi.fn(), create: vi.fn(), created: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({
  default: {
    downloadTask: { getAllDownloaderDefinitions: async () => ({ code: 0, data: [] }) },
    downloadCenter: {
      preflightDownloadCenterCloud: api.preflight,
      createDownloadCenterCloudTasks: api.create,
    },
  },
}));
vi.mock("@/stores/options", () => ({
  useDownloaderGlobalOptionsStore: (selector: (value: unknown) => unknown) =>
    selector({ data: { defaultDownloadPath: "/downloads" } }),
}));
vi.mock("@/components/Acquire/MethodPreparation", () => ({ default: () => null }));
vi.mock("../components/TaskDetailModal", () => ({ SiteDownloadForm: () => null }));
vi.mock("@/components/ThirdPartyIcon", () => ({ default: () => <svg /> }));
vi.mock("@/components/ThirdPartyConfig/base/DownloadDirectoryField", () => ({
  default: ({ value, onChange }: { value: string; onChange: (value: string) => void }) => (
    <input
      aria-label="directory"
      value={value}
      onChange={(event) => onChange(event.target.value)}
    />
  ),
}));
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
  Input: ({ label, value, onValueChange, isDisabled }: any) => (
    <input
      aria-label={label}
      value={value}
      disabled={isDisabled}
      onChange={(event) => onValueChange(event.target.value)}
    />
  ),
  Checkbox: ({ children, isSelected, onValueChange, isDisabled }: any) => (
    <label>
      <input
        type="checkbox"
        checked={isSelected}
        disabled={isDisabled}
        onChange={(event) => onValueChange(event.target.checked)}
      />
      {children}
    </label>
  ),
  Spinner: () => null,
  Chip: ({ children }: any) => <span>{children}</span>,
  Tooltip: ({ children }: any) => <>{children}</>,
}));

function ready(request: AcquireRequest): AcquirePreflight {
  return {
    mode: "files",
    canSubmit: !!request.candidates[0].url,
    requirements: [],
    candidates: request.candidates.map((candidate) => ({
      candidate,
      canSelect: true,
      state: "ready",
      requirements: [],
      methods: [
        {
          id: `${candidate.providerId}.download`,
          displayName: "download",
          providerId: candidate.providerId!,
          transport: "site",
          supported: true,
          ready: true,
          state: "ready",
          requirements: [],
          dependencyIds: [],
          supportsTransfer: false,
        },
      ],
    })),
  };
}
const submit = () => screen.getByRole("button", { name: "downloader.action.createTask" });
const urlInput = () => screen.getByLabelText("cloudDownload.url");
const submission = {
  code: 0,
  data: { mode: "files", runs: [], resourceIds: [], acquisitionTaskIds: [] },
};
beforeEach(() => {
  vi.clearAllMocks();
  useDependentComponentContextsStore.setState({ contexts: [] });
  api.preflight.mockImplementation(async (request: AcquireRequest) => ({
    code: 0,
    data: ready(request),
  }));
  api.create.mockResolvedValue(submission);
});

describe("download-center shell with the real cloud intake form", () => {
  it.each([
    ["pikPak", "pikpak", "https://mypikpak.com/s/public-share"],
    ["pixeldrain", "pixeldrain", "https://pixeldrain.com/u/public-file"],
    ["oneDrive", "oneDrive", "https://tenant.sharepoint.com/:f:/g/personal/public-folder"],
  ])("uses common download intake for the %s source", async (enumName, providerId, url) => {
    render(<CreateDownloadTaskModal onCreated={api.created} />);
    fireEvent.click(screen.getByText(`enum.thirdPartyId.${enumName}`));
    fireEvent.change(urlInput(), { target: { value: url } });
    await waitFor(() => expect(submit()).not.toBeDisabled());
    expect(screen.getAllByRole("dialog")).toHaveLength(1);
    // Account paths are an explicit alternative; choosing a source still defaults to public shares.
    expect(screen.getByLabelText("acquire.download.remote")).not.toBeChecked();
    fireEvent.click(submit());
    await waitFor(() =>
      expect(api.create).toHaveBeenCalledExactlyOnceWith(
        expect.objectContaining({
          mode: "files",
          candidates: [expect.objectContaining({ providerId, kind: "share", url })],
        }),
        { outputDirectory: "/downloads" },
      ),
    );
  });

  it("discards a previous provider's pending readiness and submits the new provider's download-only input", async () => {
    let finishOld!: (value: unknown) => void;
    api.preflight.mockImplementation((request: AcquireRequest) =>
      request.candidates[0].url.includes("old-drive")
        ? new Promise((resolve) => {
            finishOld = resolve;
          })
        : Promise.resolve({ code: 0, data: ready(request) }),
    );
    render(<CreateDownloadTaskModal onCreated={api.created} />);
    fireEvent.click(screen.getByText("enum.thirdPartyId.googleDrive"));
    fireEvent.change(urlInput(), {
      target: { value: "https://drive.google.com/file/d/old-drive/view" },
    });
    await waitFor(() => expect(finishOld).toBeTypeOf("function"));
    const oldRequest = api.preflight.mock.calls.find(
      ([request]) => request.candidates[0].providerId === "googleDrive",
    )![0];

    fireEvent.click(screen.getByText("enum.thirdPartyId.gofile"));
    expect(urlInput()).toHaveValue("");
    expect(submit()).toBeDisabled();
    await act(async () => finishOld({ code: 0, data: ready(oldRequest) }));
    expect(submit()).toBeDisabled();
    expect(screen.getAllByRole("dialog")).toHaveLength(1);
    expect(document.querySelectorAll("form")).toHaveLength(1);
    fireEvent.change(urlInput(), { target: { value: "https://gofile.io/d/new-folder" } });
    await waitFor(() => expect(submit()).not.toBeDisabled());
    fireEvent.click(submit());
    await waitFor(() => expect(api.created).toHaveBeenCalledOnce());
    expect(api.create).toHaveBeenCalledExactlyOnceWith(
      {
        mode: "files",
        title: undefined,
        accountId: undefined,
        candidates: [
          {
            id: "download",
            providerId: "gofile",
            kind: "share",
            url: "https://gofile.io/d/new-folder",
            accessCode: undefined,
          },
        ],
        selectedCandidateIds: ["download"],
      },
      { outputDirectory: "/downloads" },
    );
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("invalidates readiness when the directory changes and submits the freshly checked destination", async () => {
    let finishDirectory!: (value: unknown) => void;
    api.preflight.mockImplementation(
      (request: AcquireRequest, query: { outputDirectory: string }) =>
        query.outputDirectory === "/new-directory"
          ? new Promise((resolve) => {
              finishDirectory = resolve;
            })
          : Promise.resolve({ code: 0, data: ready(request) }),
    );
    render(<CreateDownloadTaskModal onCreated={api.created} />);
    fireEvent.click(screen.getByText("enum.thirdPartyId.googleDrive"));
    fireEvent.change(urlInput(), {
      target: { value: "https://drive.google.com/file/d/file/view" },
    });
    await waitFor(() => expect(submit()).not.toBeDisabled());
    fireEvent.change(screen.getByLabelText("directory"), { target: { value: "/new-directory" } });
    expect(submit()).toBeDisabled();
    fireEvent.submit(document.querySelector("form")!);
    expect(api.create).not.toHaveBeenCalled();
    await waitFor(() => expect(finishDirectory).toBeTypeOf("function"));
    const request = api.preflight.mock.calls.at(-1)![0];
    await act(async () => finishDirectory({ code: 0, data: ready(request) }));
    await waitFor(() => expect(submit()).not.toBeDisabled());
    fireEvent.click(submit());
    await waitFor(() =>
      expect(api.create).toHaveBeenCalledWith(request, { outputDirectory: "/new-directory" }),
    );
  });

  it("keeps the dialog open on submit rejection, locks switching while pending and rechecks before retry", async () => {
    let rejectSubmit!: (error: Error) => void;
    api.create.mockImplementation(
      () =>
        new Promise((_resolve, reject) => {
          rejectSubmit = reject;
        }),
    );
    render(<CreateDownloadTaskModal onCreated={api.created} />);
    fireEvent.click(screen.getByText("enum.thirdPartyId.googleDrive"));
    fireEvent.change(urlInput(), {
      target: { value: "https://drive.google.com/file/d/file/view" },
    });
    await waitFor(() => expect(submit()).not.toBeDisabled());
    fireEvent.click(submit());
    expect(screen.getByText("enum.thirdPartyId.gofile")).toBeDisabled();
    expect(screen.getByText("common.action.cancel")).toBeDisabled();
    fireEvent.submit(document.querySelector("form")!);
    expect(api.create).toHaveBeenCalledOnce();
    const checks = api.preflight.mock.calls.length;
    await act(async () => rejectSubmit(new Error("Account changed; check again")));
    expect(screen.getByRole("alert")).toHaveTextContent("Account changed; check again");
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(submit()).toBeDisabled();
    expect(api.created).not.toHaveBeenCalled();
    await waitFor(() => expect(api.preflight.mock.calls.length).toBeGreaterThan(checks));
    await waitFor(() => expect(submit()).not.toBeDisabled());
  });
});
