import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Button } from "@/components/bakaui";
import DeliverySettingsFields, {
  defaultDeliverySettings,
  deliverySettingsAreInvalid,
} from "../DeliverySettingsFields";
import type { PlanTaskDetail } from "./models";
import type { BakabaseModulesAcquisitionAbstractionsModelsDomainAcquisitionDeliverySettings as DeliverySettings } from "@/sdk/Api";

export default function TaskDeliverySettings({
  detail,
  editable = false,
  busy = false,
  onSave,
}: {
  detail: PlanTaskDetail;
  editable?: boolean;
  busy?: boolean;
  onSave?: (value: DeliverySettings) => Promise<void>;
}) {
  const { t } = useTranslation();
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState<DeliverySettings>(defaultDeliverySettings);
  const layout = detail.plan?.deliveryLayout;
  const paths = layout?.taskDirectory
    ? [
        ...new Set(
          Object.values(layout.partDirectories ?? {}).length
            ? Object.values(layout.partDirectories ?? {})
            : [layout.taskDirectory],
        ),
      ]
    : [];
  return (
    <section className="space-y-2 rounded-lg border border-default-200 p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-sm font-medium">{t("acquisition.delivery.frozen")}</h3>
        {onSave && editable && !editing && (
          <Button
            size="sm"
            variant="flat"
            isDisabled={busy}
            onPress={() => {
              setDraft(detail.deliverySettings ?? defaultDeliverySettings);
              setEditing(true);
            }}
          >
            {t("acquisition.delivery.edit")}
          </Button>
        )}
      </div>
      {editing ? (
        <>
          <DeliverySettingsFields
            value={draft}
            onChange={setDraft}
            disabled={busy}
            downloadRoot={detail.task.targetDirectory}
            taskId={detail.task.id}
            title={detail.task.title}
            showHint={false}
          />
          <div className="flex flex-wrap gap-2">
            <Button
              size="sm"
              color="primary"
              isLoading={busy}
              isDisabled={busy || deliverySettingsAreInvalid(draft)}
              onPress={async () => {
                await onSave?.(draft);
              }}
            >
              {t("acquisition.delivery.save")}
            </Button>
            <Button size="sm" variant="light" isDisabled={busy} onPress={() => setEditing(false)}>
              {t("acquisition.delivery.cancelEdit")}
            </Button>
          </div>
        </>
      ) : paths.length ? (
        <>
          {paths.map((path) => (
            <code className="block break-all text-xs" key={path}>
              {path}
            </code>
          ))}
          <p className="text-xs text-default-500">{t("acquisition.delivery.notCreated")}</p>
        </>
      ) : (
        <p className="text-xs text-default-500">
          {t(
            detail.deliverySettings
              ? "acquisition.delivery.rootHint"
              : "acquisition.delivery.legacy",
          )}
        </p>
      )}
    </section>
  );
}
