import type { DestroyableProps } from "@/components/bakaui/types";

import { useCallback } from "react";
import { useTranslation } from "react-i18next";

import { Modal } from "@/components/bakaui";

interface Props extends DestroyableProps {
  title: string;
  message: string;
  /** Action button label. Defaults to common.action.confirm. */
  confirmLabel?: string;
  /** When true, action button is rendered as danger. Default false. */
  destructive?: boolean;
  onConfirm: () => void | Promise<void>;
}

/**
 * Local confirm dialog for the health-score page. We intentionally avoid
 * <c>window.confirm</c> because the Electron host suppresses it, which is
 * why "delete profile" appeared to do nothing.
 */
const ConfirmModal = ({
  title,
  message,
  confirmLabel,
  destructive = false,
  onConfirm,
  onDestroyed,
}: Props) => {
  const { t } = useTranslation();

  const handleOk = useCallback(async () => {
    await onConfirm();
    onDestroyed?.();
  }, [onConfirm, onDestroyed]);

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["cancel", "ok"],
        okProps: {
          children: confirmLabel ?? t<string>("common.action.confirm"),
          color: destructive ? "danger" : "primary",
        },
      }}
      title={title}
      onDestroyed={onDestroyed}
      onOk={handleOk}
    >
      <div className="text-sm whitespace-pre-line">{message}</div>
    </Modal>
  );
};

ConfirmModal.displayName = "ConfirmModal";

export default ConfirmModal;
