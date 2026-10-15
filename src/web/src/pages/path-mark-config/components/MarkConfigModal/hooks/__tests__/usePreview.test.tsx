import type { MarkConfig } from "../../types";
import type { PathMarkPreviewResult } from "../usePreview";

import { act, cleanup, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { usePreview } from "../usePreview";

import { PathMarkType, PathMatchMode, PropertyValueType } from "@/sdk/constants";

const { previewRequest } = vi.hoisted(() => ({ previewRequest: vi.fn() }));

vi.mock("@/sdk/BApi", () => ({
  default: { pathMark: { previewPathMarkMatchedPaths: previewRequest } },
}));

type PreviewResponse = { code?: number; data?: PathMarkPreviewResult[]; message?: string };
type PreviewProps = {
  rootPath?: string;
  markType: PathMarkType;
  config: MarkConfig;
};

const propertyConfig: MarkConfig = {
  matchMode: PathMatchMode.Layer,
  layer: 1,
  valueType: PropertyValueType.Dynamic,
  valueMatchMode: PathMatchMode.Layer,
  valueLayer: 0,
};
const libraryConfig: MarkConfig = {
  matchMode: PathMatchMode.Layer,
  layer: 1,
  mediaLibraryValueType: PropertyValueType.Dynamic,
  layerToMediaLibrary: 0,
};

const deferred = () => {
  let resolve!: (response: PreviewResponse) => void;
  let reject!: (error: Error) => void;
  const promise = new Promise<PreviewResponse>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });

  return { promise, resolve, reject };
};

const advanceDebounce = async () => {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(500);
  });
};

const renderPreview = (config = propertyConfig, markType = PathMarkType.Property) => {
  const initialProps: PreviewProps = { rootPath: "/media/精品", markType, config };

  return {
    ...renderHook(
      (props: PreviewProps) => usePreview(props.rootPath, props.markType, props.config),
      {
        initialProps,
      },
    ),
    initialProps,
  };
};

beforeEach(() => {
  vi.useFakeTimers();
  previewRequest.mockReset();
  previewRequest.mockResolvedValue({ data: [] });
});

afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

describe("usePreview", () => {
  it.each([
    {
      name: "property value type",
      markType: PathMarkType.Property,
      config: { ...propertyConfig, valueType: PropertyValueType.Fixed },
      update: { valueType: PropertyValueType.Dynamic },
      expected: { valueType: PropertyValueType.Dynamic },
    },
    {
      name: "fixed property value",
      markType: PathMarkType.Property,
      config: { ...propertyConfig, valueType: PropertyValueType.Fixed, fixedValue: "old" },
      update: { fixedValue: "new" },
      expected: { fixedValue: "new" },
    },
    {
      name: "property value layer",
      markType: PathMarkType.Property,
      config: propertyConfig,
      update: { valueLayer: -1 },
      expected: { valueLayer: -1 },
    },
    {
      name: "property extraction mode",
      markType: PathMarkType.Property,
      config: { ...propertyConfig, valueRegex: "(name)" },
      update: { valueMatchMode: PathMatchMode.Regex },
      expected: { valueRegex: "(name)" },
    },
    {
      name: "property value regex",
      markType: PathMarkType.Property,
      config: { ...propertyConfig, valueMatchMode: PathMatchMode.Regex, valueRegex: "old" },
      update: { valueRegex: "(new)" },
      expected: { valueRegex: "(new)" },
    },
    {
      name: "media library value type",
      markType: PathMarkType.MediaLibrary,
      config: { ...libraryConfig, mediaLibraryValueType: PropertyValueType.Fixed },
      update: { mediaLibraryValueType: PropertyValueType.Dynamic },
      expected: { valueType: PropertyValueType.Dynamic },
    },
    {
      name: "fixed media library",
      markType: PathMarkType.MediaLibrary,
      config: {
        ...libraryConfig,
        mediaLibraryValueType: PropertyValueType.Fixed,
        mediaLibraryId: 1,
      },
      update: { mediaLibraryId: 2 },
      expected: { mediaLibraryId: 2 },
    },
    {
      name: "media library value layer",
      markType: PathMarkType.MediaLibrary,
      config: libraryConfig,
      update: { layerToMediaLibrary: 1 },
      expected: { layerToMediaLibrary: 1 },
    },
    {
      name: "media library value regex",
      markType: PathMarkType.MediaLibrary,
      config: { ...libraryConfig, regexToMediaLibrary: "old" },
      update: { regexToMediaLibrary: "(new)" },
      expected: { regexToMediaLibrary: "(new)" },
    },
  ])(
    "refreshes when $name changes without changing path matching",
    async ({ markType, config, update, expected }) => {
      const { rerender, initialProps } = renderPreview(config, markType);

      await advanceDebounce();
      expect(previewRequest).toHaveBeenCalledTimes(1);

      rerender({ ...initialProps, config: { ...config, ...update } });
      await advanceDebounce();

      expect(previewRequest).toHaveBeenCalledTimes(2);
      expect(JSON.parse(previewRequest.mock.calls[1][0].configJson)).toMatchObject(expected);
    },
  );

  it("keeps the latest values when an older request finishes afterward", async () => {
    const older = deferred();

    previewRequest.mockReturnValueOnce(older.promise);
    const latest = [{ path: "/media/精品/七彩直播", propertyValue: "精品" }];
    const { result, rerender, initialProps } = renderPreview();

    await advanceDebounce();
    previewRequest.mockResolvedValueOnce({ data: latest });
    rerender({ ...initialProps, config: { ...propertyConfig, valueLayer: 1 } });
    await advanceDebounce();
    expect(result.current.results).toEqual(latest);

    await act(async () => {
      older.resolve({ data: [{ path: "/old", propertyValue: "outdated" }] });
    });

    expect(result.current.results).toEqual(latest);
    expect(result.current.resultsByPath).toEqual([
      { path: initialProps.rootPath, results: latest },
    ]);
    expect(result.current.error).toBeNull();
    expect(result.current.loading).toBe(false);
  });

  it.each(["response error", "rejection"])(
    "ignores an older %s while the current preview is loading",
    async (errorKind) => {
      const older = deferred();
      const current = deferred();

      previewRequest.mockReturnValueOnce(older.promise).mockReturnValueOnce(current.promise);
      const { result, rerender, initialProps } = renderPreview();

      await advanceDebounce();
      rerender({ ...initialProps, config: { ...propertyConfig, valueLayer: 1 } });
      await advanceDebounce();
      await act(async () => {
        if (errorKind === "response error") older.resolve({ code: 1, message: "outdated error" });
        else older.reject(new Error("outdated error"));
      });

      expect(result.current.error).toBeNull();
      expect(result.current.loading).toBe(true);

      await act(async () => {
        current.resolve({ data: [] });
      });
      expect(result.current.loading).toBe(false);
    },
  );

  it.each(["invalid config", "missing path"])(
    "clears the preview and ignores in-flight results after %s",
    async (change) => {
      const pending = deferred();

      previewRequest.mockReturnValueOnce(pending.promise);
      const { result, rerender, initialProps } = renderPreview();

      await advanceDebounce();
      rerender(
        change === "missing path"
          ? { ...initialProps, rootPath: undefined }
          : { ...initialProps, config: { ...propertyConfig, layer: undefined } },
      );

      expect(result.current.loading).toBe(false);
      await act(async () => {
        pending.resolve({ data: [{ path: "/old", propertyValue: "outdated" }] });
      });
      expect(result.current.results).toEqual([]);
      expect(result.current.resultsByPath).toEqual([]);
      expect(result.current.error).toBeNull();
      expect(previewRequest).toHaveBeenCalledTimes(1);
    },
  );
});
