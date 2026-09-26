import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { useState } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";

import DataSyncDialog from "../components/DataSyncDialog";

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string) => key,
    i18n: { language: "en", changeLanguage: vi.fn(), exists: () => false },
  }),
  initReactI18next: { type: "3rdParty", init: vi.fn() },
}));
vi.mock("@/components/HelpCenter/HelpCenterButton", () => ({
  default: () => <span data-testid="help" />,
}));

afterEach(cleanup);

/**
 * A page with a control that opens the dialog and, like the review's link once the first sync is
 * done, goes away while the dialog is open; and a heading of the details it was in.
 */
function Page() {
  const [open, setOpen] = useState(false);
  const [opener, setOpener] = useState(true);

  return (
    <div>
      <h2 data-testid="details-heading" tabIndex={-1}>
        Details
      </h2>
      {opener && (
        <button type="button" onClick={() => setOpen(true)}>
          Review
        </button>
      )}
      <button type="button" onClick={() => setOpener(false)}>
        Take the opener away
      </button>
      {open && (
        <DataSyncDialog
          returnFocus={() => document.querySelector<HTMLElement>('[data-testid="details-heading"]')}
          title="The first sync"
          onClose={() => setOpen(false)}
        >
          <button type="button" onClick={() => setOpener(false)}>
            Apply
          </button>
        </DataSyncDialog>
      )}
    </div>
  );
}

describe("data sync's own dialogs", () => {
  it("give the keyboard back to what opened them", () => {
    render(<Page />);
    const opener = screen.getByText("Review");

    act(() => opener.focus());
    fireEvent.click(opener);
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    fireEvent.keyDown(document, { key: "Escape" });

    expect(screen.queryByRole("dialog")).toBeNull();
    expect(opener).toHaveFocus();
  });

  it("give it where they are told when what opened them went away meanwhile", () => {
    render(<Page />);
    const opener = screen.getByText("Review");

    act(() => opener.focus());
    fireEvent.click(opener);
    // The first sync is applied: the link that opened the review is taken away.
    fireEvent.click(screen.getByText("Apply"));
    expect(screen.queryByText("Review")).toBeNull();
    fireEvent.keyDown(document, { key: "Escape" });

    expect(screen.queryByRole("dialog")).toBeNull();
    expect(screen.getByTestId("details-heading")).toHaveFocus();
  });
});
