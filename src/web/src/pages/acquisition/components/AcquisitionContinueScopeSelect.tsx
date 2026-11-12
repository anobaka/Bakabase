import { useTranslation } from "react-i18next";
import { Select } from "@/components/bakaui";

export const acquisitionContinuationScopes = ["localQueue", "workflow", "downloads", "localFiles"] as const;
export type AcquisitionContinuationScope = typeof acquisitionContinuationScopes[number];
export type AcquisitionContinuationNavigation = {
  continuationScope?: AcquisitionContinuationScope;
  onContinuationScopeChange?: (scope: AcquisitionContinuationScope) => void;
};

export default function AcquisitionContinueScopeSelect({ value, onChange, disabled = false }: {
  value: AcquisitionContinuationScope;
  onChange: (scope: AcquisitionContinuationScope) => void;
  disabled?: boolean;
}) {
  const { t } = useTranslation();
  return <div className="space-y-1">
    <Select label={t("acquisition.continueMode.label")} aria-label={t("acquisition.continueMode.label")}
      className="max-w-xl" size="sm" disallowEmptySelection isDisabled={disabled} selectedKeys={[value]}
      dataSource={acquisitionContinuationScopes.map(scope => ({ value: scope,
        label: t(`acquisition.continueMode.${scope}.label`),
        description: t(`acquisition.continueMode.${scope}.description`),
      }))} onSelectionChange={keys => {
        const selected = Array.from(keys)[0];
        if (!disabled && acquisitionContinuationScopes.includes(selected as AcquisitionContinuationScope))
          onChange(selected as AcquisitionContinuationScope);
      }} />
    <p className="text-xs text-default-500">{t(`acquisition.continueMode.${value}.description`)}</p>
  </div>;
}
