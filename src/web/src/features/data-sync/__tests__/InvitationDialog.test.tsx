import type * as Api from "../api";

import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import InvitationDialog from "../components/InvitationDialog";
import { dataSyncApi } from "../api";
import { useDataSyncActions } from "../hooks/useDataSyncActions";

import { blurWhenDisabled } from "./blurWhenDisabled";
import { minutesAhead, NOW } from "./dataSyncFixtures";

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: Record<string, unknown>) =>
      options
        ? [
            key,
            ...Object.entries(options)
              .filter(([name, value]) => name !== "defaultValue" && value !== undefined)
              .map(([, value]) => String(value)),
          ].join(" ")
        : key,
    i18n: { language: "en", changeLanguage: vi.fn(), exists: () => false },
  }),
  initReactI18next: { type: "3rdParty", init: vi.fn() },
}));
vi.mock("../api", async (importOriginal) => ({
  ...(await importOriginal<typeof Api>()),
  dataSyncApi: {
    createInvitation: vi.fn(),
  },
}));
vi.mock("@/components/HelpCenter/HelpCenterButton", () => ({
  default: ({ section, topic }: { section: string; topic: string }) => (
    <span data-help={`${topic}/${section}`} data-testid="help" />
  ),
}));

const open = () => {
  const Host = () => {
    const actions = useDataSyncActions(() => undefined);

    return <InvitationDialog actions={actions} forName="NAS" now={NOW} onClose={vi.fn()} />;
  };

  render(<Host />);
};

let letGo: (() => void) | undefined;

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(dataSyncApi.createInvitation).mockResolvedValue({
    code: "48213957",
    expiresAt: minutesAhead(20),
    addresses: ["http://192.168.1.10:34567"],
    allowTwoWay: false,
  });
});
afterEach(() => {
  letGo?.();
  letGo = undefined;
  cleanup();
});

describe("a one-time code", () => {
  it("keeps the keyboard in the dialog while the code is made, and says the code", async () => {
    open();
    const dialog = screen.getByRole("dialog");
    const status = within(dialog).getByTestId("data-sync-invitation-status");

    // A live region before there is anything to say.
    expect(status).toHaveAttribute("role", "status");
    expect(status).toBeEmptyDOMElement();
    const create = within(dialog).getByTestId("data-sync-invitation-create");

    act(() => create.focus());
    // The browser takes focus off the button once it is disabled while the code is made.
    letGo = blurWhenDisabled();
    await act(async () => {
      fireEvent.click(create);
    });

    expect(within(status).getByTestId("data-sync-invitation-code")).toHaveTextContent("48213957");
    expect(document.activeElement).not.toBe(document.body);
    expect(within(dialog).getByRole("heading")).toHaveFocus();
  });
});
