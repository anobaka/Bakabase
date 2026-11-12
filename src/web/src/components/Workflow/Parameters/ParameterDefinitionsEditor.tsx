import { useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineDelete, AiOutlinePlus } from "react-icons/ai";
import { Accordion, AccordionItem, Button, Input, Select, Switch } from "@/components/bakaui";
import ParameterValueField from "./ParameterValueField";
import { emptyParameterValue, parameterTypes, type WorkflowParameter } from "./model";

export default function ParameterDefinitionsEditor({
  parameters,
  onChange,
  onValidityChange,
}: {
  parameters: WorkflowParameter[];
  onChange: (parameters: WorkflowParameter[]) => void;
  onValidityChange: (valid: boolean) => void;
}) {
  const { t } = useTranslation();
  const [invalid, setInvalid] = useState<Set<number>>(new Set());
  const update = (index: number, changes: Partial<WorkflowParameter>) =>
    onChange(
      parameters.map((parameter, i) => (i === index ? { ...parameter, ...changes } : parameter)),
    );
  const report = (index: number, valid: boolean) => {
    setInvalid((previous) => {
      if (previous.has(index) === !valid) return previous;
      const next = new Set(previous);
      if (valid) next.delete(index);
      else next.add(index);
      onValidityChange(next.size === 0);
      return next;
    });
  };
  return (
    <Accordion variant="light" className="mb-2 px-1">
      <AccordionItem
        key="parameters"
        title={t("workflow.parameters.title", { count: parameters.length })}
      >
        <div className="space-y-3 pb-2">
          <p className="text-xs text-default-500">{t("workflow.parameters.contractHint")}</p>
          {parameters.map((parameter, index) => (
            <div key={index} className="space-y-2 rounded-lg border border-default-200 p-3">
              <div className="grid gap-2 md:grid-cols-[1fr_1fr_10rem_auto]">
                <Input
                  size="sm"
                  label={t("workflow.parameters.key")}
                  value={parameter.key}
                  isInvalid={
                    !/^[A-Za-z][A-Za-z0-9_.-]*$/.test(parameter.key) ||
                    parameters.filter((item) => item.key === parameter.key).length > 1
                  }
                  onValueChange={(key) => update(index, { key })}
                />
                <Input
                  size="sm"
                  label={t("workflow.parameters.label")}
                  value={parameter.label ?? ""}
                  onValueChange={(label) => update(index, { label })}
                />
                <Select
                  size="sm"
                  label={t("workflow.parameters.type")}
                  disallowEmptySelection
                  selectedKeys={[parameter.type]}
                  dataSource={parameterTypes.map((type) => ({
                    value: type,
                    label: t(`workflow.parameters.type.${type}`),
                  }))}
                  onSelectionChange={(keys) => {
                    const type = String(Array.from(keys)[0]);
                    update(index, {
                      type,
                      format: undefined,
                      allowedValues: undefined,
                      defaultValue:
                        parameter.defaultValue == null ? undefined : emptyParameterValue(type),
                    });
                    report(index, true);
                  }}
                />
                <Button
                  size="sm"
                  isIconOnly
                  color="danger"
                  variant="light"
                  aria-label={t("workflow.parameters.remove", { key: parameter.key })}
                  onPress={() => {
                    onChange(parameters.filter((_, i) => i !== index));
                    setInvalid(new Set());
                    onValidityChange(true);
                  }}
                >
                  <AiOutlineDelete />
                </Button>
              </div>
              <Input
                size="sm"
                label={t("workflow.parameters.description")}
                value={parameter.description ?? ""}
                onValueChange={(description) => update(index, { description })}
              />
              <div className="flex flex-wrap items-center gap-x-5 gap-y-2">
                <Switch
                  size="sm"
                  isSelected={parameter.required}
                  onValueChange={(required) => update(index, { required })}
                >
                  {t("workflow.parameters.required")}
                </Switch>
                <Switch
                  size="sm"
                  isSelected={parameter.sensitive}
                  onValueChange={(sensitive) =>
                    update(index, {
                      sensitive,
                      displayInRun: sensitive ? false : parameter.displayInRun,
                    })
                  }
                >
                  {t("workflow.parameters.sensitive")}
                </Switch>
                <Switch
                  size="sm"
                  isDisabled={parameter.sensitive || ["object", "array"].includes(parameter.type)}
                  isSelected={parameter.displayInRun}
                  onValueChange={(displayInRun) => update(index, { displayInRun })}
                >
                  {t("workflow.parameters.displayInRun")}
                </Switch>
                {parameter.type === "string" && (
                  <Switch
                    size="sm"
                    isSelected={parameter.format === "directory"}
                    onValueChange={(directory) =>
                      update(index, { format: directory ? "directory" : undefined })
                    }
                  >
                    {t("workflow.parameters.directory")}
                  </Switch>
                )}
                <Switch
                  size="sm"
                  isSelected={parameter.defaultValue != null}
                  onValueChange={(enabled) => {
                    update(index, {
                      defaultValue: enabled ? emptyParameterValue(parameter.type) : undefined,
                    });
                    report(index, true);
                  }}
                >
                  {t("workflow.parameters.hasDefault")}
                </Switch>
              </div>
              {parameter.defaultValue != null && (
                <ParameterValueField
                  parameter={{ ...parameter, required: true }}
                  value={parameter.defaultValue}
                  label={t("workflow.parameters.defaultValue")}
                  onChange={(defaultValue) => update(index, { defaultValue })}
                  onValidityChange={(valid) => report(index, valid)}
                />
              )}
            </div>
          ))}
          <Button
            size="sm"
            variant="flat"
            startContent={<AiOutlinePlus />}
            onPress={() => {
              let key = `parameter${parameters.length + 1}`;
              while (parameters.some((parameter) => parameter.key === key)) key += "_";
              onChange([
                ...parameters,
                { key, type: "string", required: false, sensitive: false, displayInRun: false },
              ]);
            }}
          >
            {t("workflow.parameters.add")}
          </Button>
          {!!invalid.size && (
            <p className="text-xs text-danger">{t("workflow.parameters.invalid")}</p>
          )}
        </div>
      </AccordionItem>
    </Accordion>
  );
}
