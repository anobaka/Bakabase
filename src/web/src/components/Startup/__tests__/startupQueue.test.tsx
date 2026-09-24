import type { StartupSurfaceId, StartupSurfaceStatus } from "../startupQueue";

import { act } from "@testing-library/react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import {
  STARTUP_SURFACES,
  nextStartupSurface,
  useStartupQueue,
  useStartupSurface,
} from "../startupQueue";

describe("nextStartupSurface", () => {
  it("puts the welcome first, then notices, then the release notes, then a page's guide", () => {
    expect(STARTUP_SURFACES).toEqual(["gettingStarted", "notices", "whatsNew", "pageGuide"]);
    expect(
      nextStartupSurface(
        {
          pageGuide: { status: "ready" },
          whatsNew: { status: "ready" },
          notices: { status: "ready" },
          gettingStarted: { status: "ready" },
        },
        null,
      ),
    ).toBe("gettingStarted");
    expect(
      nextStartupSurface(
        {
          pageGuide: { status: "ready" },
          whatsNew: { status: "ready" },
          notices: { status: "ready" },
        },
        null,
      ),
    ).toBe("notices");
    expect(
      nextStartupSurface({ pageGuide: { status: "ready" }, whatsNew: { status: "ready" } }, null),
    ).toBe("whatsNew");
  });

  it("waits while an earlier surface is still deciding", () => {
    expect(
      nextStartupSurface({ notices: { status: "deciding" }, whatsNew: { status: "ready" } }, null),
    ).toBeNull();
    // A later one deciding holds nobody up.
    expect(
      nextStartupSurface({ notices: { status: "ready" }, whatsNew: { status: "deciding" } }, null),
    ).toBe("notices");
  });

  it("skips surfaces with nothing to show, and surfaces that are not there", () => {
    expect(
      nextStartupSurface(
        { gettingStarted: { status: "idle" }, whatsNew: { status: "ready" } },
        null,
      ),
    ).toBe("whatsNew");
    expect(nextStartupSurface({}, null)).toBeNull();
  });

  it("never lets a surface open over the one on screen", () => {
    expect(
      nextStartupSurface(
        { gettingStarted: { status: "ready" }, notices: { status: "ready" } },
        "notices",
      ),
    ).toBe("notices");
    expect(
      nextStartupSurface(
        { gettingStarted: { status: "ready" }, notices: { status: "idle" } },
        "notices",
      ),
    ).toBe("gettingStarted");
  });

  it("gives no turn to a surface put off until the next launch", () => {
    expect(
      nextStartupSurface(
        { whatsNew: { status: "ready", deferred: true }, pageGuide: { status: "ready" } },
        null,
      ),
    ).toBe("pageGuide");
    expect(
      nextStartupSurface({ whatsNew: { status: "deciding", deferred: true } }, null),
    ).toBeNull();
  });
});

describe("useStartupSurface", () => {
  let host: HTMLDivElement;
  let root: Root;
  const deferrers = new Map<StartupSurfaceId, () => void>();

  const Surface = ({ id, status }: { id: StartupSurfaceId; status: StartupSurfaceStatus }) => {
    const { isTurn, deferRest } = useStartupSurface(id, status);

    deferrers.set(id, deferRest);

    return isTurn ? <div data-showing={id} /> : null;
  };

  const showing = () =>
    [...host.querySelectorAll("[data-showing]")].map((node) => node.getAttribute("data-showing"));

  const render = async (surfaces: Partial<Record<StartupSurfaceId, StartupSurfaceStatus>>) => {
    await act(async () =>
      root.render(
        <>
          {Object.entries(surfaces).map(([id, status]) => (
            <Surface key={id} id={id as StartupSurfaceId} status={status!} />
          ))}
        </>,
      ),
    );
  };

  beforeEach(() => {
    vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
    useStartupQueue.setState({ slots: {}, current: null });
    deferrers.clear();
    host = document.createElement("div");
    document.body.appendChild(host);
    root = createRoot(host);
  });

  afterEach(async () => {
    await act(async () => root.unmount());
    host.remove();
  });

  it("shows one surface at a time and hands the turn on when it is done", async () => {
    await render({ whatsNew: "ready", notices: "deciding", gettingStarted: "ready" });
    expect(showing()).toEqual(["gettingStarted"]);

    await render({ whatsNew: "ready", notices: "deciding", gettingStarted: "idle" });
    expect(showing()).toEqual([]);

    await render({ whatsNew: "ready", notices: "ready", gettingStarted: "idle" });
    expect(showing()).toEqual(["notices"]);

    await render({ whatsNew: "ready", notices: "idle", gettingStarted: "idle" });
    expect(showing()).toEqual(["whatsNew"]);
  });

  it("does not open an earlier surface that arrives while a later one is showing", async () => {
    await render({ notices: "ready" });
    expect(showing()).toEqual(["notices"]);

    await render({ notices: "ready", gettingStarted: "ready" });
    expect(showing()).toEqual(["notices"]);

    await render({ notices: "idle", gettingStarted: "ready" });
    expect(showing()).toEqual(["gettingStarted"]);
  });

  it("stops waiting for a surface that unmounts", async () => {
    await render({ gettingStarted: "deciding", notices: "ready" });
    expect(showing()).toEqual([]);

    await render({ notices: "ready" });
    expect(showing()).toEqual(["notices"]);
  });

  it("puts off what waits after a surface that sent the user elsewhere", async () => {
    await render({ notices: "ready", whatsNew: "ready" });
    await act(async () => deferrers.get("notices")!());
    await render({ notices: "idle", whatsNew: "ready" });

    expect(showing()).toEqual([]);
    expect(useStartupQueue.getState().slots.whatsNew).toMatchObject({ deferred: true });

    // A page opened afterwards brings its own guide, which was not waiting then.
    await render({ notices: "idle", whatsNew: "ready", pageGuide: "ready" });
    expect(showing()).toEqual(["pageGuide"]);
  });
});
