import { useTranslation } from "react-i18next";
import { Accordion, AccordionItem, Chip } from "@/components/bakaui";
import type { ParameterSource } from "./model";

/** Only display the server's allow-listed value, never re-read raw run/config JSON. */
export default function WorkflowParameterSummary({
  sources,
}: {
  sources?: ParameterSource[] | null;
}) {
  const { t } = useTranslation();
  // The app's missing-key handler can return the key instead of defaultValue.
  const label = (key: string, fallback: string) => {
    const translated = t(key, { defaultValue: fallback });
    return translated === key ? fallback : translated;
  };
  if (!sources?.length) return null;
  return (
    <Accordion variant="bordered">
      <AccordionItem key="parameters" title={t("workflow.parameters.snapshotTitle")}>
        <div className="space-y-2 pb-2">
          <p className="text-xs text-default-500">{t("workflow.parameters.snapshotHint")}</p>
          {sources.map((parameter) => (
            <div key={parameter.key} className="space-y-1 text-xs">
              <div className="flex flex-wrap items-center gap-2">
                <span className="font-medium">
                  {label(`workflow.parameters.names.${parameter.key}`, parameter.key)}
                </span>
                <Chip size="sm" variant="flat">
                  {t(`workflow.parameters.source.${parameter.source}`, { defaultValue: parameter.source })}
                </Chip>
                <span className="text-default-500">
                  {t(`workflow.parameters.type.${parameter.type}`, { defaultValue: parameter.type })}
                </span>
              </div>
              <p className="whitespace-pre-wrap break-all text-default-500">
                {!parameter.hasValue
                  ? t("workflow.parameters.unset")
                  : parameter.sensitive
                    ? t("workflow.parameters.valueHidden")
                    : parameter.displayValue == null
                      ? t("workflow.parameters.valueHidden")
                      : label(`workflow.parameters.values.${parameter.key}.${parameter.displayValue}`, parameter.displayValue)}
              </p>
            </div>
          ))}
        </div>
      </AccordionItem>
    </Accordion>
  );
}
