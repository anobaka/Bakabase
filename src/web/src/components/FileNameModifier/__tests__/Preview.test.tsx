import type { ReactNode } from "react";
import type { FileNameModificationResult } from "../PreviewList";
import type * as ReactVirtualized from "react-virtualized";

import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import DiffHighlight from "../DiffHighlight";
import PreviewItem from "../PreviewItem";
import PreviewList, { PREVIEW_ROW_HEIGHT } from "../PreviewList";

// Use the real virtual list; only supply the dimensions that jsdom cannot measure.
vi.mock("react-virtualized", async () => ({
  ...(await vi.importActual<typeof ReactVirtualized>("react-virtualized")),
  AutoSizer: ({ children }: { children: (size: { width: number; height: number }) => ReactNode }) =>
    children({ width: 400, height: 224 }),
}));

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
  vi.unstubAllGlobals();
});

async function render(content: ReactNode) {
  await act(async () => root.render(<>{content}</>));
}

function result(index = 1): FileNameModificationResult {
  return {
    originalPath: `/library/albums/old-${index}.jpg`,
    modifiedPath: `/library/albums/new-${index}.jpg`,
    originalFileName: `old-${index}.jpg`,
    modifiedFileName: `new-${index}.jpg`,
    commonPrefix: "/library/albums/",
    originalRelative: `old-${index}.jpg`,
    modifiedRelative: `new-${index}.jpg`,
  };
}

describe("rename preview", () => {
  it("keeps combined differences for existing workflow consumers", async () => {
    await render(<DiffHighlight modified="photo-new.jpg" original="photo-old.jpg" />);

    expect(container.textContent).toBe("photo-oldnew.jpg");
    expect(container.querySelector(".line-through")).toHaveTextContent("old");
  });

  it.each([
    ["photo-old.jpg", "photo-new.jpg"],
    ["photo.jpg", "photo-01.jpg"],
    ["photo-01.jpg", "photo.jpg"],
  ])("keeps each side intact for %s → %s", async (original, modified) => {
    await render(
      <>
        <div data-side="original">
          <DiffHighlight mode="original" modified={modified} original={original} />
        </div>
        <div data-side="modified">
          <DiffHighlight mode="modified" modified={modified} original={original} />
        </div>
      </>,
    );

    expect(container.querySelector('[data-side="original"]')!.textContent).toBe(original);
    expect(container.querySelector('[data-side="modified"]')!.textContent).toBe(modified);
    expect(container.querySelector('[data-side="modified"] .line-through')).toBeNull();
  });

  it("shows names separately while retaining complete paths on each column", async () => {
    const entry = result();

    await render(<PreviewItem commonPrefix="/library/" result={entry} showFullPaths={false} />);

    const cells = container.querySelectorAll('[role="cell"]');

    expect(cells[0].textContent).toBe("old-1.jpg");
    expect(cells[1].textContent).toBe("new-1.jpg");
    expect(cells[0]).toHaveAttribute("title", entry.originalPath);
    expect(cells[1]).toHaveAttribute("title", entry.modifiedPath);

    await render(<PreviewItem showFullPaths commonPrefix="/library/" result={entry} />);
    expect(container.querySelectorAll('[role="cell"]')[0]).toHaveTextContent("/library/albums/");
    expect(container.querySelectorAll('[role="cell"]')[1]).toHaveTextContent("/library/albums/");
  });

  it("preserves changed Windows directories without treating them as a shared prefix", async () => {
    const entry = {
      ...result(),
      originalPath: "C:\\library\\old-1.jpg",
      modifiedPath: "D:\\sorted\\new-1.jpg",
    };

    await render(<PreviewItem showFullPaths commonPrefix="C:\\library\\" result={entry} />);

    const cells = container.querySelectorAll('[role="cell"]');

    expect(cells[0]).toHaveTextContent("C:\\library\\");
    expect(cells[1]).toHaveTextContent("D:\\sorted\\");
    expect(cells[1]).not.toHaveTextContent("C:\\library\\");
  });

  it("virtualizes long lists and reaches the final row beneath the fixed header", async () => {
    const entries = Array.from({ length: 100 }, (_, index) => result(index + 1));

    await render(<PreviewList showFullPaths commonPrefix="/library/" results={entries} />);

    expect(container.querySelector('[role="table"]')).toHaveAttribute("aria-rowcount", "101");
    expect(container.querySelectorAll('[role="columnheader"]')).toHaveLength(2);
    expect(container.querySelectorAll('[role="cell"]')).toHaveLength(18);
    expect(container.textContent).not.toContain("new-100.jpg");

    const scroller = container.querySelector<HTMLElement>(".ReactVirtualized__Grid")!;

    await act(async () => {
      scroller.scrollTop = entries.length * PREVIEW_ROW_HEIGHT - 224;
      scroller.dispatchEvent(new Event("scroll", { bubbles: true }));
    });

    const lastRow = container.querySelector<HTMLElement>('[aria-rowindex="101"]')!;

    expect(lastRow).toHaveTextContent("new-100.jpg");
    expect(lastRow.style.height).toBe(`${PREVIEW_ROW_HEIGHT}px`);
    expect(Number.parseFloat(lastRow.style.top) + PREVIEW_ROW_HEIGHT).toBe(
      entries.length * PREVIEW_ROW_HEIGHT,
    );
    expect(scroller.querySelector('[role="columnheader"]')).toBeNull();
  });

  it("replaces stale rows with loading or empty feedback", async () => {
    await render(<PreviewList commonPrefix="" results={[result()]} showFullPaths={false} />);
    expect(container.textContent).toContain("new-1.jpg");

    await render(
      <PreviewList isLoading commonPrefix="" results={[result()]} showFullPaths={false} />,
    );
    expect(container.querySelector('[role="status"]')).toHaveTextContent(
      "FileNameModifier.Loading",
    );
    expect(container.querySelector('[role="cell"]')).toBeNull();

    await render(<PreviewList commonPrefix="" results={[]} showFullPaths={false} />);
    expect(container.querySelector('[role="status"]')).toHaveTextContent(
      "FileNameModifier.NoPreviewResults",
    );
  });
});
