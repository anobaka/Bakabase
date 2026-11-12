import { useState } from "react";
import { useTranslation } from "react-i18next";
import { FolderOpenOutlined } from "@ant-design/icons";
import { Button, Checkbox, Modal } from "@/components/bakaui";
import type { PlanTaskDetail } from "./models";
import RetryDeliveryRootField from "./RetryDeliveryRootField";
import RevisionDeliveryRoots from "./RevisionDeliveryRoots";

type Placement = { useCurrent: boolean; override?: string };

export default function RevisionPlacementDialog({ detail, currentRoot, useCurrent, override, forceCurrent = false,
  showRoots, disabled, onApply }: {
  detail: PlanTaskDetail; currentRoot?: string | null; useCurrent: boolean; override?: string;
  forceCurrent?: boolean; showRoots: boolean; disabled: boolean; onApply: (draft: Placement) => void;
}) {
  const { t } = useTranslation();
  const [draft, setDraft] = useState<Placement>();
  return (
    <>
      <Button size="sm" variant="light" startContent={<FolderOpenOutlined aria-hidden />}
        aria-haspopup="dialog" isDisabled={disabled} onPress={() => setDraft({ useCurrent, override })}>
        {t("acquisition.placement.open")}
      </Button>
      {draft && <Modal visible size="2xl" scrollBehavior="inside" title={t("acquisition.placement.open")}
        onClose={() => setDraft(undefined)}
        footer={<div className="flex justify-end gap-2">
          <Button variant="light" onPress={() => setDraft(undefined)}>{t("common.action.cancel")}</Button>
          <Button color="primary" isDisabled={disabled || draft.override === ""}
            onPress={() => { onApply(draft); setDraft(undefined); }}>{t("common.action.apply")}</Button>
        </div>}>
        <Checkbox isSelected={forceCurrent || draft.useCurrent} isDisabled={disabled || forceCurrent}
          onValueChange={use => setDraft({ ...draft, useCurrent: use })}>
          <span className="block text-sm">{t("acquisition.plan.continueCompleted.useCurrentStorage")}</span>
          <span className="block text-xs text-default-500">{t("acquisition.plan.continueCompleted.useCurrentStorageHint")}</span>
        </Checkbox>
        <RetryDeliveryRootField value={draft.override} startPath={draft.override || (forceCurrent || draft.useCurrent ? currentRoot : detail.deliverySettings?.rootDirectory)}
          disabled={disabled} onChange={value => setDraft({ ...draft, override: value })} />
        {showRoots && <RevisionDeliveryRoots detail={detail} currentRoot={currentRoot}
          useCurrent={forceCurrent || draft.useCurrent} override={draft.override} />}
      </Modal>}
    </>
  );
}
