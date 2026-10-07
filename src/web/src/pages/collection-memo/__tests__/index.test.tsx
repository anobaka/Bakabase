import type { ReactElement, ReactNode } from "react";

import { Children, cloneElement, isValidElement, useState } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import CollectionMemoPage from "..";
import RangeEditor from "../components/RangeEditor";
import SettingsEditor from "../components/SettingsEditor";

import CollectionMemoTopic from "@/components/HelpCenter/topics/collectionMemo";

const { api, createPortal, openUrl } = vi.hoisted(() => ({
  api: {
    getCollectionMemoTargets: vi.fn(),
    getCollectionMemoSettings: vi.fn(),
    updateCollectionMemoSettings: vi.fn(),
    createCollectionMemoTarget: vi.fn(),
    updateCollectionMemoTarget: vi.fn(),
    deleteCollectionMemoTarget: vi.fn(),
    createCollectionMemoRange: vi.fn(),
    updateCollectionMemoRange: vi.fn(),
    deleteCollectionMemoRange: vi.fn(),
    fillCollectionMemoGap: vi.fn(),
    resizeCollectionMemoRangeCoverage: vi.fn(),
  },
  createPortal: vi.fn(),
  openUrl: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: { collectionMemo: api, gui: { openUrlInDefaultBrowser: openUrl } },
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal }),
}));
vi.mock("@/components/HelpCenter/HelpCenterModal", () => ({
  default: ({ topic }: { topic?: string }) => (
    <div aria-label="helpCenter.title" data-topic={topic} role="dialog">
      {topic === "collectionMemo" && <CollectionMemoTopic />}
    </div>
  ),
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
  Textarea: ({
    label,
    value,
    onValueChange,
  }: {
    label: string;
    value: string;
    onValueChange: (value: string) => void;
  }) => (
    <label>
      {label}
      <textarea value={value} onChange={(event) => onValueChange(event.target.value)} />
    </label>
  ),
  Input: ({
    label,
    value,
    onValueChange,
    type,
    placeholder,
    description,
    errorMessage,
    onCompositionStart,
    onCompositionEnd,
    "aria-label": ariaLabel,
  }: {
    label?: string;
    value?: string;
    onValueChange?: (value: string) => void;
    type?: string;
    placeholder?: string;
    description?: ReactNode;
    errorMessage?: string;
    onCompositionStart?: () => void;
    onCompositionEnd?: () => void;
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
        onCompositionEnd={onCompositionEnd}
        onCompositionStart={onCompositionStart}
      />
      {description && <span>{description}</span>}
      {errorMessage && <span role="alert">{errorMessage}</span>}
    </label>
  ),
  RadioGroup: ({
    children,
    label,
    value,
    onValueChange,
  }: {
    children?: ReactNode;
    label?: string;
    value?: string;
    onValueChange?: (value: string) => void;
  }) => (
    <fieldset>
      <legend>{label}</legend>
      {Children.map(children, (child) => {
        if (!isValidElement(child)) return child;
        const radio = child as ReactElement<{
          value: string;
          isSelected?: boolean;
          onValueChange?: (value: string) => void;
        }>;

        return cloneElement(radio, { isSelected: radio.props.value === value, onValueChange });
      })}
    </fieldset>
  ),
  Radio: ({
    children,
    value,
    isSelected,
    onValueChange,
  }: {
    children?: ReactNode;
    value: string;
    isSelected?: boolean;
    onValueChange?: (value: string) => void;
  }) => (
    <label>
      <input
        checked={isSelected}
        type="radio"
        value={value}
        onChange={() => onValueChange?.(value)}
      />
      {children}
    </label>
  ),
  Modal: ({
    children,
    title,
    onOk,
    footer,
  }: {
    children?: ReactNode;
    title?: string;
    onOk?: () => Promise<void>;
    footer?: { okProps?: { isDisabled?: boolean } };
  }) => {
    const [open, setOpen] = useState(true);

    return open ? (
      <div aria-label={title} role="dialog">
        {children}
        <button
          disabled={footer?.okProps?.isDisabled}
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
  vi.stubGlobal(
    "ResizeObserver",
    class {
      observe() {}
      disconnect() {}
    },
  );
  vi.spyOn(Date, "now").mockReturnValue(Date.parse("2026-10-01T08:00:00.000Z"));
  for (const method of Object.values(api)) method.mockResolvedValue({ code: 0 });
  api.getCollectionMemoTargets.mockResolvedValue({ code: 0, data: [target] });
  api.getCollectionMemoSettings.mockResolvedValue({
    code: 0,
    data: { startAt: earliest, reverse: true },
  });
  createPortal.mockImplementation((Component, props) => render(<Component {...props} />));
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("collection memo page", () => {
  it("opens collection memo help beside the title and omits the global settings summary", async () => {
    page();
    await screen.findByText("exhentai");
    expect(screen.queryByText("collectionMemo.settings.start")).not.toBeInTheDocument();
    expect(screen.queryByText("collectionMemo.timeline.now")).not.toBeInTheDocument();
    expect(screen.queryByText("collectionMemo.settings.reverse")).not.toBeInTheDocument();
    expect(
      screen.queryByText("collectionMemo.browsingIntegration.pending"),
    ).not.toBeInTheDocument();
    click("helpCenter.button.tooltip");
    const help = screen.getByRole("dialog", { name: "helpCenter.title" });

    expect(help).toHaveAttribute("data-topic", "collectionMemo");
    expect(within(help).getByText("helpCenter.collectionMemo.timeline.drag")).toBeInTheDocument();
    expect(
      within(help).getByText("helpCenter.collectionMemo.timeline.keyboard"),
    ).toBeInTheDocument();
    expect(
      within(help).getByText("helpCenter.collectionMemo.settings.start.desc"),
    ).toBeInTheDocument();
  });

  it("opens a linked range through ExternalLink and displays its note as plain text", async () => {
    const annotated = {
      ...target.ranges[0],
      url: "https://example.com/source",
      note: "Source <b>one</b>\nsecond line",
    };

    api.getCollectionMemoTargets.mockResolvedValue({
      code: 0,
      data: [{ ...target, ranges: [annotated] }],
    });
    page();
    await screen.findByText(target.name);
    const article = screen.getByRole("article");

    expect(within(article).getByText(/Source <b>one<\/b>/)).toHaveTextContent("second line");
    expect(article.querySelector("b")).toBeNull();
    fireEvent.click(article.querySelector("li a")!);
    expect(openUrl).toHaveBeenCalledExactlyOnceWith({ url: annotated.url });
  });

  it("saves optional links and multiline notes, then allows clearing both without changing dates", async () => {
    const onSave = vi.fn().mockResolvedValue(undefined);
    const annotated = {
      ...target.ranges[0],
      url: "https://example.com/source",
      note: "First\nSecond",
    };
    const view = render(
      <RangeEditor
        globalStartAt={earliest}
        range={annotated}
        targetName="Target"
        onSave={onSave}
      />,
    );

    expect(screen.getByLabelText("collectionMemo.range.url")).toHaveValue(annotated.url);
    expect(screen.getByLabelText("collectionMemo.range.note")).toHaveValue(annotated.note);
    fireEvent.change(screen.getByLabelText("collectionMemo.range.url"), {
      target: { value: "  https://example.com/new  " },
    });
    fireEvent.change(screen.getByLabelText("collectionMemo.range.note"), {
      target: { value: "  Changed\nnotes  " },
    });
    click("Save");
    await waitFor(() =>
      expect(onSave).toHaveBeenCalledWith({
        startAt: earliest,
        endAt: end,
        url: "https://example.com/new",
        note: "Changed\nnotes",
      }),
    );
    view.unmount();

    render(
      <RangeEditor
        globalStartAt={earliest}
        range={annotated}
        targetName="Target"
        onSave={onSave}
      />,
    );
    fireEvent.change(screen.getByLabelText("collectionMemo.range.url"), { target: { value: "" } });
    fireEvent.change(screen.getByLabelText("collectionMemo.range.note"), { target: { value: "" } });
    click("Save");
    await waitFor(() => expect(onSave).toHaveBeenLastCalledWith({ startAt: earliest, endAt: end }));
  });

  it.each(["not a link", "javascript:alert(1)", "https://user:pass@example.com"])(
    "keeps an invalid range link open: %s",
    async (url) => {
      const onSave = vi.fn();

      render(
        <RangeEditor
          globalStartAt={earliest}
          range={target.ranges[0]}
          targetName="Target"
          onSave={onSave}
        />,
      );
      fireEvent.change(screen.getByLabelText("collectionMemo.range.url"), {
        target: { value: url },
      });
      click("Save");
      expect(await screen.findByRole("alert")).toHaveTextContent("collectionMemo.validation.url");
      expect(onSave).not.toHaveBeenCalled();
    },
  );

  it("uses the same current-time limit for every target without a header summary", async () => {
    api.getCollectionMemoTargets.mockResolvedValue({
      code: 0,
      data: [target, { ...target, id: 8, name: "soulplus" }],
    });
    page();

    await screen.findByText("soulplus");
    expect(screen.queryByText("collectionMemo.timeline.now")).not.toBeInTheDocument();
    const endHandles = screen.getAllByRole("slider", { name: "collectionMemo.timeline.resizeEnd" });

    expect(endHandles).toHaveLength(2);
    for (const handle of endHandles) {
      expect(handle).toHaveAttribute("aria-valuemax", String(Date.now()));
    }
  });

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

    expect(Array.from(article.querySelectorAll("time")).map((time) => time.dateTime)).toContain(
      earliest,
    );
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
    const freshOrigin = "2026-08-01T00:00:00.0000001Z";

    api.getCollectionMemoSettings
      .mockResolvedValueOnce({ code: 0, data: { startAt: earliest, reverse: true } })
      .mockResolvedValue({ code: 0, data: { startAt: freshOrigin, reverse: false } });

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
    expect(document.querySelector("time")?.dateTime).toBe(freshOrigin);
    click("collectionMemo.settings.title");
    expect(screen.getByRole("radio", { name: "collectionMemo.settings.forward" })).toBeChecked();
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
    const local = "2026/09/05 16:00:35";

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
        `^2026-09-05 ${String(date.getHours()).padStart(2, "0")}:${String(date.getMinutes()).padStart(2, "0")}:35(?:\\.000)?$`,
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

describe("collection memo timeline shortcuts", () => {
  it("fills a gap with the atomic API and reloads the consolidated range list", async () => {
    const nextStart = "2026-09-10T00:00:00.1234567Z";
    const nextEnd = "2026-09-20T00:00:00.7654321Z";

    api.getCollectionMemoTargets
      .mockResolvedValueOnce({
        code: 0,
        data: [
          { ...target, ranges: [...target.ranges, { id: 12, startAt: nextStart, endAt: nextEnd }] },
        ],
      })
      .mockResolvedValue({
        code: 0,
        data: [{ ...target, ranges: [{ id: 11, startAt: earliest, endAt: nextEnd }] }],
      });
    page();
    await screen.findByText("exhentai");
    fireEvent.pointerEnter(
      screen.getAllByRole("button", { name: "collectionMemo.timeline.uncollectedRange" })[0],
    );
    click("collectionMemo.timeline.fillGap");

    await waitFor(() =>
      expect(api.fillCollectionMemoGap).toHaveBeenCalledWith(
        7,
        { startAt: end, endAt: nextStart },
        { showErrorToast: false },
      ),
    );
    await waitFor(() =>
      expect(
        screen.getAllByRole("button", { name: "collectionMemo.action.editRange" }),
      ).toHaveLength(1),
    );
    expect(api.getCollectionMemoTargets).toHaveBeenCalledTimes(2);
    expect(api.createCollectionMemoRange).not.toHaveBeenCalled();
    expect(api.updateCollectionMemoRange).not.toHaveBeenCalled();
    expect(api.deleteCollectionMemoRange).not.toHaveBeenCalled();
  });

  it("keeps a rejected gap fill visible and retryable without reloading or discarding coverage", async () => {
    api.fillCollectionMemoGap.mockResolvedValueOnce({ code: 500, message: "Save failed" });
    page();
    await screen.findByText("exhentai");
    fireEvent.pointerEnter(
      screen.getByRole("button", { name: "collectionMemo.timeline.uncollectedRange" }),
    );
    click("collectionMemo.timeline.fillGap");

    await waitFor(() =>
      expect(screen.getAllByRole("alert")[0]).toHaveTextContent(
        "collectionMemo.timeline.saveFailed",
      ),
    );
    expect(api.getCollectionMemoTargets).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "collectionMemo.timeline.fillGap" })).toBeEnabled();
    click("collectionMemo.timeline.fillGap");
    await waitFor(() => expect(api.fillCollectionMemoGap).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(api.getCollectionMemoTargets).toHaveBeenCalledTimes(2));
  });

  it("persists a keyboard boundary adjustment with the raw component snapshot and target id", async () => {
    const newEnd = new Date(Date.parse(end) + 60_000).toISOString();

    api.getCollectionMemoTargets
      .mockResolvedValueOnce({ code: 0, data: [target] })
      .mockResolvedValue({
        code: 0,
        data: [{ ...target, ranges: [{ ...target.ranges[0], endAt: newEnd }] }],
      });
    page();
    await screen.findByText("exhentai");
    fireEvent.keyDown(screen.getByRole("slider", { name: "collectionMemo.timeline.resizeEnd" }), {
      key: "ArrowLeft",
    });

    await waitFor(() =>
      expect(api.resizeCollectionMemoRangeCoverage).toHaveBeenCalledWith(
        7,
        { ranges: target.ranges, edge: "end", at: newEnd },
        { showErrorToast: false },
      ),
    );
    await waitFor(() => expect(api.getCollectionMemoTargets).toHaveBeenCalledTimes(2));
    expect(api.updateCollectionMemoRange).not.toHaveBeenCalled();
  });

  it("refreshes a conflicting boundary snapshot and asks the user to adjust the updated records", async () => {
    api.resizeCollectionMemoRangeCoverage.mockResolvedValueOnce({ code: 409, message: "Changed" });
    const updatedEnd = "2026-09-06T08:00:35Z";

    api.getCollectionMemoTargets
      .mockResolvedValueOnce({ code: 0, data: [target] })
      .mockResolvedValue({
        code: 0,
        data: [{ ...target, ranges: [{ ...target.ranges[0], endAt: updatedEnd }] }],
      });
    page();
    await screen.findByText("exhentai");
    fireEvent.keyDown(screen.getByRole("slider", { name: "collectionMemo.timeline.resizeEnd" }), {
      key: "ArrowLeft",
    });

    await waitFor(() => expect(api.getCollectionMemoTargets).toHaveBeenCalledTimes(2));
    await waitFor(() =>
      expect(screen.getByRole("alert")).toHaveTextContent("collectionMemo.timeline.changed"),
    );
    expect(screen.queryByRole("button", { name: "collectionMemo.timeline.retry" })).toBeNull();
    expect(
      screen.getByRole("slider", { name: "collectionMemo.timeline.resizeEnd" }),
    ).toHaveAttribute("aria-valuenow", String(Date.parse(updatedEnd)));
    expect(api.resizeCollectionMemoRangeCoverage).toHaveBeenCalledTimes(1);
  });
});

describe("collection memo text date-time entry", () => {
  it("keeps a valid UTC minimum record editable and previews its actual instant", async () => {
    const onSave = vi.fn().mockResolvedValue(undefined);
    const minimum = "0001-01-01T00:00:00.0000001Z";

    render(
      <RangeEditor
        globalStartAt={earliest}
        range={{ id: 11, startAt: minimum, endAt: minimum }}
        targetName="Target"
        onSave={onSave}
      />,
    );
    expect(screen.getByRole("textbox", { name: "collectionMemo.range.start" })).not.toHaveValue("");
    expect(
      Array.from(screen.getByRole("dialog").querySelectorAll("time")).map((time) => time.dateTime),
    ).toEqual([minimum, minimum]);
    click("Save");
    await waitFor(() => expect(onSave).toHaveBeenCalledWith({ startAt: minimum, endAt: minimum }));
  });

  it("accepts pasted Chinese dates as plain text and previews the saved instant without rewriting input", async () => {
    const user = userEvent.setup();
    const onSave = vi.fn().mockResolvedValue(undefined);
    const value = "2026年9月5日 16时00分35秒";
    const iso = new Date(2026, 8, 5, 16, 0, 35).toISOString();

    render(<RangeEditor globalStartAt={earliest} targetName="Target" onSave={onSave} />);
    const startInput = screen.getByRole("textbox", { name: "collectionMemo.range.start" });
    const endInput = screen.getByRole("textbox", { name: "collectionMemo.range.end" });

    expect(startInput).toHaveAttribute("type", "text");
    expect(endInput).toHaveAttribute("type", "text");
    await user.clear(startInput);
    await user.paste(value);
    await user.clear(endInput);
    await user.paste(value);
    await user.tab();
    expect(startInput).toHaveValue(value);
    expect(endInput).toHaveValue(value);
    expect(
      Array.from(screen.getByRole("dialog").querySelectorAll("time")).map((time) => time.dateTime),
    ).toEqual([iso, iso]);
    await user.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(onSave).toHaveBeenCalledWith({ startAt: iso, endAt: iso }));
    expect(onSave).toHaveBeenCalledTimes(1);
  });

  it("uses local midnight for date-only start and end entries", async () => {
    const onSave = vi.fn().mockResolvedValue(undefined);

    render(<RangeEditor globalStartAt={earliest} targetName="Target" onSave={onSave} />);
    fireEvent.change(screen.getByLabelText("collectionMemo.range.start"), {
      target: { value: "2026.9.1" },
    });
    fireEvent.change(screen.getByLabelText("collectionMemo.range.end"), {
      target: { value: "20260905" },
    });
    click("Save");

    await waitFor(() =>
      expect(onSave).toHaveBeenCalledWith({
        startAt: new Date(2026, 8, 1).toISOString(),
        endAt: new Date(2026, 8, 5).toISOString(),
      }),
    );
  });

  it("honors explicit pasted offsets and full fractional seconds while comparing actual instants", async () => {
    const onSave = vi.fn().mockResolvedValue(undefined);
    const expected = "2026-09-04T16:00:35.1234567Z";

    render(<RangeEditor globalStartAt={earliest} targetName="Target" onSave={onSave} />);
    fireEvent.change(screen.getByLabelText("collectionMemo.range.start"), {
      target: { value: "2026-09-05 01:00:35.1234567+09:00" },
    });
    fireEvent.change(screen.getByLabelText("collectionMemo.range.end"), {
      target: { value: expected },
    });
    click("Save");

    await waitFor(() =>
      expect(onSave).toHaveBeenCalledWith({ startAt: expected, endAt: expected }),
    );
  });

  it("preserves incomplete typed text on blur and keeps the editor open until corrected", async () => {
    const user = userEvent.setup();
    const onSave = vi.fn().mockResolvedValue(undefined);

    render(<RangeEditor globalStartAt={earliest} targetName="Target" onSave={onSave} />);
    const startInput = screen.getByRole("textbox", { name: "collectionMemo.range.start" });

    await user.clear(startInput);
    await user.type(startInput, "2026/0");
    await user.tab();
    expect(startInput).toHaveValue("2026/0");
    await user.click(screen.getByRole("button", { name: "Save" }));
    expect(onSave).not.toHaveBeenCalled();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(startInput).toHaveValue("2026/0");
    fireEvent.change(startInput, { target: { value: "2026/09/01" } });
    fireEvent.change(screen.getByLabelText("collectionMemo.range.end"), {
      target: { value: "2026/09/05" },
    });
    click("Save");
    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
  });
});

describe("collection memo range validation", () => {
  it.each([
    ["2026-09-05T16:00", "2026-09-01T16:00", "collectionMemo.validation.order"],
    ["2027-09-05T16:00", "2027-09-05T16:00", "collectionMemo.validation.future"],
    ["", "2026-08-05T16:00", "collectionMemo.validation.inheritedOrder"],
    ["2026/02/30", "2026/09/05", "collectionMemo.validation.date"],
    [
      "2026-09-05T08:00:35.1234568Z",
      "2026-09-05T08:00:35.1234567Z",
      "collectionMemo.validation.order",
    ],
    [
      "2026-10-01T08:00:00.0000001Z",
      "2026-10-01T08:00:00.0000001Z",
      "collectionMemo.validation.future",
    ],
  ])("keeps invalid dates open (%s to %s)", async (start, finish, message) => {
    const onSave = vi.fn();

    render(<RangeEditor globalStartAt={earliest} targetName="Target" onSave={onSave} />);
    fireEvent.change(screen.getByLabelText("collectionMemo.range.start"), {
      target: { value: start },
    });
    fireEvent.change(screen.getByLabelText("collectionMemo.range.end"), {
      target: { value: finish },
    });
    click("Save");

    expect(
      (await screen.findAllByRole("alert")).some((alert) => alert.textContent?.includes(message)),
    ).toBe(true);
    expect(onSave).not.toHaveBeenCalled();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });
});

describe("collection memo global settings", () => {
  it("waits for settings before publishing targets or enabling settings actions", async () => {
    let finishSettings!: (value: {
      code: number;
      data: { startAt: string; reverse: boolean };
    }) => void;

    api.getCollectionMemoSettings.mockReturnValueOnce(
      new Promise((resolve) => {
        finishSettings = resolve;
      }),
    );
    page();
    await waitFor(() => expect(api.getCollectionMemoTargets).toHaveBeenCalledTimes(1));
    expect(screen.queryByRole("heading", { name: "exhentai" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "collectionMemo.settings.title" })).toBeDisabled();
    await act(async () => finishSettings({ code: 0, data: { startAt: earliest, reverse: true } }));
    expect(await screen.findByRole("heading", { name: "exhentai" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "collectionMemo.settings.title" })).toBeEnabled();
    click("collectionMemo.settings.title");
    expect(screen.getByRole("radio", { name: "collectionMemo.settings.reverse" })).toBeChecked();
  });

  it.each([
    { code: 500, message: "Failed" },
    { code: 0, data: { reverse: true } },
    { code: 0, data: { startAt: "invalid", reverse: false } },
    { code: 0, data: { startAt: earliest } },
  ])("keeps a partial initial load retryable without inventing settings (%j)", async (response) => {
    api.getCollectionMemoSettings.mockResolvedValueOnce(response);
    page();
    expect(await screen.findByRole("alert")).toHaveTextContent("collectionMemo.error.load");
    expect(screen.queryByRole("heading", { name: "exhentai" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "collectionMemo.settings.title" })).toBeDisabled();
    expect(screen.queryByText("collectionMemo.empty")).not.toBeInTheDocument();
    click("common.action.refresh");
    expect(await screen.findByRole("heading", { name: "exhentai" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "collectionMemo.settings.title" })).toBeEnabled();
  });

  it("retains the last coherent pair when a refresh only loads new targets", async () => {
    page();
    await screen.findByText("exhentai");
    api.getCollectionMemoTargets.mockResolvedValue({ code: 0, data: [{ ...target, name: "New" }] });
    api.getCollectionMemoSettings.mockResolvedValue({ code: 500 });
    click("common.action.refresh");
    expect(await screen.findByRole("alert")).toHaveTextContent("collectionMemo.error.load");
    expect(screen.getByRole("heading", { name: "exhentai" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "New" })).not.toBeInTheDocument();
    click("collectionMemo.settings.title");
    expect(screen.getByRole("radio", { name: "collectionMemo.settings.reverse" })).toBeChecked();
  });

  it("saves the shared origin and direction, then loads the persisted values again", async () => {
    const nextStart = "2026-08-01T00:00:00.1234567Z";

    page();
    await screen.findByText("exhentai");
    click("collectionMemo.settings.title");
    expect(screen.getByRole("radio", { name: "collectionMemo.settings.reverse" })).toBeChecked();
    fireEvent.change(screen.getByRole("textbox", { name: "collectionMemo.settings.start" }), {
      target: { value: nextStart },
    });
    fireEvent.click(screen.getByRole("radio", { name: "collectionMemo.settings.forward" }));
    expect(screen.getByRole("dialog").querySelector("time")?.dateTime).toBe(nextStart);
    api.getCollectionMemoSettings.mockResolvedValue({
      code: 0,
      data: { startAt: nextStart, reverse: false },
    });
    click("Save");
    await waitFor(() =>
      expect(api.updateCollectionMemoSettings).toHaveBeenCalledWith(
        { startAt: nextStart, reverse: false },
        { showErrorToast: false },
      ),
    );
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(screen.getByRole("article").querySelector("time")?.dateTime).toBe(nextStart);
    click("collectionMemo.settings.title");
    expect(screen.getByRole("radio", { name: "collectionMemo.settings.forward" })).toBeChecked();
    expect(screen.getByRole("dialog").querySelector("time")?.dateTime).toBe(nextStart);
    expect(api.getCollectionMemoSettings).toHaveBeenCalledTimes(2);
  });

  it("keeps rejected settings edits open without replacing the persisted timeline", async () => {
    api.updateCollectionMemoSettings.mockResolvedValue({ code: 400 });
    page();
    await screen.findByText("exhentai");
    click("collectionMemo.settings.title");
    const typed = "2026/8/1 00:00";

    fireEvent.change(screen.getByRole("textbox", { name: "collectionMemo.settings.start" }), {
      target: { value: typed },
    });
    fireEvent.click(screen.getByRole("radio", { name: "collectionMemo.settings.forward" }));
    click("Save");
    expect(await screen.findByRole("alert")).toHaveTextContent("collectionMemo.error.save");
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "collectionMemo.settings.start" })).toHaveValue(
      typed,
    );
    expect(screen.getByRole("radio", { name: "collectionMemo.settings.forward" })).toBeChecked();
    expect(api.getCollectionMemoSettings).toHaveBeenCalledTimes(1);
    expect(document.querySelector("time")?.dateTime).toBe(earliest);
  });

  it("validates global start against the earliest inherited end across all targets", async () => {
    const minimumEnd = "2026-09-03T00:00:00.1234567Z";

    api.getCollectionMemoTargets.mockResolvedValue({
      code: 0,
      data: [
        { ...target, ranges: [{ id: 11, startAt: null, endAt: end }] },
        { id: 8, name: "Other", ranges: [{ id: 12, startAt: null, endAt: minimumEnd }] },
      ],
    });
    page();
    await screen.findByText("exhentai");
    click("collectionMemo.settings.title");
    fireEvent.change(screen.getByRole("textbox", { name: "collectionMemo.settings.start" }), {
      target: { value: "2026-09-03T00:00:00.1234568Z" },
    });
    click("Save");
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "collectionMemo.settings.inheritedOrder",
    );
    expect(api.updateCollectionMemoSettings).not.toHaveBeenCalled();
    fireEvent.change(screen.getByRole("textbox", { name: "collectionMemo.settings.start" }), {
      target: { value: minimumEnd },
    });
    click("Save");
    await waitFor(() =>
      expect(api.updateCollectionMemoSettings).toHaveBeenCalledWith(
        { startAt: minimumEnd, reverse: true },
        { showErrorToast: false },
      ),
    );
  });

  it("preserves precise global origin on an unchanged local edit", async () => {
    const precise = "2025-11-02T06:30:35.1234567Z";
    const onSave = vi.fn().mockResolvedValue(undefined);

    render(<SettingsEditor settings={{ startAt: precise, reverse: true }} onSave={onSave} />);
    expect(screen.getByRole("dialog").querySelector("time")?.dateTime).toBe(precise);
    click("Save");
    await waitFor(() => expect(onSave).toHaveBeenCalledWith({ startAt: precise, reverse: true }));
  });

  it.each([
    ["", "collectionMemo.validation.date"],
    ["2026/02/30", "collectionMemo.validation.date"],
    ["2026-10-01T08:00:00.0000001Z", "collectionMemo.settings.future"],
  ])("rejects invalid global origins (%s)", async (value, error) => {
    const onSave = vi.fn();

    render(<SettingsEditor settings={{ startAt: earliest, reverse: true }} onSave={onSave} />);
    const input = screen.getByRole("textbox", { name: "collectionMemo.settings.start" });

    fireEvent.change(input, { target: { value } });
    fireEvent.blur(input);
    expect(input).toHaveValue(value);
    click("Save");
    expect(
      (await screen.findAllByRole("alert")).some((alert) => alert.textContent?.includes(error)),
    ).toBe(true);
    expect(onSave).not.toHaveBeenCalled();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });
});

describe("collection memo inherited starts", () => {
  it("keeps an inherited point's end date and point marker beside the global-start label", async () => {
    api.getCollectionMemoTargets.mockResolvedValue({
      code: 0,
      data: [{ ...target, ranges: [{ id: 11, startAt: null, endAt: earliest }] }],
    });
    page();
    await screen.findByText("exhentai");
    const range = screen.getByRole("listitem");

    expect(within(range).getByText("collectionMemo.range.inherited")).toBeInTheDocument();
    expect(within(range).getByText("collectionMemo.range.point")).toBeInTheDocument();
    expect([...range.querySelectorAll("time")].map((time) => time.dateTime)).toEqual([earliest]);
  });

  it("creates a blank-start range and persists its inheritance as null", async () => {
    page();
    await screen.findByText("exhentai");
    click("collectionMemo.action.addRange");
    expect(screen.getByLabelText("collectionMemo.range.start")).toHaveValue("");
    expect(screen.getByRole("dialog").querySelector("time")?.dateTime).toBe(earliest);
    fireEvent.change(screen.getByLabelText("collectionMemo.range.end"), { target: { value: end } });
    click("Save");
    await waitFor(() =>
      expect(api.createCollectionMemoRange).toHaveBeenCalledWith(
        7,
        { startAt: undefined, endAt: end },
        { showErrorToast: false },
      ),
    );
  });

  it.each([null, undefined])(
    "labels an inherited start without displaying its date and retains it when editing (%s)",
    async (startAt) => {
      api.getCollectionMemoTargets.mockResolvedValue({
        code: 0,
        data: [{ ...target, ranges: [{ id: 11, startAt, endAt: end }] }],
      });
      page();
      await screen.findByText("exhentai");
      expect(
        within(screen.getByRole("listitem")).getByText("collectionMemo.range.inherited"),
      ).toBeInTheDocument();
      expect(
        [...screen.getByRole("listitem").querySelectorAll("time")].map((time) => time.dateTime),
      ).toEqual([end]);
      click("collectionMemo.action.editRange");
      expect(screen.getByLabelText("collectionMemo.range.start")).toHaveValue("");
      click("Save");
      await waitFor(() =>
        expect(api.updateCollectionMemoRange).toHaveBeenCalledWith(
          7,
          11,
          { startAt: undefined, endAt: end },
          { showErrorToast: false },
        ),
      );
    },
  );

  it("lets an explicit range switch to a blank inherited start", async () => {
    page();
    await screen.findByText("exhentai");
    click("collectionMemo.action.editRange");
    fireEvent.change(screen.getByLabelText("collectionMemo.range.start"), {
      target: { value: "   " },
    });
    click("Save");
    await waitFor(() =>
      expect(api.updateCollectionMemoRange).toHaveBeenCalledWith(
        7,
        11,
        { startAt: undefined, endAt: end },
        { showErrorToast: false },
      ),
    );
  });

  it("updates a blank start preview dynamically and validates its new effective instant", async () => {
    const onSave = vi.fn().mockResolvedValue(undefined);
    const range = { id: 11, startAt: null, endAt: end };
    const later = "2026-09-06T00:00:00Z";
    const view = render(
      <RangeEditor globalStartAt={earliest} range={range} targetName="Target" onSave={onSave} />,
    );

    expect(screen.getByRole("dialog").querySelector("time")?.dateTime).toBe(earliest);
    view.rerender(
      <RangeEditor globalStartAt={later} range={range} targetName="Target" onSave={onSave} />,
    );
    expect(screen.getByLabelText("collectionMemo.range.start")).toHaveValue("");
    expect(screen.getByRole("dialog").querySelector("time")?.dateTime).toBe(later);
    click("Save");
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "collectionMemo.validation.inheritedOrder",
    );
    expect(onSave).not.toHaveBeenCalled();
    fireEvent.change(screen.getByLabelText("collectionMemo.range.start"), {
      target: { value: earliest },
    });
    click("Save");
    await waitFor(() => expect(onSave).toHaveBeenCalledWith({ startAt: earliest, endAt: end }));
  });

  it("keeps the default new end precise enough for a global start initialized now", async () => {
    const instant = "2026-10-01T08:00:00.123Z";
    const onSave = vi.fn().mockResolvedValue(undefined);

    vi.spyOn(Date, "now").mockReturnValue(Date.parse(instant));
    render(<RangeEditor globalStartAt={instant} targetName="Target" onSave={onSave} />);
    expect(
      Array.from(screen.getByRole("dialog").querySelectorAll("time")).map((time) => time.dateTime),
    ).toEqual([instant, instant]);
    click("Save");
    await waitFor(() => expect(onSave).toHaveBeenCalledWith({ startAt: null, endAt: instant }));
  });

  it("resizes inherited coverage with the raw null snapshot and expected global origin", async () => {
    const inherited = { id: 11, startAt: null, endAt: end };
    const newEnd = new Date(Date.parse(end) + 60_000).toISOString();

    api.getCollectionMemoTargets.mockResolvedValue({
      code: 0,
      data: [{ ...target, ranges: [inherited] }],
    });
    page();
    await screen.findByText("exhentai");
    fireEvent.keyDown(screen.getByRole("slider", { name: "collectionMemo.timeline.resizeEnd" }), {
      key: "ArrowLeft",
    });
    await waitFor(() =>
      expect(api.resizeCollectionMemoRangeCoverage).toHaveBeenCalledWith(
        7,
        {
          ranges: [{ ...inherited, startAt: undefined }],
          edge: "end",
          at: newEnd,
          expectedGlobalStartAt: earliest,
        },
        { showErrorToast: false },
      ),
    );
  });
});
