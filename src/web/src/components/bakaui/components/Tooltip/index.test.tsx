import React, { createRef } from "react";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { HeroUIProvider } from "@heroui/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import Tooltip from "./index";

const renderTooltip = (element: React.ReactElement) => render(element, { wrapper: HeroUIProvider });

afterEach(cleanup);

describe("shared Tooltip", () => {
  it("keeps its anchor marked and the same overlay mounted when hovering the content", async () => {
    const user = userEvent.setup();

    renderTooltip(
      <Tooltip closeDelay={100} content="Play this resource">
        <button>Play</button>
      </Tooltip>,
    );
    const trigger = screen.getByRole("button", { name: "Play" });

    // Establish pointer modality before entering a trigger, as a real pointer
    // moving across the page does before React Aria's hover handler runs.
    await user.hover(document.body);
    await user.hover(trigger);
    const overlay = await screen.findByRole("tooltip");

    expect(trigger).toHaveAttribute("data-bakabase-tooltip-open", "true");
    expect(trigger).toHaveAttribute("aria-describedby", overlay.id);

    await user.hover(overlay);
    expect(screen.getByRole("tooltip")).toBe(overlay);
    expect(trigger).toHaveAttribute("data-bakabase-tooltip-open", "true");
    await user.unhover(overlay);
    await waitFor(() => expect(screen.queryByRole("tooltip")).not.toBeInTheDocument());
    expect(trigger).not.toHaveAttribute("data-bakabase-tooltip-open");
  });

  it("supports keyboard focus and removes the overlay immediately on Escape", async () => {
    const user = userEvent.setup();

    renderTooltip(
      <Tooltip content="Keyboard help">
        <button>Help</button>
      </Tooltip>,
    );
    await user.tab();
    await screen.findByRole("tooltip");
    const trigger = screen.getByRole("button", { name: "Help" });

    expect(trigger).toHaveAttribute("data-bakabase-tooltip-open", "true");
    await user.keyboard("{Escape}");
    expect(screen.queryByRole("tooltip")).not.toBeInTheDocument();
    expect(trigger).not.toHaveAttribute("data-bakabase-tooltip-open");
  });

  it("honors controlled state and forwards open changes", async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    const view = renderTooltip(
      <Tooltip content="Controlled help" isOpen={false} onOpenChange={onOpenChange}>
        <button>Help</button>
      </Tooltip>,
    );

    await user.hover(document.body);
    await user.hover(screen.getByRole("button"));
    expect(onOpenChange).toHaveBeenCalledWith(true);
    expect(screen.queryByRole("tooltip")).not.toBeInTheDocument();

    view.rerender(
      <Tooltip isOpen content="Controlled help" onOpenChange={onOpenChange}>
        <button>Help</button>
      </Tooltip>,
    );
    expect(screen.getByRole("tooltip")).toHaveTextContent("Controlled help");
    expect(screen.getByRole("button")).toHaveAttribute("data-bakabase-tooltip-open", "true");
    view.rerender(
      <Tooltip content="Controlled help" isOpen={false} onOpenChange={onOpenChange}>
        <button>Help</button>
      </Tooltip>,
    );
    expect(screen.queryByRole("tooltip")).not.toBeInTheDocument();
    expect(screen.getByRole("button")).not.toHaveAttribute("data-bakabase-tooltip-open");
  });

  it("supports default-open interactive content and disabled tooltips", async () => {
    const user = userEvent.setup();
    const action = vi.fn();
    const view = renderTooltip(
      <Tooltip defaultOpen content={<button onClick={action}>Preview action</button>}>
        <button>Preview</button>
      </Tooltip>,
    );

    expect(screen.getByRole("tooltip")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Preview action" }));
    expect(action).toHaveBeenCalledOnce();

    view.rerender(
      <Tooltip isDisabled content="Disabled">
        <button>Preview</button>
      </Tooltip>,
    );
    expect(screen.queryByRole("tooltip")).not.toBeInTheDocument();
    expect(screen.getByRole("button")).not.toHaveAttribute("data-bakabase-tooltip-open");
  });

  it("preserves the trigger ref and click handler without adding a wrapper", async () => {
    const user = userEvent.setup();
    const ref = createRef<HTMLButtonElement>();
    const onClick = vi.fn();
    const { container } = renderTooltip(
      <Tooltip content="Help">
        <button ref={ref} onClick={onClick}>
          Action
        </button>
      </Tooltip>,
    );
    const trigger = screen.getByRole("button");

    expect(ref.current).toBe(trigger);
    expect(container.querySelector("[data-overlay-container]")?.firstElementChild).toBe(trigger);
    await user.click(trigger);
    expect(onClick).toHaveBeenCalledOnce();
  });
});
