"use client";

import type { ButtonProps } from "@/components/bakaui";

import { useTranslation } from "react-i18next";

import { Button, Modal } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BApi from "@/sdk/BApi";

type Props = {
  id: string;
} & ButtonProps;

export default (props: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const stop = async () => {
    const rsp = await BApi.backgroundTask.stopBackgroundTask(props.id, {
      confirm: false,
    });

    if (rsp.code == 202) {
      createPortal(Modal, {
        defaultVisible: true,
        title: t<string>("Stop Task"),
        children: rsp.message ?? t<string>("Sure to stop the task?"),
        onOk: async () =>
          await BApi.backgroundTask.stopBackgroundTask(props.id, {
            confirm: true,
          }),
      });
    }
  };

  return (
    <Button {...props} onPress={props.onPress ?? stop}>
      {props.children ?? t<string>("Stop")}
    </Button>
  );
};
