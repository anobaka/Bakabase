"use client";
import React, { useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import { readTextPreview, type TextPreview } from "./preview";
import { buildLineOffsets, findLineAtOffset, getVisibleLineRange } from "./layout";

import { Spinner } from "@/components/bakaui";
import envConfig from "@/config/env";
import "./index.scss";

interface TextReaderProps {
  file?: string;
  style?: React.CSSProperties;
  onLoad?: () => void;
  onError?: (error: string) => void;
  className?: string;
}
const TextReader = ({ file, style, onLoad, onError, className = "" }: TextReaderProps) => {
  const { t } = useTranslation();
  const [preview, setPreview] = useState<TextPreview>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
  const [encoding, setEncoding] = useState("utf-8");
  const [fontSize, setFontSize] = useState(14);
  const [wrap, setWrap] = useState(true);
  const [attempt, setAttempt] = useState(0);
  const [scrollTop, setScrollTop] = useState(0);
  const [size, setSize] = useState({ height: 480, width: 800 });
  const viewport = useRef<HTMLDivElement>(null);
  const lineContainer = useRef<HTMLDivElement>(null);
  const callbacks = useRef({ onLoad, onError });

  callbacks.current = { onLoad, onError };
  useLayoutEffect(() => {
    const element = viewport.current;

    if (!element) return;
    // An unconstrained flex ancestor can report the whole document's height as
    // the viewport. Never let that disable virtualization and mount every line.
    const updateSize = () => {
      const height = Math.min(element.clientHeight || 480, Math.max(1, window.innerHeight - 128));
      const width = element.clientWidth || 800;

      setSize((current) =>
        current.height === height && current.width === width ? current : { height, width },
      );
    };

    updateSize();
    const observer = new ResizeObserver(updateSize);

    observer.observe(element);
    window.addEventListener("resize", updateSize);

    return () => {
      observer.disconnect();
      window.removeEventListener("resize", updateSize);
    };
  }, [loading, error]);
  useEffect(() => {
    let cancelled = false;
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), 15_000);

    setLoading(true);
    setError(undefined);
    setPreview(undefined);
    setScrollTop(0);
    if (!file) {
      setLoading(false);
      clearTimeout(timer);

      return;
    }
    const route = file.includes("!") ? "play" : "raw";

    void readTextPreview(
      `${envConfig.apiEndpoint}/file/${route}?fullname=${encodeURIComponent(file)}`,
      controller.signal,
      encoding,
    )
      .then((result) => {
        if (!cancelled) {
          setPreview(result);
          callbacks.current.onLoad?.();
        }
      })
      .catch((failure) => {
        if (cancelled) return;
        const message = controller.signal.aborted
          ? t("mediaPlayer.text.timeout")
          : t("mediaPlayer.text.failed", { reason: failure.message });

        setError(message);
        callbacks.current.onError?.(message);
      })
      .finally(() => {
        clearTimeout(timer);
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
      clearTimeout(timer);
      controller.abort();
    };
  }, [file, encoding, attempt, t]);
  const lines = useMemo(() => preview?.text.split("\n") ?? [], [preview]);
  const lineHeight = Math.round(fontSize * 1.6);
  const layout = useMemo(
    () => ({ lines, fontSize, width: size.width, wrap }),
    [lines, fontSize, size.width, wrap],
  );
  const [measurements, setMeasurements] = useState<{
    layout?: typeof layout;
    heights: Map<number, number>;
  }>({ heights: new Map() });
  const offsets = useMemo(
    () =>
      buildLineOffsets(
        lines,
        lineHeight,
        fontSize,
        size.width,
        wrap,
        measurements.layout === layout ? measurements.heights : new Map(),
      ),
    [layout, lineHeight, measurements],
  );
  const { start, end } = getVisibleLineRange(offsets, scrollTop, size.height);
  const maxWidth = useMemo(() => Math.max(60, ...lines.map((line) => line.length)), [lines]);
  const previousLayout = useRef<{ layout: typeof layout; offsets: number[] }>();

  useLayoutEffect(() => {
    const element = viewport.current;
    const previous = previousLayout.current;

    // Keep the same source line visible when wrapping, font size or width changes,
    // and when measurements refine the estimated height of preceding rows.
    if (element && lines.length && previous && previous.layout.lines === lines) {
      const line = findLineAtOffset(previous.offsets, element.scrollTop);
      const withinLine = element.scrollTop - previous.offsets[line];
      const nextScrollTop = Math.max(
        0,
        Math.min(
          offsets[offsets.length - 1] - size.height,
          offsets[line] + Math.min(withinLine, offsets[line + 1] - offsets[line] - 1),
        ),
      );

      if (Math.abs(element.scrollTop - nextScrollTop) > 0.5) {
        element.scrollTop = nextScrollTop;
        setScrollTop(nextScrollTop);
      }
    }
    previousLayout.current = { layout, offsets };
  }, [layout, offsets, lines, size.height]);

  useLayoutEffect(() => {
    const rows = Array.from(lineContainer.current?.children ?? []) as HTMLDivElement[];

    if (!rows.length) return;
    const measure = () => {
      const heights = rows.map(
        (row) => [Number(row.dataset.line), row.getBoundingClientRect().height] as const,
      );

      setMeasurements((previous) => {
        const current = previous.layout === layout ? previous.heights : new Map<number, number>();
        const changed = heights.filter(
          ([index, height]) => height > 0 && Math.abs((current.get(index) ?? 0) - height) > 0.5,
        );

        if (!changed.length) return previous;
        const next = new Map(current);

        changed.forEach(([index, height]) => next.set(index, height));

        return { layout, heights: next };
      });
    };

    measure();
    const observer = new ResizeObserver(measure);

    rows.forEach((row) => observer.observe(row));

    return () => observer.disconnect();
  }, [layout, start, end, loading, error]);

  /* eslint-disable jsx-a11y/no-noninteractive-tabindex -- The named scroll region must receive keyboard focus for scrolling and selecting read-only text; it is not a button. */
  return (
    <div className={`text-reader ${className}`} style={style}>
      <div className="text-reader-toolbar">
        <span className="text-reader-count">
          {t("mediaPlayer.text.lines", { count: lines.length })}
        </span>
        <label>
          {t("mediaPlayer.text.encoding")}
          <select
            aria-label={t("mediaPlayer.text.encoding")}
            value={encoding}
            onChange={(event) => setEncoding(event.target.value)}
          >
            <option value="utf-8">UTF-8 / UTF-16</option>
            <option value="gb18030">GB18030</option>
            <option value="windows-1252">Windows-1252</option>
          </select>
        </label>
        <label className="text-reader-wrap">
          <input
            aria-label={t("mediaPlayer.text.wrap")}
            checked={wrap}
            type="checkbox"
            onChange={(event) => setWrap(event.target.checked)}
          />
          {t("mediaPlayer.text.wrap")}
        </label>
        <label>
          {t("mediaPlayer.text.fontSize")}
          <input
            aria-label={t("mediaPlayer.text.fontSize")}
            max={24}
            min={10}
            type="number"
            value={fontSize}
            onChange={(event) =>
              setFontSize(Math.max(10, Math.min(24, Number(event.target.value) || 14)))
            }
          />
        </label>
      </div>
      {preview?.truncated && (
        <div className="text-reader-notice" role="status">
          {t("mediaPlayer.text.truncated")}
        </div>
      )}
      {loading ? (
        <div className="media-player-empty" role="status">
          <Spinner />
          <span>{t("mediaPlayer.text.loading")}</span>
        </div>
      ) : error ? (
        <div className="media-player-empty" role="alert">
          <span>{error}</span>
          <button className="media-player-button" onClick={() => setAttempt((value) => value + 1)}>
            {t("mediaPlayer.retry")}
          </button>
        </div>
      ) : (
        <div
          ref={viewport}
          aria-label={t("mediaPlayer.text.preview")}
          className={`text-reader-viewport${wrap ? " text-reader-viewport-wrap" : ""}`}
          role="region"
          tabIndex={0}
          onScroll={(event) => setScrollTop(event.currentTarget.scrollTop)}
        >
          {preview?.text ? (
            <div
              ref={lineContainer}
              className="text-reader-lines"
              style={{
                height: offsets[offsets.length - 1],
                minWidth: wrap ? 0 : maxWidth * fontSize * 0.62 + 80,
              }}
            >
              {lines.slice(start, end).map((line, index) => (
                <div
                  key={start + index}
                  className="text-reader-line"
                  data-line={start + index}
                  style={{
                    top: offsets[start + index],
                    minHeight: lineHeight,
                    lineHeight: `${lineHeight}px`,
                    fontSize,
                  }}
                >
                  <span aria-hidden="true" className="text-reader-number">
                    {start + index + 1}
                  </span>
                  <span className="text-reader-content">{line || " "}</span>
                </div>
              ))}
            </div>
          ) : (
            <div className="media-player-empty">{t("mediaPlayer.text.empty")}</div>
          )}
        </div>
      )}
    </div>
  );
  /* eslint-enable jsx-a11y/no-noninteractive-tabindex */
};

export default TextReader;
