import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Accordion, AccordionItem, Button, Select } from "@/components/bakaui";
import {
  configFields,
  pathsOverlap,
  valueMatchesType,
  type ParameterBinding,
  type WorkflowParameter,
} from "./model";

export default function ParameterBindingsEditor({
  configJson,
  parameters,
  bindings,
  onChange,
}: {
  configJson: string;
  parameters: WorkflowParameter[];
  bindings: ParameterBinding[];
  onChange: (bindings: ParameterBinding[]) => void;
}) {
  const { t } = useTranslation();
  const [nextPath, setNextPath] = useState("");
  const fields = configFields(configJson);
  const compatible = (path: string) => {
    const field = fields.find((candidate) => candidate.path === path);
    return parameters.filter(
      (parameter) =>
        field && (field.value == null || valueMatchesType(field.value, parameter.type)),
    );
  };
  const available = fields.filter(
    (field) =>
      !bindings.some((binding) => pathsOverlap(field.path, binding.configPath)) &&
      compatible(field.path).length > 0,
  );
  const title = (path: string) =>
    path
      .split("/")
      .slice(1)
      .map((segment) => {
        const name = segment.replace(/~1/g, "/").replace(/~0/g, "~");
        return t(`workflow.parameters.fields.${name}`, { defaultValue: name });
      })
      .join(" / ");

  return (
    <Accordion variant="light" className="px-0">
      <AccordionItem
        key="bindings"
        title={t("workflow.parameters.bindings", { count: bindings.length })}
      >
        <div className="space-y-3 pb-2">
          <p className="text-xs text-default-500">{t("workflow.parameters.bindingHint")}</p>
          {!parameters.length && (
            <p className="text-xs text-default-500">{t("workflow.parameters.noParameters")}</p>
          )}
          {bindings.map((binding, index) => {
            const choices = compatible(binding.configPath);
            const exists = choices.some((parameter) => parameter.key === binding.parameterKey);
            return (
              <Select
                key={`${binding.configPath}:${index}`}
                size="sm"
                label={title(binding.configPath)}
                isInvalid={!exists}
                errorMessage={t("workflow.parameters.bindingInvalid")}
                selectedKeys={[binding.parameterKey]}
                disallowEmptySelection
                dataSource={[
                  { value: "__fixed__", label: t("workflow.parameters.fixedValue") },
                  ...choices.map((parameter) => ({
                    value: parameter.key,
                    label: parameter.label
                      ? `${parameter.label} (${parameter.key})`
                      : parameter.key,
                  })),
                  ...(!exists
                    ? [
                        {
                          value: binding.parameterKey,
                          label: binding.parameterKey,
                          isDisabled: true,
                        },
                      ]
                    : []),
                ]}
                onSelectionChange={(keys) => {
                  const key = String(Array.from(keys)[0]);
                  onChange(
                    key === "__fixed__"
                      ? bindings.filter((_, i) => i !== index)
                      : bindings.map((item, i) =>
                          i === index ? { ...item, parameterKey: key } : item,
                        ),
                  );
                }}
              />
            );
          })}
          {available.length > 0 && (
            <div className="space-y-2">
              <Select
                size="sm"
                label={t("workflow.parameters.bindField")}
                selectedKeys={available.some((field) => field.path === nextPath) ? [nextPath] : []}
                dataSource={available.map((field) => ({
                  value: field.path,
                  label: title(field.path),
                }))}
                onSelectionChange={(keys) => setNextPath(String(Array.from(keys)[0] ?? ""))}
              />
              <Button
                size="sm"
                variant="flat"
                isDisabled={!available.some((field) => field.path === nextPath)}
                onPress={() => {
                  const parameter = compatible(nextPath)[0];
                  if (parameter)
                    onChange([...bindings, { configPath: nextPath, parameterKey: parameter.key }]);
                  setNextPath("");
                }}
              >
                {t("workflow.parameters.bind")}
              </Button>
            </div>
          )}
        </div>
      </AccordionItem>
    </Accordion>
  );
}
