import type { ReactNode } from "react";

import { HeroUIProvider } from "@heroui/react";
import { act, cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ConfigurationModal from "../components/ConfigurationModal";

import { useThirdPartyOptionsStore } from "@/stores/options";

const api = vi.hoisted(() => ({ patchOptions: vi.fn(), startAll: vi.fn() }));

vi.mock("@/sdk/BApi", () => ({
  default: {
    options: { patchThirdPartyOptions: api.patchOptions },
    postParser: { startAllPostParserTasks: api.startAll },
  },
}));
vi.mock("@/components/bakaui", async () => ({
  ...(await import("@heroui/react")),
  Modal: ({ children, title }: { children: ReactNode; title: ReactNode }) => (
    <div role="dialog">
      <h2>{title}</h2>
      {children}
    </div>
  ),
}));
vi.mock("@/components/AiProviderPanel", () => ({ default: () => null }));
vi.mock("@/components/AiFeaturePanel", () => ({ default: () => null }));
vi.mock("@/components/ThirdPartyConfig/platforms/SoulPlusConfig", () => ({
  SoulPlusConfigField: { Accounts: "accounts", Other: "other" },
  SoulPlusConfigPanel: () => null,
}));

const show = (automaticallyParsingPosts = false) => {
  useThirdPartyOptionsStore.setState({ data: { automaticallyParsingPosts } });
  render(
    <HeroUIProvider disableAnimation>
      <ConfigurationModal onDestroyed={vi.fn()} />
    </HeroUIProvider>,
  );

  return {
    user: userEvent.setup(),
    checkbox: screen.getByRole("checkbox", { name: "postParser.label.automaticallyParsing" }),
  };
};

beforeEach(() => {
  api.patchOptions.mockReset().mockResolvedValue({ code: 0 });
  api.startAll.mockReset();
});
afterEach(cleanup);

describe("post parser automatic parsing configuration", () => {
  it.each([false, true])(
    "saves a change from %s through options without starting existing tasks",
    async (originalValue) => {
      const { user, checkbox } = show(originalValue);

      expect(screen.getByRole("tab", { name: "postParser.config.general" })).toHaveAttribute(
        "aria-selected",
        "true",
      );
      await user.click(checkbox);
      await waitFor(() =>
        expect(useThirdPartyOptionsStore.getState().data.automaticallyParsingPosts).toBe(
          !originalValue,
        ),
      );
      expect(api.patchOptions).toHaveBeenCalledExactlyOnceWith({
        automaticallyParsingPosts: !originalValue,
      });
      expect(checkbox).toHaveProperty("checked", !originalValue);
      expect(checkbox).not.toBeDisabled();
      expect(api.startAll).not.toHaveBeenCalled();

      await user.click(screen.getByRole("tab", { name: "postParser.config.ai" }));
      await user.click(screen.getByRole("tab", { name: "postParser.config.general" }));
      expect(
        screen.getByRole("checkbox", { name: "postParser.label.automaticallyParsing" }),
      ).toHaveProperty("checked", !originalValue);
      expect(api.patchOptions).toHaveBeenCalledTimes(1);
    },
  );

  it.each(["response", "network"])(
    "shows the %s failure, retains the saved value, and permits retry",
    async (failureType) => {
      const reason = "Settings could not be saved";

      if (failureType === "response") {
        api.patchOptions.mockResolvedValueOnce({ code: 400, message: reason });
      } else {
        api.patchOptions.mockRejectedValueOnce(new Error(reason));
      }
      const { user, checkbox } = show(true);

      await user.click(checkbox);
      expect(await screen.findByRole("alert")).toHaveTextContent(reason);
      expect(checkbox).toBeChecked();
      expect(checkbox).not.toBeDisabled();
      expect(useThirdPartyOptionsStore.getState().data.automaticallyParsingPosts).toBe(true);
      expect(api.startAll).not.toHaveBeenCalled();

      await user.click(checkbox);
      await waitFor(() => expect(checkbox).not.toBeChecked());
      expect(screen.queryByRole("alert")).not.toBeInTheDocument();
      expect(useThirdPartyOptionsStore.getState().data.automaticallyParsingPosts).toBe(false);
      expect(api.patchOptions).toHaveBeenCalledTimes(2);
      expect(api.patchOptions).toHaveBeenNthCalledWith(1, { automaticallyParsingPosts: false });
      expect(api.patchOptions).toHaveBeenNthCalledWith(2, { automaticallyParsingPosts: false });
      expect(api.startAll).not.toHaveBeenCalled();
    },
  );

  it("ignores repeated activation while a save is pending", async () => {
    let finishSave!: (response: { code: number }) => void;

    api.patchOptions.mockReturnValueOnce(
      new Promise<{ code: number }>((resolve) => {
        finishSave = resolve;
      }),
    );
    const { user, checkbox } = show();

    await user.dblClick(checkbox);
    expect(checkbox).toBeDisabled();
    expect(checkbox).not.toBeChecked();
    await user.click(checkbox);
    await user.keyboard(" ");
    expect(api.patchOptions).toHaveBeenCalledExactlyOnceWith({ automaticallyParsingPosts: true });
    expect(useThirdPartyOptionsStore.getState().data.automaticallyParsingPosts).toBe(false);

    await act(async () => finishSave({ code: 0 }));
    expect(checkbox).toBeChecked();
    expect(checkbox).not.toBeDisabled();
    expect(api.startAll).not.toHaveBeenCalled();

    await user.click(checkbox);
    await waitFor(() => expect(checkbox).not.toBeChecked());
    expect(api.patchOptions).toHaveBeenCalledTimes(2);
    expect(api.patchOptions).toHaveBeenLastCalledWith({ automaticallyParsingPosts: false });
    expect(api.startAll).not.toHaveBeenCalled();
  });
});
