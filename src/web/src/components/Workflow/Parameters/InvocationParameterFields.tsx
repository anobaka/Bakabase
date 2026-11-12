import { useTranslation } from "react-i18next";
import { Switch } from "@/components/bakaui";
import ParameterValueField from "./ParameterValueField";
import { emptyParameterValue, type WorkflowParameter } from "./model";

export default function InvocationParameterFields({
  parameters,
  values,
  onChange,
  onValidityChange,
}: {
  parameters: WorkflowParameter[];
  values: Record<string, unknown>;
  onChange: (values: Record<string, unknown>) => void;
  onValidityChange: (key: string, valid: boolean) => void;
}) {
  const { t } = useTranslation();
  if (!parameters.length) return null;
  return (
    <section className="space-y-3 border-t border-default-200 pt-3">
      <h3 className="text-sm font-medium">{t("workflow.parameters.invocationTitle")}</h3>
      <p className="text-xs text-default-500">{t("workflow.parameters.invocationHint")}</p>
      {parameters.map((parameter) => {
        const overriding = Object.prototype.hasOwnProperty.call(values, parameter.key);
        return (
          <div className="space-y-2 rounded-lg border border-default-200 p-3" key={parameter.key}>
            <div className="flex flex-wrap items-center justify-between gap-2">
              <span className="text-sm font-medium">
                {parameter.label || parameter.key}
                {parameter.required ? " *" : ""}
              </span>
              <Switch
                size="sm"
                isSelected={overriding}
                onValueChange={(enabled) => {
                  const next = { ...values };
                  if (enabled)
                    next[parameter.key] =
                      parameter.defaultValue ?? emptyParameterValue(parameter.type);
                  else delete next[parameter.key];
                  onChange(next);
                  onValidityChange(parameter.key, true);
                }}
              >
                {t("workflow.parameters.override")}
              </Switch>
            </div>
            {parameter.description && (
              <p className="text-xs text-default-500">{parameter.description}</p>
            )}
            {overriding ? (
              <ParameterValueField
                parameter={parameter}
                value={values[parameter.key]}
                onChange={(value) => onChange({ ...values, [parameter.key]: value })}
                onValidityChange={(valid) => onValidityChange(parameter.key, valid)}
              />
            ) : (
              <p
                className={`text-xs ${parameter.defaultValue == null && parameter.required ? "text-warning" : "text-default-500"}`}
              >
                {t(
                  parameter.defaultValue == null
                    ? parameter.required
                      ? "workflow.parameters.requiredWithoutDefault"
                      : "workflow.parameters.unset"
                    : "workflow.parameters.useDefault",
                )}
              </p>
            )}
          </div>
        );
      })}
    </section>
  );
}
