import { HeroUIProvider } from "@heroui/react";
import { act, cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it } from "vitest";

import PostParserTooltip from "../components/PostParserTooltip";

const show = () => {
  render(
    <HeroUIProvider>
      <PostParserTooltip content="Input: Downloaded files">
        <button type="button">Change extension to .7z</button>
      </PostParserTooltip>
    </HeroUIProvider>,
  );

  return userEvent.setup();
};

const advance = (milliseconds: number) =>
  act(() => new Promise<void>((resolve) => window.setTimeout(resolve, milliseconds)));

afterEach(cleanup);

describe("post parser tooltip interactions", () => {
  it("does not flash a tooltip when the pointer only passes over a step", async () => {
    const user = show();
    const trigger = screen.getByRole("button", { name: "Change extension to .7z" });

    await user.hover(trigger);
    await advance(100);
    expect(screen.queryByRole("tooltip")).not.toBeInTheDocument();
    await user.unhover(trigger);
    await advance(300);
    expect(screen.queryByRole("tooltip")).not.toBeInTheDocument();
  });

  it("stays open while reading its content and closes after leaving it", async () => {
    const user = show();
    const trigger = screen.getByRole("button", { name: "Change extension to .7z" });

    await user.hover(trigger);
    const tooltip = await screen.findByRole("tooltip");

    await waitFor(() => expect(tooltip).toBeVisible());
    expect(tooltip).toHaveTextContent("Input: Downloaded files");
    await user.hover(tooltip);
    await advance(300);
    expect(tooltip).toBeVisible();
    await user.unhover(tooltip);
    await advance(100);
    expect(tooltip).toBeVisible();
    await waitFor(() => expect(screen.queryByRole("tooltip")).not.toBeInTheDocument());
  });

  it("exposes the input description on keyboard focus and dismisses it with Escape", async () => {
    const user = show();
    const trigger = screen.getByRole("button", { name: "Change extension to .7z" });

    await user.tab();
    expect(trigger).toHaveFocus();
    await waitFor(() => expect(screen.getByRole("tooltip")).toBeVisible());
    expect(trigger).toHaveAccessibleDescription("Input: Downloaded files");
    await user.keyboard("{Escape}");
    await waitFor(() => expect(screen.queryByRole("tooltip")).not.toBeInTheDocument());
    expect(trigger).toHaveFocus();
  });
});
