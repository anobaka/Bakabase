import { useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineBranches } from "react-icons/ai";
import { useNavigate } from "react-router-dom";

import TriggerUsageSummary from "./TriggerUsageSummary";
import {
  workflowIntegrationSurfaces,
  type WorkflowIntegrationSurface,
} from "./integrationSurfaces";

import { Button, Modal } from "@/components/bakaui";
import { HelpCenterButton } from "@/components/HelpCenter";

/** A shared, discoverable entry; trigger details are loaded only when opened. */
export default function WorkflowIntegrationHint({
  surface,
}: {
  surface: WorkflowIntegrationSurface;
}) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [open, setOpen] = useState(false);

  return (
    <>
      <Button
        size="sm"
        startContent={<AiOutlineBranches aria-hidden className="text-base" />}
        variant="light"
        onPress={() => setOpen(true)}
      >
        {t("workflow.integration.open")}
      </Button>
      {open && (
        <Modal
          footer={false}
          size="2xl"
          title={t("workflow.integration.title")}
          visible
          onClose={() => setOpen(false)}
        >
          <div className="flex flex-col gap-4">
            <p className="text-sm leading-relaxed text-default-500">
              {t("workflow.integration.description")}
            </p>
            {workflowIntegrationSurfaces[surface].triggerKinds.map((triggerKind) => (
              <section key={triggerKind} className="rounded-xl bg-default-50 p-3">
                <TriggerUsageSummary showActions={false} triggerKind={triggerKind} />
                <Button
                  className="mt-2"
                  size="sm"
                  variant="flat"
                  onPress={() => {
                    setOpen(false);
                    navigate(`/workflows?triggerKind=${encodeURIComponent(triggerKind)}`);
                  }}
                >
                  {t("workflow.integration.related")}
                </Button>
              </section>
            ))}
            <HelpCenterButton
              label={t("workflow.integration.catalog")}
              section="triggers"
              topic="workflow"
            />
          </div>
        </Modal>
      )}
    </>
  );
}
