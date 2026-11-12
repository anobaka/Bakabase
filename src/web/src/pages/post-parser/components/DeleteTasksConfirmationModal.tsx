import type { DestroyableProps } from "@/components/bakaui/types";
import type { TaskStatusGroup } from "../taskStatus";

import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineDelete } from "react-icons/ai";

import { Button, Modal } from "@/components/bakaui";
import TaskFilterScopeSummary from "./TaskFilterScopeSummary";

interface Props extends DestroyableProps {
  count: number;
  keyword: string;
  status: TaskStatusGroup | "all";
  reasonCode?: string;
  onConfirm: () => Promise<void>;
  onClose?: () => void;
}

/** Confirms the already prepared selection; it never recalculates or expands its scope. */
export default function DeleteTasksConfirmationModal({
  count,
  keyword,
  status,
  reasonCode,
  onConfirm,
  onClose,
  onDestroyed,
}: Props) {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string>();
  const submittingRef = useRef(false);
  const cancelButton = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    // HeroUI first focuses the dialog itself. Move to the safe action once its
    // focus scope has mounted, so Enter never defaults to destructive approval.
    const frame = window.requestAnimationFrame(() => cancelButton.current?.focus());
    return () => window.cancelAnimationFrame(frame);
  }, []);
  const valid = Number.isSafeInteger(count) && count > 0;
  const filtered = !!keyword.trim() || status !== "all" || !!reasonCode;
  const close = () => {
    if (submittingRef.current) return;
    setVisible(false);
    onClose?.();
  };
  const confirm = async () => {
    if (!valid || submittingRef.current) return;
    submittingRef.current = true;
    setSubmitting(true);
    setError(undefined);
    try {
      await onConfirm();
      submittingRef.current = false;
      close();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : t("postParser.result.failed"));
    } finally {
      submittingRef.current = false;
      setSubmitting(false);
    }
  };

  return (
    <Modal
      visible={visible}
      title={t(
        filtered ? "postParser.deleteBatch.filteredTitle" : "postParser.deleteBatch.allTitle",
        { count },
      )}
      size="lg"
      hideCloseButton={submitting}
      isDismissable={!submitting}
      isKeyboardDismissDisabled={submitting}
      onClose={close}
      onDestroyed={onDestroyed}
      footer={
        <>
          <Button
            ref={cancelButton}
            autoFocus
            color="default"
            variant="flat"
            isDisabled={submitting}
            onPress={close}
          >
            {t("postParser.deleteBatch.cancel")}
          </Button>
          <Button
            color="danger"
            variant="solid"
            isDisabled={!valid || submitting}
            isLoading={submitting}
            startContent={<AiOutlineDelete aria-hidden />}
            onPress={() => void confirm()}
          >
            {t("postParser.deleteBatch.confirm", { count })}
          </Button>
        </>
      }
    >
      <div className="space-y-4 text-sm leading-relaxed">
        <TaskFilterScopeSummary keyword={keyword} status={status} reasonCode={reasonCode} />
        <p className="font-medium text-danger">{t("postParser.deleteBatch.scope", { count })}</p>
        <p>{t("postParser.deleteBatch.snapshot")}</p>
        <p>{t("postParser.deleteBatch.preserve")}</p>
        {error && (
          <p role="alert" className="break-words text-danger">
            {error}
          </p>
        )}
      </div>
    </Modal>
  );
}
