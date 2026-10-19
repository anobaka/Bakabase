import type { ReactNode } from "react";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import CloudDownloadModal from "../CloudDownloadModal";
import { PostParseTarget, PostParserSource } from "@/sdk/constants";
import type { PostParserTask } from "@/core/models/PostParserTask";

const api = vi.hoisted(() => ({
  startCloudDownload: vi.fn(),
  startCloudDownloadFromPost: vi.fn(),
  searchWorkflows: vi.fn(),
}));
vi.mock("@/sdk/BApi", () => ({ default: { cloudDownloads: api, workflow: api } }));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));
vi.mock("../AccountSelect", () => ({ default: () => null }));
vi.mock("@/stores/options", () => ({
  useDownloaderGlobalOptionsStore: (select: (value: unknown) => unknown) =>
    select({ data: { defaultDownloadPath: "/downloads" } }),
}));
vi.mock("@/components/ThirdPartyConfig/base/DownloadDirectoryField", () => ({
  default: () => null,
}));
vi.mock("@heroui/react", () => ({
  Select: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  SelectItem: () => null,
}));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ children, footer }: { children: ReactNode; footer: ReactNode }) => (
    <div>
      {children}
      {footer}
    </div>
  ),
  Button: ({
    children,
    onPress,
    isDisabled,
  }: {
    children: ReactNode;
    onPress: () => void;
    isDisabled?: boolean;
  }) => (
    <button disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
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
    <label>
      {label}
      <input value={value} onChange={(event) => onValueChange(event.target.value)} />
    </label>
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
  Spinner: () => null,
}));

beforeEach(() => {
  api.searchWorkflows.mockReset().mockResolvedValue({ code: 0, data: [] });
  api.startCloudDownload.mockReset().mockResolvedValue({ code: 0, data: { id: 15 } });
  api.startCloudDownloadFromPost.mockReset().mockResolvedValue({ code: 0, data: { id: 16 } });
});

describe("cloud download submission", () => {
  it("preserves share access information and passes the guarded output directory as a query parameter", async () => {
    render(<CloudDownloadModal />);
    fireEvent.change(screen.getByLabelText("cloudDownload.url"), {
      target: { value: "https://pan.baidu.com/s/example" },
    });
    fireEvent.change(screen.getByLabelText("cloudDownload.accessCode"), {
      target: { value: "abcd" },
    });
    fireEvent.click(screen.getByRole("button", { name: "cloudDownload.start" }));
    await waitFor(() => expect(api.startCloudDownload).toHaveBeenCalled());
    expect(api.startCloudDownload.mock.calls[0]).toEqual([
      expect.objectContaining({
        sources: [
          expect.objectContaining({ url: "https://pan.baidu.com/s/example", accessCode: "abcd" }),
        ],
      }),
      { outputDirectory: "/downloads" },
    ]);
    expect(await screen.findByRole("status")).toHaveTextContent("cloudDownload.started");
  });

  it("requires explicit selection and sends one representative index when the same link was parsed twice", async () => {
    const task: PostParserTask = {
      id: 9,
      source: PostParserSource.SoulPlus,
      link: "https://example.com/post/9",
      revision: 7,
      targets: [PostParseTarget.DownloadInfo],
      results: {
        [PostParseTarget.DownloadInfo]: {
          isComplete: true,
          resources: [
            { link: "https://gofile.io/d/duplicate" },
            { link: "https://gofile.io/d/duplicate" },
          ],
        },
      },
    };
    render(<CloudDownloadModal task={task} />);
    expect(screen.getByRole("button", { name: "cloudDownload.start" })).toBeDisabled();
    fireEvent.click(screen.getByLabelText("https://gofile.io/d/duplicate"));
    fireEvent.click(screen.getByRole("button", { name: "cloudDownload.start" }));
    await waitFor(() =>
      expect(api.startCloudDownloadFromPost).toHaveBeenCalledWith(
        expect.objectContaining({ taskId: 9, revision: 7, resourceIndices: [0] }),
        { outputDirectory: "/downloads" },
      ),
    );
  });
});
