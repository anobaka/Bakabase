import { useTranslation } from "react-i18next";
import { acquisitionActionLimits } from "../acquisitionSelection";
import { Tab, Tabs } from "@/components/bakaui";

type Method = "automatic" | "adaptive";

export default function AcquisitionContinuationMethodTabs({ value, onSwitch, disabled, adaptiveDisabled }: {
  value: Method;
  onSwitch?: () => void;
  disabled?: boolean;
  adaptiveDisabled?: boolean;
}) {
  const { t } = useTranslation();
  return (
    <>
    <Tabs
      size="sm"
      aria-label={t("acquisition.continue.method")}
      selectedKey={value}
      isDisabled={disabled}
      onSelectionChange={key => { if (key !== value) onSwitch?.(); }}
      classNames={{ base: "max-w-full", tabList: "max-w-full" }}
    >
      <Tab key="automatic" title={t("acquisition.continueActions.byPlan")} />
      <Tab key="adaptive" isDisabled={adaptiveDisabled} title={t("acquisition.continueActions.byAi")} />
    </Tabs>
    {adaptiveDisabled && <p className="text-xs text-default-500">
      {t("acquisition.continueActions.aiLimit", { count: acquisitionActionLimits.adaptive })}
    </p>}
    </>
  );
}
