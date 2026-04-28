"use client";

import React, { useCallback, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";
import {
  CloseOutlined,
  EyeInvisibleOutlined,
  EyeOutlined,
  MinusCircleFilled,
  MinusCircleOutlined,
} from "@ant-design/icons";

import BApi from "@/sdk/BApi";
import {
  Button,
  Chip,
  Input,
  Modal,
} from "@/components/bakaui";
import type { IProperty } from "@/components/Property/models";
import type { DestroyableProps } from "@/components/bakaui/types";

interface LayoutItem {
  propertyId: number;
  x: number;
  y: number;
  w: number;
  h: number;
  hideLabel?: boolean;
  hideEmpty?: boolean;
}

interface DisplayTemplateDesignerProps extends DestroyableProps {
  cardTypeId: number;
  propertyIds: number[];
  allProperties: IProperty[];
  displayTemplate?: { cols?: number; rows?: number; layout?: LayoutItem[] };
  onSaved?: () => void;
}

const CELL_SIZE = 48; // px per grid cell for the editor UI
const MIN_COLS = 1;
const MAX_COLS = 12;
const MIN_ROWS = 1;
const MAX_ROWS = 12;

const DisplayTemplateDesigner = ({
  cardTypeId,
  propertyIds,
  allProperties,
  displayTemplate: initial,
  onSaved,
  onDestroyed,
}: DisplayTemplateDesignerProps) => {
  const { t } = useTranslation();

  const [cols, setCols] = useState(initial?.cols || 6);
  const [rows, setRows] = useState(initial?.rows || 4);
  const [layout, setLayout] = useState<LayoutItem[]>(() => {
    if (initial?.layout?.length) return initial.layout;
    // Default: stack each property in a row, full width
    return propertyIds.map((pid, i) => ({
      propertyId: pid,
      x: 0,
      y: i,
      w: Math.min(6, cols),
      h: 1,
    }));
  });

  // Unplaced properties = not in layout
  const placedIds = useMemo(() => new Set(layout.map((l) => l.propertyId)), [layout]);
  const unplacedProperties = useMemo(
    () => propertyIds.filter((pid) => !placedIds.has(pid)),
    [propertyIds, placedIds],
  );

  // Drag state
  const [dragging, setDragging] = useState<{
    propertyId: number;
    mode: "move" | "resize" | "place";
    offsetX: number;
    offsetY: number;
    startItem?: LayoutItem;
  } | null>(null);
  const [preview, setPreview] = useState<LayoutItem | null>(null);
  const gridRef = useRef<HTMLDivElement>(null);

  const getPropertyName = (pid: number) =>
    allProperties.find((p) => p.id === pid)?.name || `#${pid}`;

  const cellToGrid = (clientX: number, clientY: number) => {
    if (!gridRef.current) return { gx: 0, gy: 0 };
    const rect = gridRef.current.getBoundingClientRect();
    const gx = Math.floor((clientX - rect.left) / CELL_SIZE);
    const gy = Math.floor((clientY - rect.top) / CELL_SIZE);
    return { gx: Math.max(0, Math.min(gx, cols - 1)), gy: Math.max(0, Math.min(gy, rows - 1)) };
  };

  const hasOverlap = (item: LayoutItem, excludeId?: number) => {
    return layout.some(
      (l) =>
        l.propertyId !== excludeId &&
        l.x < item.x + item.w &&
        l.x + l.w > item.x &&
        l.y < item.y + item.h &&
        l.y + l.h > item.y,
    );
  };

  const clampItem = (item: LayoutItem): LayoutItem => ({
    ...item,
    x: Math.max(0, Math.min(item.x, cols - item.w)),
    y: Math.max(0, Math.min(item.y, rows - item.h)),
    w: Math.max(1, Math.min(item.w, cols - item.x)),
    h: Math.max(1, Math.min(item.h, rows - item.y)),
  });

  const handlePointerDown = (
    e: React.PointerEvent,
    propertyId: number,
    mode: "move" | "resize",
  ) => {
    e.preventDefault();
    e.stopPropagation();
    const item = layout.find((l) => l.propertyId === propertyId);
    if (!item) return;

    const { gx, gy } = cellToGrid(e.clientX, e.clientY);
    setDragging({
      propertyId,
      mode,
      offsetX: mode === "move" ? gx - item.x : 0,
      offsetY: mode === "move" ? gy - item.y : 0,
      startItem: { ...item },
    });
    setPreview({ ...item });
  };

  const handlePalettePointerDown = (e: React.PointerEvent, propertyId: number) => {
    e.preventDefault();
    setDragging({
      propertyId,
      mode: "place",
      offsetX: 0,
      offsetY: 0,
    });
    setPreview({ propertyId, x: 0, y: 0, w: 2, h: 1 });
  };

  const handlePointerMove = useCallback(
    (e: React.PointerEvent) => {
      if (!dragging) return;
      const { gx, gy } = cellToGrid(e.clientX, e.clientY);

      if (dragging.mode === "move" || dragging.mode === "place") {
        const w = dragging.startItem?.w ?? 2;
        const h = dragging.startItem?.h ?? 1;
        setPreview(
          clampItem({
            propertyId: dragging.propertyId,
            x: gx - dragging.offsetX,
            y: gy - dragging.offsetY,
            w,
            h,
          }),
        );
      } else if (dragging.mode === "resize" && dragging.startItem) {
        const si = dragging.startItem;
        setPreview(
          clampItem({
            propertyId: dragging.propertyId,
            x: si.x,
            y: si.y,
            w: Math.max(1, gx - si.x + 1),
            h: Math.max(1, gy - si.y + 1),
          }),
        );
      }
    },
    [dragging, cols, rows],
  );

  const handlePointerUp = useCallback(() => {
    if (!dragging || !preview) {
      setDragging(null);
      setPreview(null);
      return;
    }

    const clamped = clampItem(preview);
    if (!hasOverlap(clamped, dragging.propertyId)) {
      if (dragging.mode === "place") {
        setLayout((prev) => [...prev, clamped]);
      } else {
        setLayout((prev) =>
          prev.map((l) => (l.propertyId === dragging.propertyId ? clamped : l)),
        );
      }
    }

    setDragging(null);
    setPreview(null);
  }, [dragging, preview, layout]);

  const removeFromLayout = (propertyId: number) => {
    setLayout((prev) => prev.filter((l) => l.propertyId !== propertyId));
  };

  const toggleHideLabel = (propertyId: number) => {
    setLayout((prev) =>
      prev.map((l) =>
        l.propertyId === propertyId ? { ...l, hideLabel: !l.hideLabel } : l,
      ),
    );
  };

  const toggleHideEmpty = (propertyId: number) => {
    setLayout((prev) =>
      prev.map((l) =>
        l.propertyId === propertyId ? { ...l, hideEmpty: !l.hideEmpty } : l,
      ),
    );
  };

  const handleSave = async () => {
    await BApi.dataCardType.updateDataCardTypeDisplayTemplate(cardTypeId, {
      cols,
      rows,
      layout: layout.map((l: LayoutItem) => ({
        propertyId: l.propertyId,
        x: l.x,
        y: l.y,
        w: l.w,
        h: l.h,
        hideLabel: l.hideLabel ?? false,
        hideEmpty: l.hideEmpty ?? false,
      })),
    });
    toast.success(t("common.success.saved"));
    onSaved?.();
  };

  return (
    <Modal
      defaultVisible
      size="3xl"
      title={t("dataCard.type.displayTemplate")}
      onOk={handleSave}
      onDestroyed={onDestroyed}
    >
      <div className="flex flex-col gap-4">
        <p className="text-xs text-default-400">
          {t("dataCard.displayTemplate.description")}
        </p>

        {/* Grid size controls */}
        <div className="flex items-center gap-4">
          <div className="flex items-center gap-2">
            <span className="text-sm">{t("dataCard.displayTemplate.cols")}</span>
            <Input
              type="number"
              size="sm"
              className="w-20"
              value={String(cols)}
              onValueChange={(v) => setCols(Math.max(MIN_COLS, Math.min(MAX_COLS, Number(v) || MIN_COLS)))}
            />
          </div>
          <div className="flex items-center gap-2">
            <span className="text-sm">{t("dataCard.displayTemplate.rows")}</span>
            <Input
              type="number"
              size="sm"
              className="w-20"
              value={String(rows)}
              onValueChange={(v) => setRows(Math.max(MIN_ROWS, Math.min(MAX_ROWS, Number(v) || MIN_ROWS)))}
            />
          </div>
        </div>

        {/* Grid editor */}
        <div
          ref={gridRef}
          className="relative border border-default-300 rounded-lg bg-default-50 select-none"
          style={{ width: cols * CELL_SIZE, height: rows * CELL_SIZE }}
          onPointerMove={handlePointerMove}
          onPointerUp={handlePointerUp}
          onPointerLeave={handlePointerUp}
        >
          {/* Grid lines */}
          {Array.from({ length: cols - 1 }).map((_, i) => (
            <div
              key={`vc-${i}`}
              className="absolute top-0 bottom-0 border-l border-default-200"
              style={{ left: (i + 1) * CELL_SIZE }}
            />
          ))}
          {Array.from({ length: rows - 1 }).map((_, i) => (
            <div
              key={`hr-${i}`}
              className="absolute left-0 right-0 border-t border-default-200"
              style={{ top: (i + 1) * CELL_SIZE }}
            />
          ))}

          {/* Placed items */}
          {layout.map((item) => {
            const isBeingDragged = dragging?.propertyId === item.propertyId;
            return (
              <div
                key={item.propertyId}
                className={`absolute rounded border-2 flex items-center justify-center px-1 gap-1 overflow-hidden cursor-move ${
                  isBeingDragged
                    ? "border-primary-300 bg-primary-50 opacity-40"
                    : "border-primary-400 bg-primary-100"
                }`}
                style={{
                  left: item.x * CELL_SIZE + 1,
                  top: item.y * CELL_SIZE + 1,
                  width: item.w * CELL_SIZE - 2,
                  height: item.h * CELL_SIZE - 2,
                }}
                onPointerDown={(e) => handlePointerDown(e, item.propertyId, "move")}
              >
                <span className="text-xs truncate leading-none">
                  {getPropertyName(item.propertyId)}
                </span>
                {/* Toggle hide-when-empty */}
                <span
                  className="absolute top-0.5 right-10 text-[10px] cursor-pointer opacity-60 hover:opacity-100"
                  title={
                    item.hideEmpty
                      ? t("dataCard.displayTemplate.showEmpty")
                      : t("dataCard.displayTemplate.hideEmpty")
                  }
                  onPointerDown={(e) => e.stopPropagation()}
                  onClick={(e) => {
                    e.stopPropagation();
                    toggleHideEmpty(item.propertyId);
                  }}
                >
                  {item.hideEmpty ? <MinusCircleFilled /> : <MinusCircleOutlined />}
                </span>
                {/* Toggle label visibility */}
                <span
                  className="absolute top-0.5 right-5 text-[10px] cursor-pointer opacity-60 hover:opacity-100"
                  title={
                    item.hideLabel
                      ? t("dataCard.displayTemplate.showLabel")
                      : t("dataCard.displayTemplate.hideLabel")
                  }
                  onPointerDown={(e) => e.stopPropagation()}
                  onClick={(e) => {
                    e.stopPropagation();
                    toggleHideLabel(item.propertyId);
                  }}
                >
                  {item.hideLabel ? <EyeInvisibleOutlined /> : <EyeOutlined />}
                </span>
                {/* Remove */}
                <CloseOutlined
                  className="absolute top-0.5 right-0.5 text-[10px] text-danger-500 cursor-pointer opacity-60 hover:opacity-100"
                  onPointerDown={(e) => e.stopPropagation()}
                  onClick={(e) => {
                    e.stopPropagation();
                    removeFromLayout(item.propertyId);
                  }}
                />
                {/* Resize handle - bottom right corner */}
                <div
                  className="absolute bottom-0 right-0 w-3 h-3 cursor-se-resize flex items-center justify-center"
                  onPointerDown={(e) => {
                    e.stopPropagation();
                    handlePointerDown(e, item.propertyId, "resize");
                  }}
                >
                  <svg width="8" height="8" viewBox="0 0 8 8" className="text-primary-500 opacity-60 hover:opacity-100">
                    <path d="M7 1L1 7M7 4L4 7M7 7L7 7" stroke="currentColor" strokeWidth="1.2" strokeLinecap="round" />
                  </svg>
                </div>
              </div>
            );
          })}

          {/* Drag preview */}
          {preview && dragging && (
            <div
              className={`absolute rounded border-2 border-dashed pointer-events-none flex items-center justify-center overflow-hidden ${
                hasOverlap(preview, dragging.propertyId)
                  ? "border-danger-400 bg-danger-50"
                  : "border-success-400 bg-success-50"
              }`}
              style={{
                left: preview.x * CELL_SIZE + 1,
                top: preview.y * CELL_SIZE + 1,
                width: preview.w * CELL_SIZE - 2,
                height: preview.h * CELL_SIZE - 2,
              }}
            >
              <span className="text-xs px-1 truncate">
                {getPropertyName(preview.propertyId)}
              </span>
            </div>
          )}
        </div>

        {/* Hidden-rule summary (placed properties that may be hidden) */}
        {(() => {
          const hiddenItems = layout.filter((l) => l.hideLabel || l.hideEmpty);
          if (hiddenItems.length === 0) return null;
          return (
            <div>
              <label className="text-sm text-default-500 mb-2 block">
                {t("dataCard.displayTemplate.hiddenSummary")}
              </label>
              <div className="flex flex-col gap-1.5">
                {hiddenItems.map((item) => {
                  const reasons: string[] = [];
                  if (item.hideLabel) reasons.push(t("dataCard.displayTemplate.hideLabel"));
                  if (item.hideEmpty) reasons.push(t("dataCard.displayTemplate.hideEmpty"));
                  return (
                    <div key={item.propertyId} className="flex items-center gap-2">
                      <Chip size="sm" variant="flat" color="primary">
                        {getPropertyName(item.propertyId)}
                      </Chip>
                      <span className="text-xs text-default-500">
                        {reasons.join(" · ")}
                      </span>
                    </div>
                  );
                })}
              </div>
            </div>
          );
        })()}

        {/* Unplaced properties palette */}
        {unplacedProperties.length > 0 && (
          <div>
            <label className="text-sm text-default-500 mb-2 block">
              {t("dataCard.displayTemplate.unplaced")}
            </label>
            <div className="flex flex-wrap gap-2">
              {unplacedProperties.map((pid) => (
                <Chip
                  key={pid}
                  size="sm"
                  variant="bordered"
                  className="cursor-grab"
                  onPointerDown={(e) => handlePalettePointerDown(e, pid)}
                >
                  {getPropertyName(pid)}
                </Chip>
              ))}
            </div>
          </div>
        )}
      </div>
    </Modal>
  );
};

export default DisplayTemplateDesigner;
