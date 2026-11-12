import type { ReactNode } from "react";

import { HeroUIProvider } from "@heroui/react";
import { act, cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import AutoAcquisitionConfiguration from "../components/AutoAcquisitionConfiguration";
import { useDownloaderGlobalOptionsStore, useThirdPartyOptionsStore } from "@/stores/options";

vi.mock("@/pages/acquisition/components/AcquisitionSettingsModal", () => ({ default: () => null }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));

const api = vi.hoisted(() => ({ patch: vi.fn() }));
vi.mock("@/sdk/BApi", () => ({ default: { options: { patchThirdPartyOptions: api.patch } } }));
vi.mock("@/components/bakaui", async () => await import("@heroui/react"));
vi.mock("@/components/FileSystemSelector", async () => {
  const { Button } = await import("@heroui/react");

  return {
    FileSystemSelectorButton: ({
      fileSystemSelectorProps,
      children,
      ...props
    }: {
      fileSystemSelectorProps: {
        defaultSelectedPath?: string;
        onSelected: (entry: { path: string }) => void;
      };
      children: ReactNode;
    }) => (
      <Button
        {...props}
        onPress={() => fileSystemSelectorProps.onSelected({ path: "/downloads/posts" })}
      >
        {fileSystemSelectorProps.defaultSelectedPath || children}
      </Button>
    ),
  };
});

const enableLabel = "postParser.config.acquisition.enabled";
const saveLabel = "postParser.config.acquisition.save";
const directoryLabel = "postParser.config.acquisition.directory";
const show = () => {
  render(
    <HeroUIProvider disableAnimation>
      <AutoAcquisitionConfiguration />
    </HeroUIProvider>,
  );
  return userEvent.setup();
};

beforeEach(() => {
  api.patch.mockReset().mockResolvedValue({ code: 0 });
  useThirdPartyOptionsStore.setState({
    data: {
      automaticallyParsingPosts: false,
      postParserMaxConcurrency: 10,
      postParserAiMaxConcurrency: 1,
      postParserAcquisition: { enabled: false },
    },
  });
  useDownloaderGlobalOptionsStore.setState({ data: { autoStartAfterCreation: false } });
});
afterEach(cleanup);

describe("automatic acquisition settings", () => {
  it("defaults to off and requires a destination only when enabling", async () => {
    const user = show();

    expect(screen.getByRole("checkbox", { name: enableLabel })).not.toBeChecked();
    expect(screen.getByRole("button", { name: saveLabel })).not.toBeDisabled();
    await user.click(screen.getByRole("checkbox", { name: enableLabel }));
    expect(screen.getByRole("button", { name: saveLabel })).toBeDisabled();
    expect(screen.getByRole("status")).toHaveTextContent(
      "postParser.config.acquisition.directoryRequired",
    );
    expect(api.patch).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: directoryLabel }));
    expect(screen.getByRole("button", { name: directoryLabel })).toHaveTextContent(
      "/downloads/posts",
    );
    await user.click(screen.getByRole("button", { name: saveLabel }));
    await waitFor(() =>
      expect(api.patch).toHaveBeenCalledExactlyOnceWith({
        postParserAcquisition: { enabled: true, outputDirectory: "/downloads/posts" },
      }),
    );
    expect(useThirdPartyOptionsStore.getState().data.postParserAcquisition?.enabled).toBe(true);
  });

  it("inherits the global directory without freezing it into the override", async () => {
    useDownloaderGlobalOptionsStore.setState({
      data: { autoStartAfterCreation: false, defaultDownloadPath: "/downloads" },
    });
    const user = show();

    expect(
      screen.getByText("postParser.config.acquisition.inheritedDirectory"),
    ).toBeInTheDocument();
    await user.click(screen.getByRole("checkbox", { name: enableLabel }));
    await user.click(screen.getByRole("button", { name: saveLabel }));
    expect(api.patch).toHaveBeenCalledExactlyOnceWith({
      postParserAcquisition: { enabled: true, outputDirectory: "" },
    });
  });

  it("can restore inheritance and save a disabled configuration without a directory", async () => {
    useThirdPartyOptionsStore.setState({
      data: {
        automaticallyParsingPosts: false,
        postParserMaxConcurrency: 10,
        postParserAiMaxConcurrency: 1,
        postParserAcquisition: { enabled: true, outputDirectory: "/downloads/old" },
      },
    });
    const user = show();

    await user.click(
      screen.getByRole("button", { name: "postParser.config.acquisition.useDefault" }),
    );
    await user.click(screen.getByRole("checkbox", { name: enableLabel }));
    await user.click(screen.getByRole("button", { name: saveLabel }));
    expect(api.patch).toHaveBeenCalledExactlyOnceWith({
      postParserAcquisition: { enabled: false, outputDirectory: "" },
    });
  });

  it.each(["response", "network"])(
    "retains saved settings on %s failure and permits retry",
    async (type) => {
      useDownloaderGlobalOptionsStore.setState({
        data: { autoStartAfterCreation: false, defaultDownloadPath: "/downloads" },
      });
      if (type === "response")
        api.patch.mockResolvedValueOnce({ code: 400, message: "Directory rejected" });
      else api.patch.mockRejectedValueOnce(new Error("Directory rejected"));
      const user = show();

      await user.click(screen.getByRole("checkbox", { name: enableLabel }));
      await user.click(screen.getByRole("button", { name: saveLabel }));
      expect(await screen.findByRole("alert")).toHaveTextContent("Directory rejected");
      expect(useThirdPartyOptionsStore.getState().data.postParserAcquisition?.enabled).toBe(false);
      await user.click(screen.getByRole("button", { name: saveLabel }));
      await waitFor(() =>
        expect(useThirdPartyOptionsStore.getState().data.postParserAcquisition?.enabled).toBe(true),
      );
      expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    },
  );

  it("locks all draft controls while saving", async () => {
    let complete!: (response: { code: number }) => void;
    api.patch.mockReturnValueOnce(
      new Promise((resolve) => {
        complete = resolve;
      }),
    );
    const user = show();
    const saveButton = screen.getByRole("button", { name: saveLabel });

    await user.click(saveButton);
    expect(screen.getByRole("checkbox", { name: enableLabel })).toBeDisabled();
    expect(screen.getByRole("button", { name: directoryLabel })).toBeDisabled();
    expect(saveButton).toBeDisabled();
    await user.click(saveButton);
    expect(api.patch).toHaveBeenCalledTimes(1);
    await act(async () => complete({ code: 0 }));
    expect(saveButton).not.toBeDisabled();
  });
});
