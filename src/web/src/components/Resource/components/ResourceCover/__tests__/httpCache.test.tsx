import type { Resource } from "@/core/models/Resource";

import { act, cleanup, render, screen } from "@testing-library/react";
import { createRef } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";

import ResourceCover, { type IResourceCoverRef } from "..";

const coverResolution = vi.hoisted(() => ({ covers: ["/covers/local/7.jpg"], status: "ready" }));

vi.mock("@/hooks/useCoverResolution", () => ({
  useCoverResolution: () => coverResolution,
}));
vi.mock("@/features/management/resourceContext", () => ({ useResourceOwner: () => undefined }));
vi.mock("@/stores/appContext", () => ({
  useAppContextStore: (select: any) => select({ apiEndpoints: ["http://127.0.0.1:34567"] }),
}));
vi.mock("@/stores/remoteAccess", () => ({ useIsRemoteClient: () => false }));
vi.mock("@/stores/options", () => ({
  useUiOptionsStore: (select: any) => select({ data: {} }),
}));
vi.mock("@/features/management/resourceApi", () => ({ managedResourceCoverUrl: vi.fn() }));
vi.mock("@/components/MediaPreviewer", () => ({ default: () => null }));
vi.mock("../components/FallbackCover.tsx", () => ({ default: () => null }));
vi.mock("@/components/bakaui", () => ({
  Carousel: ({ children }: any) => children,
  Tooltip: ({ children }: any) => children,
  Image: ({ src }: any) => <span aria-label="Resource cover" data-src={src} role="img" />,
  Spinner: () => null,
}));

afterEach(cleanup);
const resource = { id: 7, path: "/library/7" } as Resource;
const url = () => new URL(screen.getByRole("img").getAttribute("data-src")!);

describe("resource thumbnail HTTP cache policy", () => {
  it("uses the validating URL after refresh and after an entire page remount", () => {
    const ref = createRef<IResourceCoverRef>();
    const view = render(<ResourceCover ref={ref} resource={resource} showBiggerOnHover={false} />);
    const initial = url();

    expect(initial.searchParams.get("cacheVersion")).toBe("2");
    expect(initial.searchParams.get("path")).toBe("/covers/local/7.jpg");
    act(() => ref.current!.reload());
    expect(url().searchParams.get("v")).toBe("0.1");
    view.unmount();
    render(<ResourceCover resource={resource} showBiggerOnHover={false} />);
    expect(url().href).toBe(initial.href);
    expect(url().searchParams.get("cacheVersion")).toBe("2");
  });
});
