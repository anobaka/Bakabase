import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Input, Select, Switch, Textarea } from "@/components/bakaui";
import FileSystemSelectorButton from "@/components/FileSystemSelector/components/Button";
import { parameterValueIsValid, type WorkflowParameter } from "./model";

export default function ParameterValueField({
  parameter,
  value,
  onChange,
  onValidityChange,
  label,
  disabled = false,
}: {
  parameter: WorkflowParameter;
  value: unknown;
  onChange: (value: unknown) => void;
  onValidityChange?: (valid: boolean) => void;
  label?: string;
  disabled?: boolean;
}) {
  const { t } = useTranslation();
  const structured = ["object", "array"].includes(parameter.type);
  const serialized = structured
    ? JSON.stringify(value ?? (parameter.type === "array" ? [] : {}), null, 2)
    : String(value ?? "");
  const [raw, setRaw] = useState(serialized);
  const [invalid, setInvalid] = useState(false);
  useEffect(() => {
    setRaw(serialized);
    setInvalid(false);
  }, [serialized, parameter.type]);
  useEffect(() => {
    onValidityChange?.(!invalid && parameterValueIsValid(parameter, value));
  }, [invalid, parameter, value, onValidityChange]);
  const fieldLabel = label ?? parameter.label ?? parameter.key;

  if (parameter.allowedValues?.length)
    return (
      <Select
        label={fieldLabel}
        size="sm"
        isDisabled={disabled}
        selectedKeys={
          value == null
            ? []
            : [
                String(
                  parameter.allowedValues.findIndex(
                    (allowed) => JSON.stringify(allowed) === JSON.stringify(value),
                  ),
                ),
              ]
        }
        disallowEmptySelection
        dataSource={parameter.allowedValues.map((allowed, index) => ({
          value: String(index),
          label: String(allowed),
        }))}
        onSelectionChange={(keys) => {
          const index = Number(Array.from(keys)[0]);
          if (Number.isInteger(index)) onChange(parameter.allowedValues![index]);
        }}
      />
    );
  if (parameter.type === "boolean")
    return (
      <Switch size="sm" isDisabled={disabled} isSelected={value === true} onValueChange={onChange}>
        {fieldLabel}
      </Switch>
    );
  if (parameter.type === "string" && parameter.format === "directory")
    return (
      <div className="space-y-1">
        <p className="text-xs text-default-500">{fieldLabel}</p>
        <div className="flex items-center gap-1">
          <FileSystemSelectorButton
            key={String(value ?? "")}
            className="h-auto min-h-8 max-w-full whitespace-normal break-all text-left"
            isDisabled={disabled}
            aria-label={fieldLabel}
            fileSystemSelectorProps={{
              targetType: "folder",
              multiple: false,
              startPath: String(value || "") || undefined,
              defaultSelectedPath: String(value || "") || undefined,
              onSelected: (entry) => entry.path && onChange(entry.path),
            }}
          >
            {t("workflow.parameters.chooseDirectory")}
          </FileSystemSelectorButton>
          {!!value && (
            <Button size="sm" variant="light" isDisabled={disabled} onPress={() => onChange("")}>
              {t("workflow.parameters.clear")}
            </Button>
          )}
        </div>
      </div>
    );
  const change = (text: string) => {
    setRaw(text);
    try {
      const next = structured
        ? JSON.parse(text)
        : ["integer", "number"].includes(parameter.type)
          ? text.trim()
            ? Number(text)
            : undefined
          : text;
      const valid = parameterValueIsValid({ ...parameter, required: true }, next);
      setInvalid(!valid);
      if (valid) onChange(next);
    } catch {
      setInvalid(true);
    }
  };
  const shared = {
    label: fieldLabel,
    size: "sm" as const,
    isDisabled: disabled,
    isInvalid: invalid,
    errorMessage: t("workflow.parameters.valueInvalid", {
      type: t(`workflow.parameters.type.${parameter.type}`),
    }),
    value: raw,
    onValueChange: change,
  };
  return structured ? (
    <Textarea {...shared} className="font-mono" minRows={2} maxRows={6} />
  ) : (
    <Input
      {...shared}
      type={
        parameter.sensitive
          ? "password"
          : ["integer", "number"].includes(parameter.type)
            ? "number"
            : "text"
      }
      autoComplete="off"
    />
  );
}
