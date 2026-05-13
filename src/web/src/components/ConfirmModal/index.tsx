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
 * Shared confirm dialog. Use instead of <c>window.confirm</c> — the Electron
 * host suppresses native browser confirm/alert dialogs, so anything gated on
 * <c>if (!confirm(...)) return;</c> silently no-ops.
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
