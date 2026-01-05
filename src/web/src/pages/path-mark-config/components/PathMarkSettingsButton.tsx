import type { ButtonProps as NextUIButtonProps } from "@heroui/react";

import { useState, useCallback } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineSetting } from "react-icons/ai";

import { Button, Modal, Checkbox, toast } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { useResourceOptionsStore } from "@/stores/options";
import BApi from "@/sdk/BApi";

interface PathMarkSettingsButtonProps {
  buttonVariant?: "light" | "solid" | "bordered" | "flat";
  buttonSize?: "sm" | "md";
  className?: string;
}

const PathMarkSettingsButton = ({
  buttonVariant = "light",
  buttonSize = "sm",
  className,
}: PathMarkSettingsButtonProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const resourceOptionsStore = useResourceOptionsStore((state) => state);

  const [visible, setVisible] = useState(false);

  // Get sync immediately option from resource options
  const syncMarksImmediately =
    resourceOptionsStore.data?.synchronizationOptions?.syncMarksImmediately ?? false;

  // Toggle sync immediately option
  const handleToggleSyncImmediately = useCallback(
    async (checked: boolean) => {
      // Show confirmation dialog when enabling
      if (checked) {
        const confirmed = await new Promise<boolean>((resolve) => {
          const modal = createPortal(Modal, {
            defaultVisible: true,
            title: t("pathMarkConfig.modal.enableSyncImmediatelyTitle"),
            children: (
              <div className="flex flex-col gap-2">
                <p>{t("pathMarkConfig.tip.syncImmediately")}</p>
                <p className="text-warning">
                  {t("pathMarkConfig.warning.syncDataNote")}
                </p>
              </div>
            ),
            footer: {
              actions: ["cancel", "ok"],
              okProps: {
                children: t("pathMarkConfig.action.enable"),
              },
            },
            onOk: () => {
              resolve(true);
              modal.destroy();
            },
            onDestroyed: () => {
              resolve(false);
            },
          });
        });

        if (!confirmed) {
          return;
        }
      }

      try {
        await BApi.options.patchResourceOptions({
          synchronizationOptions: {
            ...resourceOptionsStore.data?.synchronizationOptions,
            syncMarksImmediately: checked,
          },
        });
      } catch (error) {
        console.error("Failed to update sync option", error);
        toast.danger(t("pathMarkConfig.error.updateOption"));
      }
    },
    [createPortal, resourceOptionsStore.data?.synchronizationOptions, t],
  );

  return (
    <>
      <Button
        isIconOnly
        className={className}
        size={buttonSize}
        variant={buttonVariant}
        onPress={() => setVisible(true)}
      >
        <AiOutlineSetting className="text-xl" />
      </Button>

      {visible && (
        <Modal
          footer={false}
          size="sm"
          title={t("pathMarkConfig.modal.settingsTitle")}
          visible={visible}
          onClose={() => setVisible(false)}
        >
          <div className="flex flex-col gap-4 py-2">
            <Checkbox
              isSelected={syncMarksImmediately}
              size="sm"
              onValueChange={handleToggleSyncImmediately}
            >
              <div className="flex flex-col">
                <span>{t("pathMarkConfig.label.syncImmediately")}</span>
                <span className="text-xs text-default-500">
                  {t("pathMarkConfig.tip.syncImmediately")}
                </span>
              </div>
            </Checkbox>
          </div>
        </Modal>
      )}
    </>
  );
};

PathMarkSettingsButton.displayName = "PathMarkSettingsButton";

export default PathMarkSettingsButton;
