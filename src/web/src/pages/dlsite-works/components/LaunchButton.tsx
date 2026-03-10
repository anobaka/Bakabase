import { useTranslation } from "react-i18next";
import { Button, Chip, Popover, PopoverContent, PopoverTrigger, Spinner, Switch } from "@heroui/react";
import { AiOutlineDownload, AiOutlinePlayCircle } from "react-icons/ai";
import { MdOutlineRocketLaunch } from "react-icons/md";
import { CheckCircleOutlined } from "@ant-design/icons";

import { useDependentComponentContextsStore } from "@/stores/dependentComponentContexts";
import dependentComponentIds from "@/core/models/Constants/DependentComponentIds";
import { DependentComponentStatus } from "@/sdk/constants";
import BApi from "@/sdk/BApi";

/** Launch button with integrated LE configuration popover. */
export function LaunchButton({
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
  const isLeInstalled = leContext?.status === DependentComponentStatus.Installed;
  const isLeInstalling = leContext?.status === DependentComponentStatus.Installing;
  const launchDisabled = isLeAvailable && useLocaleEmulator && !isLeInstalled;

  const handleInstallLe = async () => {
    await BApi.component.installDependentComponent({ id: dependentComponentIds.LocaleEmulator });
  };

  const renderLeStatus = () => {
    if (!isLeAvailable) {
      return (
        <Chip color="default" size="sm" variant="flat">
          {t("resourceSource.dlsite.le.notAvailableOnPlatform")}
        </Chip>
      );
    }

    if (isLeInstalling) {
      return (
        <Chip
          color="warning"
          size="sm"
          startContent={<Spinner size="sm" />}
          variant="flat"
        >
          {t("resourceSource.dlsite.le.installing")}
          {leContext?.installationProgress != null && ` ${leContext.installationProgress}%`}
        </Chip>
      );
    }

    if (isLeInstalled) {
      return (
        <Chip
          color="success"
          size="sm"
          startContent={<CheckCircleOutlined className="text-xs" />}
          variant="flat"
        >
          {t("resourceSource.dlsite.le.installed")}
        </Chip>
      );
    }

    return (
      <Button
        color="warning"
        size="sm"
        startContent={<AiOutlineDownload className="text-lg" />}
        variant="flat"
        onPress={handleInstallLe}
      >
        {t("resourceSource.dlsite.le.install")}
      </Button>
    );
  };

  return (
    <Popover placement="bottom">
      <PopoverTrigger>
        {/* Wrap in span so Popover works on disabled button */}
        <span>
          <Button
            color="success"
            isDisabled={launchDisabled}
            isIconOnly
            size="sm"
            variant="light"
            onPress={() => onLaunch(workId)}
          >
            {useLocaleEmulator && isLeAvailable
              ? <MdOutlineRocketLaunch className="text-lg" />
              : <AiOutlinePlayCircle className="text-lg" />}
          </Button>
        </span>
      </PopoverTrigger>
      <PopoverContent>
        <div className="flex flex-col gap-2 p-2">
          {isLeAvailable && (
            <div className="flex items-center justify-between gap-4">
              <span className="text-sm whitespace-nowrap">
                {t("resourceSource.dlsite.label.useLocaleEmulator")}
              </span>
              <Switch
                isSelected={useLocaleEmulator}
                size="sm"
                onValueChange={(v) => onToggleUseLocaleEmulator(workId, v)}
              />
            </div>
          )}
          <div className="flex items-center">
            {renderLeStatus()}
          </div>
        </div>
      </PopoverContent>
    </Popover>
  );
}
