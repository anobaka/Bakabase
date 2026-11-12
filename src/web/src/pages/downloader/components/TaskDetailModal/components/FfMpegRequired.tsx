"use client";

import { useTranslation } from "react-i18next";

import { Alert } from "@/components/bakaui";
import { DependentComponentStatus } from "@/sdk/constants";
import { useDependentComponentContextsStore } from "@/stores/dependentComponentContexts";
import dependentComponentIds from "@/core/models/Constants/DependentComponentIds";
import { dependencyManagerPath } from "@/components/Dependency/routes";

export default function FfMpegRequired() {
  const { t } = useTranslation();

  const dependentComponentContexts = useDependentComponentContextsStore((state) => state.contexts);

  const ffmpegState = dependentComponentContexts?.find(
    (d) => d.id === dependentComponentIds.FFMpeg,
  );

  const isFfmpegMissing = ffmpegState?.status !== DependentComponentStatus.Installed;

  // If we only want to show when missing and it's not missing, don't render
  if (!isFfmpegMissing) {
    return null;
  }

  // FFmpeg is missing, show error
  return (
    <div className="space-y-2">
      <Alert color="danger" title={t<string>("dependency.ffmpeg.required")} />
      <a
        className="text-sm text-primary"
        href={`#${dependencyManagerPath(dependentComponentIds.FFMpeg)}`}
      >
        {t("menu.dependencies")}
      </a>
    </div>
  );
}
