import type { DLsiteWork } from "../../types";

import { HeroUIProvider } from "@heroui/react";
import { act, fireEvent, render } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { DownloadButton } from "../DownloadButton";

const mocks = vi.hoisted(() => ({
  source: { data: {} as { defaultPath?: string } },
  global: { data: {} as { defaultDownloadPath?: string } },
  download: vi.fn(),
  onLocalPath: vi.fn(),
}));

vi.mock("@/stores/options", () => ({
  useDLsiteOptionsStore: (selector: (state: typeof mocks.source) => unknown) =>
    selector(mocks.source),
  useDownloaderGlobalOptionsStore: (selector: (state: typeof mocks.global) => unknown) =>
    selector(mocks.global),
}));
vi.mock("@/stores/bTasks", () => ({
  useBTasksStore: (selector: (state: { tasks: unknown[] }) => unknown) => selector({ tasks: [] }),
}));
vi.mock("@/sdk/BApi", () => ({
  default: { dlsiteWork: { downloadDLsiteWork: mocks.download } },
}));
vi.mock("@/components/bakaui/components/Tooltip", () => ({
  default: ({ children }: { children: React.ReactNode }) => children,
}));

const work = { workId: "RJ001", isDownloaded: false } as DLsiteWork;
const mount = () =>
  render(
    <HeroUIProvider>
      <DownloadButton work={work} onSetWorksLocalPath={mocks.onLocalPath} />
    </HeroUIProvider>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  mocks.source.data = {};
  mocks.global.data = {};
  mocks.download.mockResolvedValue({ code: 0, data: "/nas/Downloads/DLsite/RJ001" });
});

describe("DLsite download directory availability", () => {
  it("allows downloading with only a global default and uses the server's result", async () => {
    mocks.global.data = { defaultDownloadPath: "/nas/Downloads" };
    const { container } = mount();
    const button = container.querySelector("button")!;

    expect(button).not.toBeDisabled();
    await act(async () => fireEvent.click(button));
    expect(mocks.download).toHaveBeenCalledExactlyOnceWith("RJ001");
    expect(mocks.onLocalPath).toHaveBeenCalledExactlyOnceWith(
      "RJ001",
      "/nas/Downloads/DLsite/RJ001",
    );
  });

  it("requires a directory when neither source nor downloader has a default", () => {
    const { container } = mount();

    expect(container.querySelector("button")).toBeDisabled();
    expect(mocks.download).not.toHaveBeenCalled();
  });
});
