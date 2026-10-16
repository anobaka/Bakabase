import type { ResourceProfile, ProfileSource } from "../sourceProfiles";
import type { BakabaseAbstractionsModelsDomainResourceProcessingExecutionTarget } from "@/sdk/Api";

import { useTranslation } from "react-i18next";

import { Select } from "@/components/bakaui";
import { ResourceProcessingExecutionKind } from "@/sdk/constants";

type Target = BakabaseAbstractionsModelsDomainResourceProcessingExecutionTarget;
export const defaultExecutionTarget = (profile: ResourceProfile): Target | undefined =>
  profile.sourceDeviceId ? { kind: ResourceProcessingExecutionKind.Local } : undefined;

export default function RuleExecutionTargetSelect({
  value,
  sources,
  disabled,
  onChange,
}: {
  value?: Target | null;
  sources: ProfileSource[];
  disabled: boolean;
  onChange: (target: Target) => void;
}) {
  const { t } = useTranslation();
  const key =
    value?.kind === ResourceProcessingExecutionKind.Managed
      ? JSON.stringify([value.deviceId, value.libraryEpoch])
      : String(value?.kind ?? ResourceProcessingExecutionKind.Owner);
  const devices = sources.filter((source) => source.kind === "managed" && source.libraryEpoch);
  const options = [
    {
      value: String(ResourceProcessingExecutionKind.Owner),
      label: t("resourceProfile.execution.owner"),
    },
    {
      value: String(ResourceProcessingExecutionKind.Local),
      label: t("resourceProfile.execution.local"),
    },
    ...devices.map((source) => ({
      value: JSON.stringify([source.deviceId, source.libraryEpoch]),
      label: source.name,
    })),
  ];

  // Keep an unavailable saved worker visible rather than silently switching its target.
  if (!options.some((option) => option.value === key))
    options.push({
      value: key,
      label: t("resourceProfile.execution.unavailable", { name: value?.deviceId }),
    });

  return (
    <div className="mt-3 space-y-1">
      <Select
        disallowEmptySelection
        dataSource={options}
        isDisabled={disabled}
        label={t<string>("resourceProfile.execution.label")}
        selectedKeys={[key]}
        size="sm"
        onSelectionChange={(keys) => {
          const next = String([...keys][0]);
          const device = devices.find(
            (source) => JSON.stringify([source.deviceId, source.libraryEpoch]) === next,
          );

          if (device)
            onChange({
              kind: ResourceProcessingExecutionKind.Managed,
              deviceId: device.deviceId,
              libraryEpoch: device.libraryEpoch ?? undefined,
            });
          else if (
            next === String(ResourceProcessingExecutionKind.Owner) ||
            next === String(ResourceProcessingExecutionKind.Local)
          )
            onChange({
              kind:
                next === String(ResourceProcessingExecutionKind.Owner)
                  ? ResourceProcessingExecutionKind.Owner
                  : ResourceProcessingExecutionKind.Local,
            });
        }}
      />
      <p className="text-xs text-default-400">{t("resourceProfile.execution.hint")}</p>
    </div>
  );
}
