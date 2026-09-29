import { useRef, useState } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";

import { useSectionFocusKeeper } from "../devices/useSectionFocusKeeper";

/*
 * When an action takes away the control that had the keyboard in a devices tab, focus goes
 * to the heading of the part of the tab it was in — never left on the page's body, and never
 * taken from where the reader put it.
 */

function Tab() {
  const panel = useRef<HTMLDivElement>(null);
  const heading = useRef<HTMLHeadingElement>(null);
  const [rows, setRows] = useState(["a", "b"]);
  const [busy, setBusy] = useState(false);

  useSectionFocusKeeper(panel, heading);

  return (
    <div ref={panel}>
      <h2 ref={heading} tabIndex={-1}>
        Tab
      </h2>
      <section data-focus-section>
        <h3 tabIndex={-1}>Part</h3>
        {rows.map((row) => (
          <button
            key={row}
            type="button"
            onClick={() => setRows((all) => all.filter((x) => x !== row))}
          >
            remove {row}
          </button>
        ))}
        <button disabled={busy} type="button" onClick={() => setBusy(true)}>
          work
        </button>
        <button type="button" onClick={() => setBusy(false)}>
          done
        </button>
      </section>
      <button type="button">elsewhere</button>
    </div>
  );
}

afterEach(cleanup);

describe("keeping the keyboard in a devices tab", () => {
  it("moves focus to the part's heading when the focused control is removed", async () => {
    render(<Tab />);
    const remove = screen.getByText("remove a");

    remove.focus();
    fireEvent.click(remove);
    await waitFor(() => expect(screen.getByText("Part")).toHaveFocus());
  });

  it("gives focus back to a control that was only disabled while its action ran", async () => {
    render(<Tab />);
    const work = screen.getByText("work");

    work.focus();
    fireEvent.click(work);
    // Chromium drops focus from a disabled control; jsdom does not, so the test does.
    act(() => work.blur());
    await act(async () => {
      fireEvent.click(screen.getByText("done"));
    });
    await waitFor(() => expect(work).toHaveFocus());
  });

  it("never pulls focus back once the reader pointed somewhere", async () => {
    render(<Tab />);
    const remove = screen.getByText("remove b");

    remove.focus();
    fireEvent.pointerDown(document.body);
    fireEvent.click(remove);
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 20));
    });
    expect(screen.getByText("Part")).not.toHaveFocus();
  });
});
