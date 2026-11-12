import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Modal } from "@/components/bakaui";
import type { DestroyableProps } from "@/components/bakaui/types";
import SourcePriorityConfiguration from "./SourcePriorityConfiguration";

export default function SourcePriorityModal({ onDestroyed }: DestroyableProps) {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(true);
  return (
    <Modal
      visible={visible}
      onClose={() => setVisible(false)}
      onDestroyed={onDestroyed}
      size="2xl"
      scrollBehavior="inside"
      title={t("sourceAvailability.priority.title")}
      footer={
        <Button variant="light" onPress={() => setVisible(false)}>
          {t("common.action.close")}
        </Button>
      }
    >
      <SourcePriorityConfiguration />
    </Modal>
  );
}
