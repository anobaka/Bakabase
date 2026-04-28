"use client";

import React from "react";

import PropertyValueRenderer from "@/components/Property/components/PropertyValueRenderer";
import type { IProperty } from "@/components/Property/models";

export interface DataCardLayoutItem {
  propertyId: number;
  x: number;
  y: number;
  w: number;
  h: number;
  hideLabel?: boolean;
  hideEmpty?: boolean;
}

export interface DataCardBodyCardType {
  displayTemplate?: {
    cols?: number;
    rows?: number;
    layout?: DataCardLayoutItem[];
  };
}

export interface DataCardBodyCard {
  propertyValues?: { propertyId: number; value?: string; scope?: number }[];
}

interface DataCardBodyProps {
  card: DataCardBodyCard;
  cardType: DataCardBodyCardType;
  allProperties: IProperty[];
  /**
   * Cell size in px.
   * - number: fixed mode — every grid cell is cellPx × cellPx. Pair with an
   *   outer width of `cols * cellPx + padding` for WYSIWYG sizing.
   * - undefined: responsive mode — grid fills its parent's width and
   *   preserves the configured cols:rows aspect ratio via `aspect-ratio`.
   */
  cellPx?: number;
  className?: string;
}

// p-3 both axes (24px) + 1px border each side (2px) that callers typically
// apply on the outer Card.
export const DATA_CARD_OUTER_CHROME_PX = 26;
// Inter-cell gap inside the grid — must match the Tailwind `gap-*` class
// used on the grid element below (gap-2 = 8px).
export const DATA_CARD_CELL_GAP_PX = 8;

/**
 * Returns the outer card width in fixed mode, accounting for content (cols *
 * cellPx + inter-cell gaps), padding, and the typical 1px border. Returns
 * `undefined` if the card type has no layout template — in which case the
 * caller should pick its own fallback width.
 */
export function computeFixedOuterWidth(
  cardType: DataCardBodyCardType,
  cellPx: number,
): number | undefined {
  const tpl = cardType.displayTemplate;
  const cols = tpl?.cols ?? 0;
  const rows = tpl?.rows ?? 0;
  const hasLayout = cols > 0 && rows > 0 && (tpl?.layout?.length ?? 0) > 0;
  if (!hasLayout) return undefined;
  const gridWidth = cols * cellPx + Math.max(0, cols - 1) * DATA_CARD_CELL_GAP_PX;
  return gridWidth + DATA_CARD_OUTER_CHROME_PX;
}

const DataCardBody: React.FC<DataCardBodyProps> = ({
  card,
  cardType,
  allProperties,
  cellPx,
  className,
}) => {
  const tpl = cardType.displayTemplate;
  const cols = tpl?.cols ?? 0;
  const rows = tpl?.rows ?? 0;
  const layout = tpl?.layout ?? [];
  const hasLayout = cols > 0 && rows > 0 && layout.length > 0;

  if (!hasLayout) {
    return (
      <div className={`flex flex-col gap-2${className ? ` ${className}` : ""}`}>
        {card.propertyValues?.map((pv) => {
          const prop = allProperties.find((p) => p.id === pv.propertyId);
          if (!prop) return null;
          return (
            <div key={pv.propertyId} className="flex items-center gap-2 min-w-0">
              <span className="text-xs text-default-500 flex-shrink-0">
                {prop.name}
              </span>
              <div className="min-w-0">
                <PropertyValueRenderer
                  isReadonly
                  dbValue={pv.value || undefined}
                  property={prop}
                  size="sm"
                />
              </div>
            </div>
          );
        })}
      </div>
    );
  }

  const gridStyle: React.CSSProperties =
    cellPx !== undefined
      ? {
          gridTemplateColumns: `repeat(${cols}, ${cellPx}px)`,
          gridTemplateRows: `repeat(${rows}, ${cellPx}px)`,
        }
      : {
          aspectRatio: `${cols} / ${rows}`,
          gridTemplateColumns: `repeat(${cols}, minmax(0, 1fr))`,
          gridTemplateRows: `repeat(${rows}, minmax(0, 1fr))`,
        };

  return (
    <div
      className={`grid gap-2${className ? ` ${className}` : ""}`}
      style={gridStyle}
    >
      {layout.map((item) => {
        const prop = allProperties.find((p) => p.id === item.propertyId);
        if (!prop) return null;
        const pv = card.propertyValues?.find(
          (v) => v.propertyId === item.propertyId,
        );
        if (item.hideEmpty && !pv?.value) return null;
        return (
          <div
            key={item.propertyId}
            className="min-w-0 min-h-0 overflow-hidden flex flex-col gap-0.5"
            style={{
              gridColumn: `${item.x + 1} / span ${item.w}`,
              gridRow: `${item.y + 1} / span ${item.h}`,
            }}
          >
            {!item.hideLabel && (
              <span className="text-xs text-default-500 truncate">
                {prop.name}
              </span>
            )}
            <div className="min-w-0">
              <PropertyValueRenderer
                isReadonly
                attachmentPropertyValueRendererProps={{ fill: true }}
                dbValue={pv?.value || undefined}
                property={prop}
                size="sm"
              />
            </div>
          </div>
        );
      })}
    </div>
  );
};

export default DataCardBody;
