import type { DetailLayoutConfig, SectionId } from "./types";

import React, { useState } from "react";
import { useTranslation } from "react-i18next";

import { ConfigPanel } from "./ConfigPanel";
import { DEFAULT_LAYOUT } from "./defaultLayout";
import { MasonryCanvas } from "./MasonryCanvas";
import { ModalSizePreviewOverlay } from "./ModalSizePreviewOverlay";

type Props = {
  value?: DetailLayoutConfig;
  defaultValue?: DetailLayoutConfig;
  onChange?: (next: DetailLayoutConfig) => void;
  renderSection: (id: SectionId) => React.ReactNode;
  // Controls whether the outer frame simulates a modal (width/height as vw/vh).
  simulateModal?: boolean;
  // Optional note rendered at the top of the config panel (e.g. to flag
  // that the canvas is showing sample data). Kept out of the canvas so the
  // drag area stays visually uncluttered.
  configDescription?: React.ReactNode;
};

export function ResourceDetailLayoutEditor({
  value,
  defaultValue,
  onChange,
  renderSection,
  simulateModal = true,
  configDescription,
}: Props) {
  const { t } = useTranslation();
  const [internal, setInternal] = useState<DetailLayoutConfig>(
    value ?? defaultValue ?? DEFAULT_LAYOUT,
  );
  const config = value ?? internal;
  const setConfig = (next: DetailLayoutConfig) => {
    if (value == null) setInternal(next);
    onChange?.(next);
  };

  const frameStyle: React.CSSProperties = simulateModal
    ? {
        width: `${config.modalWidthPercent}vw`,
        height: `${config.modalHeightPercent}vh`,
      }
    : { width: "100%", minHeight: 400 };

  return (
    <div className="flex gap-3 items-start">
      <div
        className="border-1 rounded-medium border-default-200 dark:border-default-100 bg-background overflow-hidden flex flex-col"
        style={frameStyle}
      >
        <div className="flex items-center gap-2 px-3 py-2 border-b-1 border-default-200 dark:border-default-100 shrink-0">
          <span className="font-semibold text-sm">
            {t<string>("resource.detailLayout.title")}
          </span>
          <span className="text-xs text-default-500">
            {config.modalWidthPercent}% × {config.modalHeightPercent}%
          </span>
        </div>
        <div className="flex-1 overflow-auto p-3">
          <MasonryCanvas config={config} renderSection={renderSection} onConfigChange={setConfig} />
        </div>
      </div>

      <ConfigPanel
        config={config}
        description={configDescription}
        onChange={setConfig}
        onReset={() => setConfig(defaultValue ?? DEFAULT_LAYOUT)}
      />

      <ModalSizePreviewOverlay
        heightPercent={config.modalHeightPercent}
        widthPercent={config.modalWidthPercent}
      />
    </div>
  );
}

export {
  ALL_SECTIONS,
  DEFAULT_LAYOUT,
  SECTION_HEIGHT_BEHAVIOR,
  normalizeLayoutConfig,
} from "./defaultLayout";
export { MasonryViewer } from "./MasonryViewer";
export { renderSamplePlaceholder } from "./SamplePlaceholders";
export type { BlockPlacement, DetailLayoutConfig, SectionHeightBehavior, SectionId } from "./types";
