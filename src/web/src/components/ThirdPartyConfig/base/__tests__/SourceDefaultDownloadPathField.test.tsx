import type { ReactNode } from "react";

import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import SourceDefaultDownloadPathField from "../SourceDefaultDownloadPathField";

import { ThirdPartyId } from "@/sdk/constants";

const mocks = vi.hoisted(() => ({
  globalOptions: { data: {} as { defaultDownloadPath?: string } },
  getOptions: vi.fn(),
  onChange: vi.fn(),
  picker: vi.fn(),
}));

vi.mock("@/stores/options", () => ({
  useDownloaderGlobalOptionsStore: (selector: (state: typeof mocks.globalOptions) => unknown) =>
    selector(mocks.globalOptions),
}));
vi.mock("@/sdk/BApi", () => ({
  default: { downloadTask: { getDownloaderOptions: mocks.getOptions } },
}));
vi.mock("@/components/FileSystemSelector", () => ({
  FileSystemSelectorButton: ({
    fileSystemSelectorProps,
    children,
  }: {
    fileSystemSelectorProps: {
      defaultSelectedPath?: string;
      startPath?: string;
      onSelected: (entry: { path: string }) => void;
    };
    children?: ReactNode;
  }) => {
    mocks.picker(fileSystemSelectorProps);

    return (
      <button
        aria-label="choose-directory"
        onClick={() => fileSystemSelectorProps.onSelected({ path: "/custom" })}
      >
        {fileSystemSelectorProps.defaultSelectedPath || children}
      </button>
    );
  },
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({
    children,
    onPress,
    "aria-label": label,
  }: {
    children: ReactNode;
    onPress: () => void;
    "aria-label": string;
  }) => (
    <button aria-label={label} onClick={onPress}>
      {children}
    </button>
  ),
}));

beforeEach(() => {
  vi.clearAllMocks();
  mocks.globalOptions.data = { defaultDownloadPath: "C:\\Downloads" };
  mocks.getOptions.mockResolvedValue({
    data: { defaultPath: null, effectiveDefaultPath: "C:\\Downloads\\ExHentai" },
  });
});

describe("source default download directory", () => {
  it("shows the server-resolved inheritance without selecting or saving it as a source override", async () => {
    render(
      <SourceDefaultDownloadPathField
        thirdPartyId={ThirdPartyId.ExHentai}
        onChange={mocks.onChange}
      />,
    );

    await waitFor(() => expect(screen.getByText("C:\\Downloads\\ExHentai")).toBeInTheDocument());
    expect(mocks.getOptions).toHaveBeenCalledExactlyOnceWith(ThirdPartyId.ExHentai);
    expect(screen.getByRole("button", { name: "choose-directory" })).toHaveTextContent(
      "downloader.config.sourceDefaultPath.inherit",
    );
    expect(mocks.picker).toHaveBeenLastCalledWith(
      expect.objectContaining({ defaultSelectedPath: undefined, startPath: "C:\\Downloads" }),
    );
    expect(mocks.onChange).not.toHaveBeenCalled();
  });

  it("clears an explicit source directory and resumes inheritance without persisting the fallback", async () => {
    const { rerender } = render(
      <SourceDefaultDownloadPathField
        thirdPartyId={ThirdPartyId.ExHentai}
        value="/custom"
        onChange={mocks.onChange}
      />,
    );

    expect(mocks.getOptions).not.toHaveBeenCalled();
    fireEvent.click(
      screen.getByRole("button", { name: "downloader.config.sourceDefaultPath.reset" }),
    );
    expect(mocks.onChange).toHaveBeenCalledExactlyOnceWith("");
    rerender(
      <SourceDefaultDownloadPathField
        thirdPartyId={ThirdPartyId.ExHentai}
        value=""
        onChange={mocks.onChange}
      />,
    );

    await waitFor(() => expect(screen.getByText("C:\\Downloads\\ExHentai")).toBeInTheDocument());
    expect(
      screen.queryByRole("button", { name: "downloader.config.sourceDefaultPath.reset" }),
    ).toBeNull();
    expect(mocks.onChange).toHaveBeenCalledTimes(1);
  });

  it("refreshes the inherited destination when the global directory changes", async () => {
    const panel = (
      <SourceDefaultDownloadPathField
        thirdPartyId={ThirdPartyId.ExHentai}
        onChange={mocks.onChange}
      />
    );
    const { rerender } = render(panel);

    await waitFor(() => expect(screen.getByText("C:\\Downloads\\ExHentai")).toBeInTheDocument());
    mocks.globalOptions.data = { defaultDownloadPath: "/new" };
    mocks.getOptions.mockResolvedValue({ data: { effectiveDefaultPath: "/new/ExHentai" } });
    await act(async () =>
      rerender(
        <SourceDefaultDownloadPathField
          thirdPartyId={ThirdPartyId.ExHentai}
          onChange={mocks.onChange}
        />,
      ),
    );
    expect(screen.getByText("/new/ExHentai")).toBeInTheDocument();
    expect(screen.queryByText("C:\\Downloads\\ExHentai")).toBeNull();
    expect(mocks.onChange).not.toHaveBeenCalled();
  });
});
