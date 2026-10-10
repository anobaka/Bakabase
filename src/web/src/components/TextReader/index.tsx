"use client";
import React, { useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Spinner } from "@/components/bakaui";
import envConfig from "@/config/env";
import { readTextPreview, type TextPreview } from "./preview";
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
  const [attempt, setAttempt] = useState(0);
  const [scrollTop, setScrollTop] = useState(0);
  const [height, setHeight] = useState(480);
  const viewport = useRef<HTMLDivElement>(null);
  const callbacks = useRef({ onLoad, onError });
  callbacks.current = { onLoad, onError };
  useEffect(() => {
    const element = viewport.current;
    if (!element) return;
    // An unconstrained flex ancestor can report the whole document's height as
    // the viewport. Never let that disable virtualization and mount every line.
    const observer = new ResizeObserver(() =>
      setHeight(Math.min(element.clientHeight || 480, Math.max(1, window.innerHeight - 128))),
    );
    observer.observe(element);
    return () => observer.disconnect();
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
  const start = Math.max(0, Math.floor(scrollTop / lineHeight) - 6);
  const end = Math.min(lines.length, start + Math.ceil(height / lineHeight) + 12);
  const maxWidth = useMemo(() => Math.max(60, ...lines.map((line) => line.length)), [lines]);

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
        <label>
          {t("mediaPlayer.text.fontSize")}
          <input
            aria-label={t("mediaPlayer.text.fontSize")}
            type="number"
            min={10}
            max={24}
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
          className="text-reader-viewport"
          role="region"
          aria-label={t("mediaPlayer.text.preview")}
          tabIndex={0}
          onScroll={(event) => setScrollTop(event.currentTarget.scrollTop)}
        >
          {preview?.text ? (
            <div
              className="text-reader-lines"
              style={{
                height: lines.length * lineHeight,
                minWidth: maxWidth * fontSize * 0.62 + 64,
              }}
            >
              {lines.slice(start, end).map((line, index) => (
                <div
                  key={start + index}
                  className="text-reader-line"
                  style={{
                    top: (start + index) * lineHeight,
                    height: lineHeight,
                    lineHeight: `${lineHeight}px`,
                    fontSize,
                  }}
                >
                  <span className="text-reader-number" aria-hidden="true">
                    {start + index + 1}
                  </span>
                  <span>{line || " "}</span>
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
