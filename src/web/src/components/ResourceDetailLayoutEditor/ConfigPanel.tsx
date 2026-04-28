import type { BlockPlacement, DetailLayoutConfig, SectionId } from "./types";

import React from "react";
import { Button, Input, Select, SelectItem, Slider } from "@heroui/react";
import { AiOutlineEye, AiOutlineEyeInvisible } from "react-icons/ai";
import { useTranslation } from "react-i18next";

import { ALL_SECTIONS } from "./defaultLayout";
import { clampSpan, settleLayout } from "./masonry";

type Props = {
  config: DetailLayoutConfig;
  onChange: (next: DetailLayoutConfig) => void;
  onReset: () => void;
  description?: React.ReactNode;
};

export function ConfigPanel({ config, onChange, onReset, description }: Props) {
  const { t } = useTranslation();
  const visibleIds = new Set(config.blocks.map((b) => b.id));

  const hideSection = (id: SectionId) => {
    const target = config.blocks.find((b) => b.id === id);

    if (!target) return;
    onChange({
      ...config,
      blocks: settleLayout(config.blocks.filter((b) => b.id !== id)),
      hidden: [...config.hidden, target],
    });
  };

  // Restore a hidden section. Drops it at the bottom-left using remembered
  // col/row spans, then resolves any overlaps by pushing neighbors down.
  const showSection = (id: SectionId) => {
    const remembered = config.hidden.find((h) => h.id === id);
    const colSpan = clampSpan(remembered?.colSpan ?? 4, config.gridCols);
    const rowSpan = Math.max(1, remembered?.rowSpan ?? 2);
    const bottomRow = config.blocks.reduce((m, b) => Math.max(m, b.rowStart + b.rowSpan), 0);

    const restored: BlockPlacement = {
      id,
      colStart: Math.max(0, Math.min(config.gridCols - colSpan, remembered?.colStart ?? 0)),
      colSpan,
      rowStart: bottomRow,
      rowSpan,
    };

    const nextBlocks = settleLayout([...config.blocks, restored], { movedId: id });

    onChange({
      ...config,
      blocks: nextBlocks,
      hidden: config.hidden.filter((h) => h.id !== id),
    });
  };

  return (
    <div className="flex flex-col gap-3 p-3 border-1 rounded-medium border-default-200 dark:border-default-100 bg-content1 w-80 shrink-0">
      <div className="flex items-center justify-between">
        <span className="font-semibold">{t<string>("resource.detailLayout.configHeader")}</span>
        <Button size="sm" variant="light" onPress={onReset}>
          {t<string>("resource.detailLayout.reset")}
        </Button>
      </div>

      {description ? (
        <div className="text-tiny text-default-500 leading-5">{description}</div>
      ) : null}

      <div className="flex flex-col gap-2">
        <Slider
          getValue={(v) => `${v}%`}
          label={t<string>("resource.detailLayout.modalWidth")}
          maxValue={95}
          minValue={50}
          size="sm"
          step={1}
          value={config.modalWidthPercent}
          onChange={(v) => onChange({ ...config, modalWidthPercent: Array.isArray(v) ? v[0] : v })}
        />
        <Slider
          getValue={(v) => `${v}%`}
          label={t<string>("resource.detailLayout.modalHeight")}
          maxValue={95}
          minValue={50}
          size="sm"
          step={1}
          value={config.modalHeightPercent}
          onChange={(v) => onChange({ ...config, modalHeightPercent: Array.isArray(v) ? v[0] : v })}
        />
        <div className="flex items-center gap-2">
          <Select
            className="max-w-[120px]"
            label={t<string>("resource.detailLayout.gridCols")}
            selectedKeys={[String(config.gridCols)]}
            size="sm"
            onChange={(e) => {
              const n = Number(e.target.value);

              if (!n) return;
              // Clamp colStart/colSpan into the new grid. Rows are unchanged.
              const next = config.blocks.map((b) => {
                const span = Math.max(1, Math.min(n, b.colSpan));
                const start = Math.max(0, Math.min(n - span, b.colStart));

                return { ...b, colSpan: span, colStart: start };
              });

              onChange({ ...config, gridCols: n, blocks: next });
            }}
          >
            {["4", "6", "8", "12"].map((n) => (
              <SelectItem key={n}>{n}</SelectItem>
            ))}
          </Select>
          <Input
            className="max-w-[120px]"
            label={t<string>("resource.detailLayout.gap")}
            size="sm"
            type="number"
            value={String(config.gap)}
            onValueChange={(v) => onChange({ ...config, gap: Math.max(0, Number(v) || 0) })}
          />
        </div>
      </div>

      <div className="h-px bg-default-200 dark:bg-default-100" />

      <div>
        <div className="text-xs text-default-500 mb-1">
          {t<string>("resource.detailLayout.sectionsHeader")}
        </div>
        <div className="flex flex-wrap gap-1">
          {ALL_SECTIONS.map((s) => {
            const visible = visibleIds.has(s.id);

            return (
              <Button
                key={s.id}
                color={visible ? "primary" : "default"}
                size="sm"
                startContent={visible ? <AiOutlineEye /> : <AiOutlineEyeInvisible />}
                variant={visible ? "flat" : "bordered"}
                onPress={() => (visible ? hideSection(s.id) : showSection(s.id))}
              >
                {t<string>(`resource.detailLayout.section.${s.id}`)}
              </Button>
            );
          })}
        </div>
      </div>
    </div>
  );
}
