"use client";

import type { DestroyableProps } from "@/components/bakaui/types";

import { useTranslation } from "react-i18next";

import { Modal } from "@/components/bakaui";

type Props = {
  onOk: () => any;
} & DestroyableProps;
const SynchronizationConfirmModal = ({ onOk, onDestroyed }: Props) => {
  const { t } = useTranslation();

  return (
    <Modal
      defaultVisible
      size={"md"}
      title={t<string>("Synchronize resource data")}
      onDestroyed={onDestroyed}
      onOk={onOk}
    >
      <div className={""}>
        {t<string>(
          "All eligible files or folders under the specified root directory will be saved to the media library.",
        )}
      </div>
      <div className={"text-warning text-sm"}>
        {t<string>(
          "Please note, if you modify the resource path (generally the file name), a new resource associated with the new path will be created after synchronization. You can transfer historical resource data to the new resource or directly delete the historical resource through the resource page.",
        )}
      </div>
    </Modal>
  );
};

SynchronizationConfirmModal.displayName = "SynchronizationConfirmModal";

export default SynchronizationConfirmModal;
