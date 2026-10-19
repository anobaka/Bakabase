import type { ReactNode } from "react";

import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import AddGeneralDownloadModal from "../components/AddGeneralDownloadModal";

const mocks = vi.hoisted(() => ({ create: vi.fn(), upload: vi.fn(), created: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({
  default: {
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
    onOk,
    footer,
  }: {
    children: ReactNode;
    onOk: () => Promise<void>;
    footer: { okProps: { isDisabled: boolean } };
  }) => (
    <div>
      {children}
      <button
        disabled={footer.okProps.isDisabled}
        onClick={() => {
          void onOk().catch(() => undefined);
        }}
      >
        submit
      </button>
    </div>
  ),
  Input: ({
    label,
    value,
    onValueChange,
  }: {
    label: string;
    value: string;
    onValueChange: (value: string) => void;
  }) => (
    <input
      aria-label={label}
      value={value}
      onChange={(event) => onValueChange(event.target.value)}
    />
  ),
  Tabs: ({
    children,
    onSelectionChange,
  }: {
    children: ReactNode;
    onSelectionChange: (value: string) => void;
  }) => (
    <div>
      <button onClick={() => onSelectionChange("file")}>file-mode</button>
      <button onClick={() => onSelectionChange("http")}>http-mode</button>
      {children}
    </div>
  ),
  Tab: ({ children }: { children: ReactNode }) => <div>{children}</div>,
}));

beforeEach(() => {
  vi.clearAllMocks();
  mocks.create.mockResolvedValue({ code: 0 });
  mocks.upload.mockResolvedValue({ code: 0 });
});

describe("general download creation", () => {
  it.each([
    ["magnet:?xt=urn:btih:abc", { magnetUri: "magnet:?xt=urn:btih:abc" }],
    [
      "https://example.test/gallery.torrent",
      { torrentUrl: "https://example.test/gallery.torrent" },
    ],
  ])("passes the server output directory separately from source %s", async (source, body) => {
    render(<AddGeneralDownloadModal onCreated={mocks.created} />);
    fireEvent.change(screen.getByLabelText("bt.add.source"), { target: { value: ` ${source} ` } });
    fireEvent.change(screen.getByLabelText("directory"), {
      target: { value: " /downloads/gallery " },
    });
    fireEvent.click(screen.getByText("submit"));
    await waitFor(() => expect(mocks.created).toHaveBeenCalledOnce());
    expect(mocks.create).toHaveBeenCalledWith(
      { ...body, provider: "bt", name: undefined },
      { outputDirectory: "/downloads/gallery" },
    );
    expect(mocks.upload).not.toHaveBeenCalled();
  });

  it("creates HTTP files through the common task API with a destination and optional file name", async () => {
    render(<AddGeneralDownloadModal onCreated={mocks.created} />);
    fireEvent.click(screen.getByText("http-mode"));
    fireEvent.change(screen.getByLabelText("downloadCenter.add.httpUrl"), {
      target: { value: "https://example.test/archive.zip" },
    });
    fireEvent.change(screen.getByLabelText("downloadCenter.add.fileName"), {
      target: { value: " archive.zip " },
    });
    fireEvent.click(screen.getByText("submit"));
    await waitFor(() =>
      expect(mocks.create).toHaveBeenCalledWith(
        {
          provider: "http",
          name: undefined,
          url: "https://example.test/archive.zip",
          fileName: "archive.zip",
        },
        { outputDirectory: "/output" },
      ),
    );
  });
  it("does not accept a magnet as an HTTP URL or a missing server directory", () => {
    render(<AddGeneralDownloadModal onCreated={mocks.created} />);
    fireEvent.click(screen.getByText("http-mode"));
    fireEvent.change(screen.getByLabelText("downloadCenter.add.httpUrl"), {
      target: { value: "magnet:?xt=urn:btih:abc" },
    });
    expect(screen.getByText("submit")).toBeDisabled();
    fireEvent.change(screen.getByLabelText("downloadCenter.add.httpUrl"), {
      target: { value: "https://example.test/archive.zip" },
    });
    fireEvent.change(screen.getByLabelText("directory"), { target: { value: "" } });
    expect(screen.getByText("submit")).toBeDisabled();
  });
  it("uploads torrent bytes with the selected server directory", async () => {
    const file = new File(["torrent"], "gallery.torrent", { type: "application/x-bittorrent" });
    render(<AddGeneralDownloadModal onCreated={mocks.created} />);
    fireEvent.click(screen.getByText("file-mode"));
    fireEvent.change(screen.getByLabelText("bt.add.file"), { target: { files: [file] } });
    fireEvent.click(screen.getByText("submit"));
    await waitFor(() =>
      expect(mocks.upload).toHaveBeenCalledWith({
        file,
        name: undefined,
        outputDirectory: "/output",
      }),
    );
    expect(mocks.create).not.toHaveBeenCalled();
  });

  it("keeps the form open with the server error when creation is rejected", async () => {
    mocks.create.mockResolvedValue({ code: 400, message: "Missing output directory" });
    render(<AddGeneralDownloadModal onCreated={mocks.created} />);
    fireEvent.change(screen.getByLabelText("bt.add.source"), {
      target: { value: "https://example.test/gallery.torrent" },
    });
    fireEvent.click(screen.getByText("submit"));
    expect(await screen.findByRole("alert")).toHaveTextContent("Missing output directory");
    expect(mocks.created).not.toHaveBeenCalled();
  });
});
