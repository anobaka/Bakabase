import { useState } from "react";
import { useTranslation } from "react-i18next";

import { Button, Card, CardBody, Select, Spinner } from "@/components/bakaui";
import TriggerUsageSummary from "@/components/Workflow/TriggerUsageSummary";
import {
  getTriggerActivationMode,
  triggerActivationModes,
  useWorkflowTriggerDescriptors,
} from "@/components/Workflow/triggerPresentation";

const TriggersSection = ({ onNavigate }: { onNavigate?: (path: string) => void }) => {
  const { t } = useTranslation();
  const query = useWorkflowTriggerDescriptors();
  const [mode, setMode] = useState("all");
  const triggers = (query.data ?? []).filter(
    (trigger) => mode === "all" || getTriggerActivationMode(trigger) === mode,
  );

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm leading-relaxed text-default-500">
        {t("workflowTriggers.catalog.description")}
      </p>
      <div className="flex flex-wrap items-center gap-2">
        <Select
          className="w-56"
          dataSource={["all", ...triggerActivationModes].map((value) => ({
            value,
            label: t(`workflowTriggers.mode.${value}`),
          }))}
          label={t("workflowTriggers.catalog.filter")}
          selectedKeys={[mode]}
          size="sm"
          onSelectionChange={(keys) => setMode(String(Array.from(keys)[0] ?? "all"))}
        />
        <Button
          isLoading={query.isFetching}
          size="sm"
          variant="light"
          onPress={() => void query.refetch()}
        >
          {t("workflowTriggers.catalog.refresh")}
        </Button>
        {query.data && (
          <span className="text-xs text-default-500">
            {t("workflowTriggers.catalog.count", { count: triggers.length })}
          </span>
        )}
      </div>
      {query.isPending && (
        <div className="flex items-center gap-2 text-sm text-default-500">
          <Spinner size="sm" />
          {t("workflowTriggers.catalog.loading")}
        </div>
      )}
      {query.isError && (
        <p className="text-sm text-danger" role="alert">
          {t("workflowTriggers.catalog.error")}
        </p>
      )}
      {query.data && triggers.length === 0 && (
        <p className="py-6 text-center text-sm text-default-500">
          {t("workflowTriggers.catalog.empty")}
        </p>
      )}
      <div className="grid grid-cols-1 gap-3 xl:grid-cols-2">
        {triggers.map((trigger) => (
          <Card key={trigger.kind} className="border border-default-200 bg-content1" shadow="none">
            <CardBody className="p-4">
              <TriggerUsageSummary showHelp={false} trigger={trigger} onNavigate={onNavigate} />
              <details className="mt-3 text-xs text-default-500">
                <summary className="cursor-pointer">
                  {t("workflowTriggers.catalog.payloadFields")}
                </summary>
                <code className="mt-2 block break-all">{trigger.kind}</code>
                {trigger.payloadFields?.length > 0 ? (
                  <ul className="mt-2 flex flex-col gap-1">
                    {trigger.payloadFields.map((field) => (
                      <li key={field.name}>
                        <code>{field.name}</code> · <code>{field.type}</code>
                        {field.nullable ? ` · ${t("workflowTriggers.catalog.nullable")}` : ""}
                      </li>
                    ))}
                  </ul>
                ) : (
                  <p className="mt-2">{t("workflowTriggers.catalog.noFields")}</p>
                )}
              </details>
            </CardBody>
          </Card>
        ))}
      </div>
    </div>
  );
};

export default TriggersSection;
