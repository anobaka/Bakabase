import { useTranslation } from "react-i18next";
import { Button, Switch, Tooltip } from "@heroui/react";
import { AiOutlinePlayCircle } from "react-icons/ai";

import { useDependentComponentContextsStore } from "@/stores/dependentComponentContexts";
import dependentComponentIds from "@/core/models/Constants/DependentComponentIds";
import { DependentComponentStatus } from "@/sdk/constants";

/** Per-row LE controls: launch button and LE toggle for a downloaded work. */
export function LeWorkControls({
  workId,
  useLocaleEmulator,
  onLaunch,
  onToggleUseLocaleEmulator,
}: {
  workId: string;
  useLocaleEmulator: boolean;
  onLaunch: (workId: string) => void;
  onToggleUseLocaleEmulator: (workId: string, useLocaleEmulator: boolean) => void;
}) {
  const { t } = useTranslation();

  const leContext = useDependentComponentContextsStore(
    (s) => s.contexts.find((c) => c.id === dependentComponentIds.LocaleEmulator),
  );

  const isLeAvailable = leContext?.isAvailableOnCurrentPlatform !== false;
  const isLeReady = leContext?.status === DependentComponentStatus.Installed;
  const launchDisabled = isLeAvailable && useLocaleEmulator && !isLeReady;

  return (
    <>
      <Tooltip
        content={
          launchDisabled
            ? t("resourceSource.dlsite.le.notReady")
            : t("resourceSource.dlsite.action.launch")
        }
      >
        {/* Wrap in span so Tooltip works on disabled button */}
        <span>
          <Button
            color="success"
            isDisabled={launchDisabled}
            isIconOnly
            size="sm"
            variant="light"
            onPress={() => onLaunch(workId)}
          >
            <AiOutlinePlayCircle className="text-lg" />
          </Button>
        </span>
      </Tooltip>
      {isLeAvailable && (
        <Tooltip content={t("resourceSource.dlsite.label.useLocaleEmulator")}>
          <div>
            <Switch
              isSelected={useLocaleEmulator}
              size="sm"
              onValueChange={(v) => onToggleUseLocaleEmulator(workId, v)}
            />
          </div>
        </Tooltip>
      )}
    </>
  );
}
