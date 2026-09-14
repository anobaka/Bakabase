import type { DestroyableProps } from "@/components/bakaui/types";
import type { components } from "@/sdk/BApi2";

import { useState } from "react";
import { useTranslation } from "react-i18next";

import { workflowLabel } from "./builtinLabels";
import { PresetUsage, workflowPresetGuide } from "./presetGuides";

import { Button, Card, CardBody, Modal } from "@/components/bakaui";

type Workflow =
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowDefinitionViewModel"];

interface Props extends DestroyableProps {
  workflows: Workflow[];
  onChoose: (path: string) => void;
}

const TemplateLibrary = ({ workflows, onChoose, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(true);
  const entries = [
    ...["fileCleaning", "externalDownload"].map((id) => ({
      id,
      title: t(`workflow.template.${id}.name`),
      guide: id,
      path: `/workflows/editor?template=${id}`,
    })),
    ...workflows
      .filter((wf) => wf.isBuiltin && workflowPresetGuide(wf) !== "externalDownload")
      .map((wf) => ({
        id: String(wf.id),
        title: workflowLabel(wf, t),
        guide: workflowPresetGuide(wf),
        path: `/workflows/editor?id=${wf.id}`,
      })),
  ];

  return (
    <Modal
      footer={{ actions: ["cancel"] }}
      size="4xl"
      title={t("workflow.templates.title")}
      visible={visible}
      onClose={() => setVisible(false)}
      onDestroyed={onDestroyed}
    >
      <p className="mb-4 text-sm leading-relaxed text-default-500">
        {t("workflow.templates.description")}
      </p>
      <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
        {entries.map((entry) => (
          <Card key={entry.id} className="bg-default-50" shadow="none">
            <CardBody className="flex gap-3 p-4">
              <h3 className="text-sm font-semibold">{entry.title}</h3>
              {entry.guide && <PresetUsage guide={entry.guide} />}
              <Button
                className="mt-auto self-start"
                color="primary"
                size="sm"
                variant="flat"
                onPress={() => {
                  setVisible(false);
                  onChoose(entry.path);
                }}
              >
                {t("workflow.templates.configure")}
              </Button>
            </CardBody>
          </Card>
        ))}
      </div>
    </Modal>
  );
};

export default TemplateLibrary;
