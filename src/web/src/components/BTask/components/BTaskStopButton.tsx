"use client";

import type { ButtonProps } from "@/components/bakaui";

import { useTranslation } from "react-i18next";

import { Button, Modal, toast } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BApi from "@/sdk/BApi";
import { useBTasksStore } from "@/stores/bTasks";
import { cancelResourceMoveBatch } from "@/components/ResourceMovePanel/api";
import { refreshMovePanel } from "@/stores/resourceMovePanel";

type Props = {
  id: string;
} & ButtonProps;
const BTaskStopButton = (props: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const stop = async () => {
    if (props.id.startsWith("MoveResources:")) {
      try {
        await cancelResourceMoveBatch(props.id.slice("MoveResources:".length));
        await refreshMovePanel();
      } catch (error) {
        toast.danger(
          error instanceof Error ? error.message : t<string>("common.error.unknownError"),
        );
      }
      return;
    }
    const rsp = await BApi.backgroundTask.stopBackgroundTask(props.id, {
      confirm: false,
    });

    if (rsp.code == 202) {
      const task = useBTasksStore.getState().tasks.find((item) => item.id === props.id);

      createPortal(Modal, {
        defaultVisible: true,
        title: t<string>("common.action.stop"),
        children: task?.messageOnInterruption ?? rsp.message ?? t<string>("common.confirm.stopTask"),
        onOk: async () =>
          await BApi.backgroundTask.stopBackgroundTask(props.id, {
            confirm: true,
          }),
      });
    }
  };

  return (
    <Button {...props} onPress={props.onPress ?? stop}>
      {props.children ?? t<string>("common.action.stop")}
    </Button>
  );
};

BTaskStopButton.displayName = "BTaskStopButton";

export default BTaskStopButton;
