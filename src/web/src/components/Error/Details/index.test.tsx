import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import ErrorDetails from "./index";

import { copyTextToClipboard } from "@/core/clipboard";

vi.mock("@/core/clipboard", () => ({ copyTextToClipboard: vi.fn() }));

describe("ErrorDetails", () => {
  beforeEach(() => {
    vi.mocked(copyTextToClipboard).mockReset();
  });

  it("keeps the complete error readable and copies it without truncation", async () => {
    const text = Array.from({ length: 500 }, (_, i) => `Error line ${i}`).join("\n");

    vi.mocked(copyTextToClipboard).mockResolvedValue(undefined);
    render(<ErrorDetails text={text} />);

    expect(screen.getByRole("region").textContent?.trim()).toBe(text);
    expect(screen.getByRole("region")).toHaveAttribute("tabindex", "0");
    fireEvent.click(screen.getByRole("button", { name: "error.details.copy" }));
    await screen.findByRole("button", { name: "error.details.copied" });
    expect(copyTextToClipboard).toHaveBeenCalledWith(text);
  });

  it("leaves the error available for manual selection when copying fails", async () => {
    vi.mocked(copyTextToClipboard).mockRejectedValue(new Error("Clipboard unavailable"));
    render(<ErrorDetails text="Full error" />);

    fireEvent.click(screen.getByRole("button", { name: "error.details.copy" }));
    expect(await screen.findByRole("status")).toHaveTextContent("error.details.copyFailed");
    expect(screen.getByRole("region")).toHaveTextContent("Full error");
  });

  it("does not show the previous copy confirmation for a new error", async () => {
    vi.mocked(copyTextToClipboard).mockResolvedValue(undefined);
    const { rerender } = render(<ErrorDetails text="First error" />);

    fireEvent.click(screen.getByRole("button", { name: "error.details.copy" }));
    await screen.findByRole("button", { name: "error.details.copied" });
    rerender(<ErrorDetails text="Second error" />);
    fireEvent.click(screen.getByRole("button", { name: "error.details.copy" }));
    await waitFor(() => expect(copyTextToClipboard).toHaveBeenLastCalledWith("Second error"));
  });
});
