import type { DownloadInfoData, ExtractionPlan } from "../results";

import { HeroUIProvider } from "@heroui/react";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import DownloadInfoResultRenderer from "../components/DownloadInfoResultRenderer";
import { AvailabilityDetails } from "../components/PostDetails";

const actions = vi.hoisted(() => ({
  copy: vi.fn(),
  open: vi.fn(),
  success: vi.fn(),
  danger: vi.fn(),
}));

vi.mock("@/core/clipboard", () => ({ copyTextToClipboard: actions.copy }));
vi.mock("@/sdk/BApi", () => ({ default: { gui: { openUrlInDefaultBrowser: actions.open } } }));
vi.mock("@/components/bakaui", async () => ({
  ...(await import("@heroui/react")),
  Button: (await import("@/components/bakaui/components/Button")).Button,
  toast: { success: actions.success, danger: actions.danger },
}));
vi.mock("react-i18next", async () => {
  const translations = (await import("@/locales/en/pages/postParser.json")).default;

  return {
    useTranslation: () => ({
      t: (key: string, values: Record<string, unknown> = {}) => {
        const text = translations[key as keyof typeof translations] ?? values.defaultValue ?? key;

        return String(text).replace(/\{\{(\w+)\}\}/g, (_, name: string) =>
          String(values[name] ?? ""),
        );
      },
    }),
  };
});

const plan: ExtractionPlan = {
  requirement: "required",
  steps: [
    { id: "rename", op: "renameExtension", input: "download", selector: "*.bin", extension: ".7z" },
    { id: "extract", op: "extractArchive", input: "rename", password: "first-password" },
    { id: "name", op: "renameFile", input: "extract", selector: "*.data", targetName: "book.zip" },
    { id: "move", op: "moveFile", input: "name", targetDirectory: "books/volume1" },
    { id: "final", op: "extractArchive", input: "move", password: "second-password" },
  ],
  evidence: ["Rename .bin to .7z; the nested archive uses a different password."],
};

const show = (data: DownloadInfoData, props = {}) =>
  render(
    <HeroUIProvider disableAnimation>
      <DownloadInfoResultRenderer data={data} {...props} />
    </HeroUIProvider>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  actions.copy.mockResolvedValue(undefined);
  actions.open.mockResolvedValue(undefined);
});
afterEach(cleanup);

describe("download result presentation", () => {
  it("opens and copies the same Baidu URL with its separate access code", async () => {
    show({ resources: [{ link: "https://pan.baidu.com/s/share#files", code: "ab12" }] });
    const url = "https://pan.baidu.com/s/share?pwd=ab12#files";

    fireEvent.click(screen.getByRole("button", { name: url }));
    fireEvent.click(screen.getByRole("button", { name: "Copy download link" }));
    await waitFor(() => expect(actions.copy).toHaveBeenCalledWith(url));
    expect(actions.open).toHaveBeenCalledWith({ url });
    expect(screen.getByRole("button", { name: "Link status unknown" })).toHaveAttribute(
      "data-link-health",
      "unknown",
    );
  });

  it("keeps processing order, copies the whole plan, and discloses evidence only on demand", async () => {
    const { container } = show({
      resources: [
        { link: "https://example.com/file", password: "first-password", extraction: plan },
      ],
    });

    expect(
      Array.from(
        container.querySelectorAll("[data-processing-operation]"),
        (element) => element.textContent,
      ),
    ).toEqual([
      "Change extension to .7z",
      "Extract with first-password",
      "Rename to book.zip",
      "Move to books/volume1",
      "Extract with second-password",
    ]);
    expect(screen.queryByText("Matching files: *.bin")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Copy archive password" })).not.toBeInTheDocument();
    expect(screen.queryByText(plan.evidence[0])).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Copy processing instructions" }));
    await waitFor(() => expect(actions.copy).toHaveBeenCalledWith(JSON.stringify(plan, null, 2)));
    fireEvent.click(screen.getByRole("button", { name: "Show source evidence" }));
    expect(await screen.findByText(plan.evidence[0])).toBeVisible();
  });

  it("uses status icons next to links without repeating reasons inline", () => {
    const { container } = show({
      resources: [
        { link: "https://example.com/valid", linkHealth: { status: "available" } },
        {
          link: "https://example.com/gone",
          linkHealth: { status: "unavailable", reason: "providerReportsShareUnavailable" },
        },
        {
          link: "https://example.com/unsupported",
          linkHealth: { status: "unknown", reason: "unsupportedProvider" },
        },
      ],
    });

    expect(screen.getByRole("button", { name: "Link available" })).toHaveClass("text-success");
    expect(screen.getByRole("button", { name: "Link unavailable" })).toHaveClass("text-danger");
    expect(screen.getByRole("button", { name: "Link checking unsupported" })).toHaveAttribute(
      "data-link-health",
      "unsupported",
    );
    expect(container).not.toHaveTextContent("providerReportsShareUnavailable");
    expect(container).not.toHaveTextContent("Link available");
  });

  it.each([
    ["Change extension to .7z", ".7z", "Copied extension: .7z"],
    ["Extract with first-password", "first-password", "Copied password: first-password"],
    ["Rename to book.zip", "book.zip", "Copied file name: book.zip"],
    ["Move to books/volume1", "books/volume1", "Copied destination: books/volume1"],
    ["Extract with second-password", "second-password", "Copied password: second-password"],
  ])(
    "copies the useful value from %s with a three-second confirmation",
    async (name, value, title) => {
      show({ resources: [{ extraction: plan }] });

      fireEvent.click(screen.getByRole("button", { name }));

      await waitFor(() => expect(actions.copy).toHaveBeenCalledWith(value));
      expect(actions.success).toHaveBeenCalledWith({ title, timeout: 3000 });
    },
  );

  it("keeps the clipboard untouched when an extraction step has no password", () => {
    show({
      resources: [
        {
          extraction: {
            requirement: "required",
            evidence: [],
            steps: [{ id: "extract", op: "extractArchive", input: "download" }],
          },
        },
      ],
    });

    fireEvent.click(screen.getByRole("button", { name: "Extract" }));

    expect(actions.copy).not.toHaveBeenCalled();
    expect(actions.success).not.toHaveBeenCalled();
  });

  it("reports a failed step copy without a success confirmation", async () => {
    actions.copy.mockRejectedValueOnce(new Error("Clipboard unavailable"));
    show({ resources: [{ extraction: plan }] });

    fireEvent.click(screen.getByRole("button", { name: "Rename to book.zip" }));

    await waitFor(() => expect(actions.danger).toHaveBeenCalled());
    expect(actions.success).not.toHaveBeenCalled();
  });

  it("shows each step's input and file selector in its tooltip", async () => {
    show({ resources: [{ extraction: plan }] });
    const user = userEvent.setup();

    await user.tab();
    expect(await screen.findByText("Matching files: *.bin", {}, { timeout: 2500 })).toBeVisible();
    expect(screen.getByText("Input: Downloaded files")).toBeVisible();
  });

  it("does not call a completed check unchecked when the provider omitted a reason", async () => {
    show({
      resources: [{ link: "https://example.com/file", linkHealth: { status: "available" } }],
    });
    const user = userEvent.setup();

    await user.tab();
    await user.tab();
    expect(await screen.findByText("Link available", {}, { timeout: 2500 })).toBeVisible();
    expect(screen.queryByText("This link has not been checked.")).not.toBeInTheDocument();
  });

  it("shows the availability status once and opens its supporting reason and evidence", async () => {
    render(
      <HeroUIProvider disableAnimation>
        <AvailabilityDetails
          value={{
            status: "expired",
            reason: "Later replies report that the original link has expired.",
            evidence: ["Floor 7: the link no longer works."],
          }}
        />
      </HeroUIProvider>,
    );

    expect(screen.getByText("Possibly expired; no restoration found")).toBeVisible();
    expect(
      screen.queryByText("Later replies report that the original link has expired."),
    ).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Show expiry assessment details" }));
    expect(
      await screen.findByText("Later replies report that the original link has expired."),
    ).toBeVisible();
    expect(screen.getByText("Floor 7: the link no longer works.")).toBeVisible();
  });

  it("can omit parent-rendered title and availability and distinguish no processing from unknown", () => {
    show(
      {
        title: "Already shown by the task row",
        availability: { status: "restored", evidence: [] },
        resources: [
          {
            link: "https://example.com/one",
            extraction: { requirement: "notRequired", steps: [], evidence: [] },
          },
          {
            link: "https://example.com/two",
            extraction: { requirement: "unknown", steps: [], evidence: [] },
          },
        ],
      },
      { showTitle: false, showAvailability: false },
    );

    expect(screen.queryByText("Already shown by the task row")).not.toBeInTheDocument();
    expect(screen.queryByText("Restoration reported")).not.toBeInTheDocument();
    expect(screen.getByText("No further processing")).toBeVisible();
    expect(screen.getByText("Processing instructions unknown")).toBeVisible();
  });
});
