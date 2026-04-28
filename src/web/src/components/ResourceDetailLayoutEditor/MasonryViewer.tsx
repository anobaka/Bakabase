import type { ComputedPosition, DetailLayoutConfig, SectionId } from "./types";

import React, { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";

import { packRuntime } from "./masonry";

type Props = {
  config: DetailLayoutConfig;
  renderSection: (id: SectionId) => React.ReactNode;
};

// Runtime viewer: column-aware waterfall. Ignores the designer's rowStart
// pixel positions and instead uses them purely as stacking priority — each
// block's top is computed from the measured bottoms of already-placed
// overlapping-column blocks. Dynamic heights thus stack cleanly.
export function MasonryViewer({ config, renderSection }: Props) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const itemObservers = useRef(new Map<SectionId, ResizeObserver>());
  const [containerWidth, setContainerWidth] = useState(0);
  const [heights, setHeights] = useState<Record<string, number>>({});

  useLayoutEffect(() => {
    const el = containerRef.current;

    if (!el) return;
    const ro = new ResizeObserver((entries) => {
      for (const entry of entries) setContainerWidth(entry.contentRect.width);
    });

    ro.observe(el);
    setContainerWidth(el.clientWidth);

    return () => ro.disconnect();
  }, []);

  const registerItem = useCallback((id: SectionId, el: HTMLDivElement | null) => {
    const observers = itemObservers.current;
    const existing = observers.get(id);

    if (existing) {
      existing.disconnect();
      observers.delete(id);
    }

    if (el) {
      const ro = new ResizeObserver((entries) => {
        for (const entry of entries) {
          const h = entry.contentRect.height;

          setHeights((prev) => (prev[id] === h ? prev : { ...prev, [id]: h }));
        }
      });

      ro.observe(el);
      observers.set(id, ro);
    }
  }, []);

  useEffect(() => {
    return () => {
      itemObservers.current.forEach((o) => o.disconnect());
      itemObservers.current.clear();
    };
  }, []);

  const { positions, containerHeight } = useMemo<{
    positions: Record<string, ComputedPosition>;
    containerHeight: number;
  }>(() => {
    if (containerWidth <= 0) return { positions: {}, containerHeight: 0 };

    return packRuntime({ config, containerWidth, heights });
  }, [config, containerWidth, heights]);

  return (
    <div ref={containerRef} className="relative w-full" style={{ height: containerHeight }}>
      {config.blocks.map((b) => {
        const pos = positions[b.id];
        const placed = pos != null;

        return (
          <div
            key={b.id}
            ref={(el) => registerItem(b.id, el)}
            className="absolute box-border"
            style={{
              top: pos?.top ?? 0,
              left: pos?.left ?? 0,
              width: pos?.width ?? 0,
              opacity: placed ? 1 : 0,
            }}
          >
            {renderSection(b.id)}
          </div>
        );
      })}
    </div>
  );
}
