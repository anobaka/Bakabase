import type { ReactNode } from "react";

import { useState } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import CollectionMemoPage from "..";
import RangeEditor from "../components/RangeEditor";

const { api, createPortal } = vi.hoisted(() => ({
  api: {
    getCollectionMemoTargets: vi.fn(),
    createCollectionMemoTarget: vi.fn(),
    updateCollectionMemoTarget: vi.fn(),
    deleteCollectionMemoTarget: vi.fn(),
    createCollectionMemoRange: vi.fn(),
    updateCollectionMemoRange: vi.fn(),
    deleteCollectionMemoRange: vi.fn(),
  },
  createPortal: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({ default: { collectionMemo: api } }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal }),
}));

vi.mock("@/components/bakaui", () => ({
  Button: ({
    children,
    onPress,
    isLoading,
    isDisabled,
    "aria-label": label,
  }: {
    children?: ReactNode;
    onPress?: () => void;
    isLoading?: boolean;
    isDisabled?: boolean;
    "aria-label"?: string;
  }) => (
    <button aria-label={label} disabled={isLoading || isDisabled} type="button" onClick={onPress}>
      {children}
    </button>
  ),
  Card: ({ children }: { children?: ReactNode }) => <article>{children}</article>,
  CardBody: ({ children }: { children?: ReactNode }) => <div>{children}</div>,
  Input: ({
    label,
    value,
    onValueChange,
    type,
    placeholder,
    errorMessage,
    "aria-label": ariaLabel,
  }: {
    label?: string;
    value?: string;
    onValueChange?: (value: string) => void;
    type?: string;
    placeholder?: string;
    errorMessage?: string;
    "aria-label"?: string;
  }) => (
    <label>
      {label}
      <input
        aria-label={ariaLabel ?? label}
        placeholder={placeholder}
        type={type}
        value={value}
        onChange={(event) => onValueChange?.(event.target.value)}
      />
      {errorMessage && <span role="alert">{errorMessage}</span>}
    </label>
  ),
  Modal: ({
    children,
    title,
    onOk,
  }: {
    children?: ReactNode;
    title?: string;
    onOk?: () => Promise<void>;
  }) => {
    const [open, setOpen] = useState(true);

    return open ? (
      <div aria-label={title} role="dialog">
        {children}
        <button
          onClick={async () => {
            try {
              await onOk?.();
              setOpen(false);
            } catch {
              /* The real modal keeps a rejected save open. */
            }
          }}
        >
          Save
        </button>
      </div>
    ) : null;
  },
  Spinner: () => <div role="status">Loading</div>,
  Tooltip: ({ children }: { children?: ReactNode }) => <>{children}</>,
}));

const earliest = "2026-09-01T00:00:00.000Z";
const end = "2026-09-05T08:00:35.000Z";
const target = { id: 7, name: "exhentai", ranges: [{ id: 11, startAt: earliest, endAt: end }] };
const page = () => render(<CollectionMemoPage />);
const click = (label: string) => fireEvent.click(screen.getByRole("button", { name: label }));

beforeEach(() => {
  vi.clearAllMocks();
  vi.spyOn(Date, "now").mockReturnValue(Date.parse("2026-10-01T08:00:00.000Z"));
  for (const method of Object.values(api)) method.mockResolvedValue({ code: 0 });
  api.getCollectionMemoTargets.mockResolvedValue({ code: 0, data: [target] });
  createPortal.mockImplementation((Component, props) => render(<Component {...props} />));
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe("collection memo page", () => {
  it("finishes loading empty data and offers creating a target", async () => {
    api.getCollectionMemoTargets.mockResolvedValue({ code: 0, data: [] });
    page();

    expect(await screen.findByText("collectionMemo.empty")).toBeInTheDocument();
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "collectionMemo.action.addTarget" })).toBeEnabled();
  });

  it("sorts groups by name and keeps the earliest date from a filtered-out group", async () => {
    api.getCollectionMemoTargets.mockResolvedValue({
      code: 0,
      data: [
        { ...target, id: 2, name: "Beta", ranges: [] },
        { ...target, id: 1, name: "Alpha" },
      ],
    });
    page();
    await screen.findByText("Alpha");

    expect(
      screen.getAllByRole("heading", { level: 2 }).map((heading) => heading.textContent),
    ).toEqual(["Alpha", "Beta"]);
    fireEvent.change(screen.getByRole("textbox", { name: "collectionMemo.action.search" }), {
      target: { value: "Beta" },
    });
    expect(screen.getAllByRole("heading", { level: 2 })).toHaveLength(1);
    const article = screen.getByRole("article");

    expect(article.querySelector("time")?.dateTime).toBe(earliest);
    expect(
      within(article).getByRole("button", { name: "collectionMemo.timeline.uncollectedRange" }),
    ).toHaveAccessibleName("collectionMemo.timeline.uncollectedRange");
    expect(
      article.querySelector('[aria-label="collectionMemo.timeline.uncollectedRange"]'),
    ).toHaveStyle({ width: "100%" });
  });

  it("shows a retryable load error instead of reporting a failed response as an empty list", async () => {
    api.getCollectionMemoTargets.mockResolvedValueOnce({ code: 400, message: "Failed" });
    page();

    expect(await screen.findByRole("alert")).toHaveTextContent("collectionMemo.error.load");
    expect(screen.queryByText("collectionMemo.empty")).not.toBeInTheDocument();
    click("common.action.refresh");
    expect(await screen.findByText("exhentai")).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("creates a trimmed target, reloads, and edits its existing name", async () => {
    page();
    await screen.findByText("exhentai");
    click("collectionMemo.action.addTarget");
    fireEvent.change(screen.getByRole("textbox", { name: "collectionMemo.target.label" }), {
      target: { value: "  Target  " },
    });
    click("Save");

    await waitFor(() =>
      expect(api.createCollectionMemoTarget).toHaveBeenCalledWith(
        { name: "Target" },
        { showErrorToast: false },
      ),
    );
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    click("collectionMemo.action.editTarget");
    expect(screen.getByRole("textbox", { name: "collectionMemo.target.label" })).toHaveValue(
      "exhentai",
    );
    fireEvent.change(screen.getByRole("textbox", { name: "collectionMemo.target.label" }), {
      target: { value: "Renamed" },
    });
    click("Save");
    await waitFor(() =>
      expect(api.updateCollectionMemoTarget).toHaveBeenCalledWith(
        7,
        { name: "Renamed" },
        { showErrorToast: false },
      ),
    );
    expect(api.getCollectionMemoTargets).toHaveBeenCalledTimes(3);
  });

  it("keeps entered target data open when saving returns an application error", async () => {
    api.createCollectionMemoTarget.mockResolvedValue({ code: 400, message: "Duplicate" });
    page();
    await screen.findByText("exhentai");
    click("collectionMemo.action.addTarget");
    fireEvent.change(screen.getByRole("textbox", { name: "collectionMemo.target.label" }), {
      target: { value: "Duplicate" },
    });
    click("Save");

    expect(await screen.findByRole("alert")).toHaveTextContent("collectionMemo.error.save");
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "collectionMemo.target.label" })).toHaveValue(
      "Duplicate",
    );
    expect(api.getCollectionMemoTargets).toHaveBeenCalledTimes(1);
  });

  it("keeps a fresh post-save list when an older initial load finishes later", async () => {
    let finishOldRequest!: (value: { code: number; data: (typeof target)[] }) => void;

    api.getCollectionMemoTargets.mockReturnValueOnce(
      new Promise((resolve) => {
        finishOldRequest = resolve;
      }),
    );
    api.getCollectionMemoTargets.mockResolvedValue({
      code: 0,
      data: [{ ...target, name: "Fresh" }],
    });
    page();
    click("collectionMemo.action.addTarget");
    fireEvent.change(screen.getByRole("textbox", { name: "collectionMemo.target.label" }), {
      target: { value: "Fresh" },
    });
    click("Save");
    await screen.findByRole("heading", { name: "Fresh", level: 2 });

    await act(async () => finishOldRequest({ code: 0, data: [target] }));

    expect(screen.getByRole("heading", { name: "Fresh", level: 2 })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "exhentai", level: 2 })).not.toBeInTheDocument();
  });

  it("requires confirmation before deleting a target and its ranges", async () => {
    page();
    await screen.findByText("exhentai");
    click("collectionMemo.action.deleteTarget");
    expect(screen.getByRole("dialog")).toHaveTextContent("collectionMemo.confirm.deleteTarget");
    expect(api.deleteCollectionMemoTarget).not.toHaveBeenCalled();
    click("Save");
    await waitFor(() =>
      expect(api.deleteCollectionMemoTarget).toHaveBeenCalledWith(7, { showErrorToast: false }),
    );
  });

  it("creates a point from local input and sends UTC dates with the parent target id", async () => {
    page();
    await screen.findByText("exhentai");
    click("collectionMemo.action.addRange");
    const local = "2026-09-05T16:00:35";

    fireEvent.change(screen.getByLabelText("collectionMemo.range.start"), {
      target: { value: local },
    });
    fireEvent.change(screen.getByLabelText("collectionMemo.range.end"), {
      target: { value: local },
    });
    click("Save");
    const iso = new Date(2026, 8, 5, 16, 0, 35).toISOString();

    await waitFor(() =>
      expect(api.createCollectionMemoRange).toHaveBeenCalledWith(
        7,
        { startAt: iso, endAt: iso },
        { showErrorToast: false },
      ),
    );
  });

  it("preserves saved seconds when editing and confirms deletion of the individual range", async () => {
    page();
    await screen.findByText("exhentai");
    click("collectionMemo.action.editRange");
    const date = new Date(end);

    expect((screen.getByLabelText("collectionMemo.range.end") as HTMLInputElement).value).toMatch(
      new RegExp(
        `^2026-09-05T${String(date.getHours()).padStart(2, "0")}:${String(date.getMinutes()).padStart(2, "0")}:35(?:\\.000)?$`,
      ),
    );
    click("Save");
    await waitFor(() =>
      expect(api.updateCollectionMemoRange).toHaveBeenCalledWith(
        7,
        11,
        { startAt: earliest, endAt: end },
        { showErrorToast: false },
      ),
    );
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    click("collectionMemo.action.deleteRange");
    expect(api.deleteCollectionMemoRange).not.toHaveBeenCalled();
    click("Save");
    await waitFor(() =>
      expect(api.deleteCollectionMemoRange).toHaveBeenCalledWith(7, 11, { showErrorToast: false }),
    );
  });

  it("resaves unchanged precise boundaries without shifting a DST-fold instant", async () => {
    const precise = "2025-11-02T06:30:35.1234567Z";

    api.getCollectionMemoTargets.mockResolvedValue({
      code: 0,
      data: [{ ...target, ranges: [{ id: 11, startAt: precise, endAt: precise }] }],
    });
    page();
    await screen.findByText("exhentai");
    click("collectionMemo.action.editRange");
    click("Save");

    await waitFor(() =>
      expect(api.updateCollectionMemoRange).toHaveBeenCalledWith(
        7,
        11,
        { startAt: precise, endAt: precise },
        { showErrorToast: false },
      ),
    );
  });
});

describe("collection memo range validation", () => {
  it.each([
    ["2026-09-05T16:00", "2026-09-01T16:00", "collectionMemo.validation.order"],
    ["2027-09-05T16:00", "2027-09-05T16:00", "collectionMemo.validation.future"],
    ["", "2026-09-05T16:00", "collectionMemo.validation.date"],
  ])("keeps invalid dates open (%s to %s)", async (start, finish, message) => {
    const onSave = vi.fn();

    render(<RangeEditor targetName="Target" onSave={onSave} />);
    fireEvent.change(screen.getByLabelText("collectionMemo.range.start"), {
      target: { value: start },
    });
    fireEvent.change(screen.getByLabelText("collectionMemo.range.end"), {
      target: { value: finish },
    });
    click("Save");

    expect(await screen.findByRole("alert")).toHaveTextContent(message);
    expect(onSave).not.toHaveBeenCalled();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });
});
