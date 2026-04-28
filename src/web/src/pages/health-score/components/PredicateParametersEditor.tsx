import { Input, NumberInput, Select } from "@/components/bakaui";
import { useTranslation } from "react-i18next";

import { PredicateMediaType, predicateMediaTypes } from "@/sdk/constants";

const COMPARISON_OPS: { value: number; label: string }[] = [
  { value: 1, label: "=" },
  { value: 2, label: "≠" },
  { value: 3, label: ">" },
  { value: 4, label: "<" },
  { value: 5, label: "≥" },
  { value: 6, label: "≤" },
];

interface Props {
  predicateId: string;
  parameters: any;
  onChange: (next: any) => void;
}

/**
 * Hand-written parameter forms per built-in predicate. Adding a new predicate
 * requires both a backend `IFilePredicate` impl and a case here. Parameter-less
 * predicates fall through to <c>null</c> so the host can render an empty slot.
 */
export const PredicateParametersEditor = ({ predicateId, parameters, onChange }: Props) => {
  const { t } = useTranslation();

  const set = (patch: any) => onChange({ ...(parameters ?? {}), ...patch });

  switch (predicateId) {
    case "MediaTypeFileCount":
    case "MediaTypeTotalSize": {
      const isCount = predicateId === "MediaTypeFileCount";
      const currentMediaType =
        parameters?.mediaType ?? PredicateMediaType.All;
      return (
        <div className="flex flex-wrap items-end gap-2">
          <Select
            className="w-40"
            label={t<string>("healthScore.label.parameters")}
            dataSource={predicateMediaTypes.map((m) => ({
              value: String(m.value),
              label: m.label,
            }))}
            selectedKeys={[String(currentMediaType)]}
            onSelectionChange={(keys: any) => {
              const v = Array.from(keys)[0];
              if (v != null) set({ mediaType: Number(v) });
            }}
          />
          <Select
            className="w-24"
            label="Op"
            dataSource={COMPARISON_OPS.map((o) => ({ value: String(o.value), label: o.label }))}
            selectedKeys={parameters?.operator ? [String(parameters.operator)] : []}
            onSelectionChange={(keys: any) => {
              const v = Array.from(keys)[0];
              if (v != null) set({ operator: Number(v) });
            }}
          />
          <NumberInput
            className="w-32"
            label={isCount ? "Count" : "Bytes"}
            value={isCount ? (parameters?.count ?? 0) : (parameters?.bytes ?? 0)}
            onValueChange={(v) =>
              isCount ? set({ count: Number(v) }) : set({ bytes: Number(v) })
            }
          />
        </div>
      );
    }

    case "FileNamePatternCount": {
      return (
        <div className="flex flex-wrap items-end gap-2">
          <Input
            className="w-72"
            label="Regex"
            value={parameters?.pattern ?? ""}
            onValueChange={(v) => set({ pattern: v })}
          />
          <Select
            className="w-24"
            label="Op"
            dataSource={COMPARISON_OPS.map((o) => ({ value: String(o.value), label: o.label }))}
            selectedKeys={parameters?.operator ? [String(parameters.operator)] : []}
            onSelectionChange={(keys: any) => {
              const v = Array.from(keys)[0];
              if (v != null) set({ operator: Number(v) });
            }}
          />
          <NumberInput
            className="w-24"
            label="Count"
            value={parameters?.count ?? 0}
            onValueChange={(v) => set({ count: Number(v) })}
          />
        </div>
      );
    }

    case "FileSizeOutOfRange": {
      return (
        <div className="flex flex-wrap items-end gap-2">
          <NumberInput
            className="w-40"
            label="Min (bytes)"
            value={parameters?.min ?? undefined}
            onValueChange={(v) => set({ min: v == null ? null : Number(v) })}
          />
          <NumberInput
            className="w-40"
            label="Max (bytes)"
            value={parameters?.max ?? undefined}
            onValueChange={(v) => set({ max: v == null ? null : Number(v) })}
          />
        </div>
      );
    }

    case "HasCoverImage":
    case "RootDirectoryExists":
      return null;

    default:
      return (
        <Input
          className="w-72"
          label={t<string>("healthScore.label.parameters")}
          value={JSON.stringify(parameters ?? {})}
          onValueChange={(v) => {
            try { onChange(JSON.parse(v)); } catch { /* ignore */ }
          }}
        />
      );
  }
};
