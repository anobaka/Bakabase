import { useTranslation } from "react-i18next";
import { Modal } from "@/components/bakaui";
import WorkflowBrowser from "./WorkflowBrowser";

export interface WorkflowBrowserModalProps {
  visible: boolean;
  onClose: () => void;
  triggerKind?: string;
  onChanged?: () => void;
}

/** A host for the shared browser, including its real editor, without navigating away. */
const WorkflowBrowserModal = ({
  visible,
  onClose,
  triggerKind,
  onChanged,
}: WorkflowBrowserModalProps) => {
  const { t } = useTranslation();
  return (
    <Modal
      visible={visible}
      title={t("workflow.browser.modalTitle")}
      size="7xl"
      style={{ width: "96vw", maxWidth: "96vw" }}
      classNames={{ body: "min-h-0 overflow-auto", base: "max-h-[94dvh]" }}
      scrollBehavior="inside"
      footer={{ actions: ["cancel"] }}
      onClose={onClose}
    >
      {visible && <WorkflowBrowser embedded triggerKind={triggerKind} onChanged={onChanged} />}
    </Modal>
  );
};
export default WorkflowBrowserModal;
