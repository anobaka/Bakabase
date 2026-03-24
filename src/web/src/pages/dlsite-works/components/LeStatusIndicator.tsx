import { useTranslation } from "react-i18next";
import { Button, Chip, Spinner, Tooltip } from "@heroui/react";
import { AiOutlineDownload } from "react-icons/ai";
import { CheckCircleOutlined } from "@ant-design/icons";

import { useDependentComponentContextsStore } from "@/stores/dependentComponentContexts";
import dependentComponentIds from "@/core/models/Constants/DependentComponentIds";
import { DependentComponentStatus } from "@/sdk/constants";
import BApi from "@/sdk/BApi";

/** Header-level LE status indicator: shows install button, installing progress, or ready chip. */
export function LeStatusIndicator() {
  const { t } = useTranslation();

  const leContext = useDependentComponentContextsStore(
    (s) => s.contexts.find((c) => c.id === dependentComponentIds.LocaleEmulator),
  );

  // Hide entirely when LE is not available on the current platform
  if (leContext && !leContext.isAvailableOnCurrentPlatform) {
    return (
      <Chip color="default" size="sm" variant="flat">
        {t("resourceSource.dlsite.le.notAvailableOnPlatform")}
      </Chip>
    );
  }

  const isInstalling = leContext?.status === DependentComponentStatus.Installing;
  const isInstalled = leContext?.status === DependentComponentStatus.Installed;

  const handleInstall = async () => {
    await BApi.component.installDependentComponent({ id: dependentComponentIds.LocaleEmulator });
  };

  if (isInstalling) {
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

  if (isInstalled) {
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
    <Tooltip content={t("resourceSource.dlsite.le.notReady")}>
      <Button
        color="warning"
        size="sm"
        startContent={<AiOutlineDownload className="text-lg" />}
        variant="flat"
        onPress={handleInstall}
      >
        {t("resourceSource.dlsite.le.install")}
      </Button>
    </Tooltip>
  );
}
