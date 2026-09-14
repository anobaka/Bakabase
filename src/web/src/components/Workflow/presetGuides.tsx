import type { WorkflowLabelSource } from "./builtinLabels";

import { useTranslation } from "react-i18next";

const guideKeys = new Map([
  ["Forum post + cloud drive", "sharedContent"],
  ["Direct download", "directDownload"],
  ["Magnet", "externalDownload"],
  ["Magnet download", "magnet"],
  ["Torrent download", "torrent"],
  ["ExHentai download", "exHentai"],
  ["Platform fetch", "platform"],
  ["Local directory", "localDirectory"],
  ["Download torrent contents", "torrentContents"],
  ["Parse post download information", "postParser"],
]);

/** Presentation metadata only; nodes and triggers decide execution and readiness. */
export const workflowPresetGuide = (workflow: WorkflowLabelSource) =>
  workflow.isBuiltin ? guideKeys.get(workflow.name) : undefined;

export const PresetUsage = ({ guide }: { guide: string }) => {
  const { t } = useTranslation();

  return (
    <dl className="space-y-2 text-xs leading-relaxed">
      {(["scenario", "configuration", "outcome"] as const).map((field) => (
        <div key={field}>
          <dt className="font-medium text-default-600">{t(`workflow.usage.${field}`)}</dt>
          <dd className="mt-0.5 text-default-500">{t(`workflow.preset.${guide}.${field}`)}</dd>
        </div>
      ))}
    </dl>
  );
};
