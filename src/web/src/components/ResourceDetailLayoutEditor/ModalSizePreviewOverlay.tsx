import React, { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";

type Props = {
  widthPercent: number;
  heightPercent: number;
};

// Transient dashed-rectangle overlay that visualizes the target modal size
// as the user moves the width/height sliders. Debounced so mid-drag values
// don't flash; auto-hides a few seconds after the last change. Pointer
// events disabled so it never blocks slider interaction.
const DEBOUNCE_MS = 200;
const AUTO_HIDE_MS = 2400;

export function ModalSizePreviewOverlay({ widthPercent, heightPercent }: Props) {
  const [visible, setVisible] = useState(false);
  const [dimensions, setDimensions] = useState({
    w: widthPercent,
    h: heightPercent,
  });
  // Skip the initial mount so we don't flash on open.
  const firstRender = useRef(true);
  const debounceTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const hideTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    if (firstRender.current) {
      firstRender.current = false;

      return;
    }

    if (debounceTimer.current) clearTimeout(debounceTimer.current);
    if (hideTimer.current) clearTimeout(hideTimer.current);

    debounceTimer.current = setTimeout(() => {
      setDimensions({ w: widthPercent, h: heightPercent });
      setVisible(true);
      hideTimer.current = setTimeout(() => setVisible(false), AUTO_HIDE_MS);
    }, DEBOUNCE_MS);

    return () => {
      if (debounceTimer.current) clearTimeout(debounceTimer.current);
    };
  }, [widthPercent, heightPercent]);

  useEffect(() => {
    return () => {
      if (debounceTimer.current) clearTimeout(debounceTimer.current);
      if (hideTimer.current) clearTimeout(hideTimer.current);
    };
  }, []);

  if (typeof document === "undefined") return null;

  return createPortal(
    <div
      aria-hidden
      className="pointer-events-none fixed inset-0 flex items-center justify-center"
      style={{
        zIndex: 9999,
        opacity: visible ? 1 : 0,
        transition: "opacity 220ms ease-out",
      }}
    >
      <div
        className="rounded-medium border-2 border-dashed border-primary/70 bg-primary/5 flex items-end justify-end p-2"
        style={{
          width: `${dimensions.w}vw`,
          height: `${dimensions.h}vh`,
        }}
      >
        <div className="bg-primary text-white text-xs rounded-small px-2 py-1 font-mono">
          {dimensions.w}% × {dimensions.h}%
        </div>
      </div>
    </div>,
    document.body,
  );
}
