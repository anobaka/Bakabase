import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import MediaFooter from "./MediaFooter";

import { IwFsType, MediaType } from "@/sdk/constants";

afterEach(cleanup);

describe("media footer context", () => {
  it("keeps contextual actions available for unsupported bytes before preview initialization", () => {
    const operations = vi.fn(() => <button>media operation</button>);
    const extra = vi.fn((path: string) => <button>{`map:${path}`}</button>);

    render(
      <MediaFooter
        activeIndex={0}
        currentInitialized={false}
        entry={{
          path: "/games/start.exe",
          playPath: "/games/start.exe",
          type: IwFsType.Unknown,
          passwordsForDecompressing: [],
        }}
        mediaType={MediaType.Unknown}
        playing={false}
        renderFooterExtra={extra}
        renderOperations={operations}
        totalEntries={1}
        onNextEntry={() => {}}
        onPrevEntry={() => {}}
      />,
    );
    expect(screen.getByRole("button", { name: "map:/games/start.exe" })).toBeInTheDocument();
    expect(extra).toHaveBeenCalledWith("/games/start.exe");
    expect(operations).not.toHaveBeenCalled();
  });
});
