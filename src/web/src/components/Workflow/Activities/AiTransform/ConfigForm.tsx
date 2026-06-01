import type { AiTransformConfig } from "./types";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import BApi from "@/sdk/BApi";
import { Select, Spinner, Textarea } from "@/components/bakaui";

interface Props {
  value: AiTransformConfig;
  onChange: (v: AiTransformConfig) => void;
}

/**
 * Two fields:
 *  - Target type (Select, optional) — if empty, the AI transform inherits from the next
 *    activity's single accepted type. The "(auto-detect)" option is the deliberate way to
 *    express "let chain pick".
 *  - Extra instructions (Textarea, optional) — appended to the system prompt as user
 *    guidance.
 *
 * Fetches item types once on mount.
 */
const ConfigForm: React.FC<Props> = ({ value, onChange }) => {
  const { t } = useTranslation();
  const [types, setTypes] = useState<
    | null
    | Array<{ value: string; label: string }>
  >(null);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      const rsp = await BApi.workflow.getWorkflowItemTypes();
      if (cancelled) return;
      const opts = (rsp.data ?? []).map((d) => ({
        value: d.itemType,
        label: `${d.displayName}  ·  ${d.itemType}`,
      }));
      // Reserve an empty value for "let the next activity decide".
      setTypes([
        { value: "", label: t<string>("workflow.activity.aiTransform.targetItemType.auto") },
        ...opts,
      ]);
    })();
    return () => {
      cancelled = true;
    };
  }, [t]);

  return (
    <div className="flex flex-col gap-3">
      {types === null ? (
        <Spinner size="sm" />
      ) : (
        <Select
          dataSource={types}
          description={t<string>("workflow.activity.aiTransform.targetItemType.description")}
          label={t<string>("workflow.activity.aiTransform.targetItemType.label")}
          selectedKeys={[value.targetItemType]}
          onSelectionChange={(keys) => {
            const next = Array.from(keys)[0] as string | undefined;
            onChange({ ...value, targetItemType: next ?? "" });
          }}
        />
      )}
      <Textarea
        description={t<string>("workflow.activity.aiTransform.extraInstructions.description")}
        label={t<string>("workflow.activity.aiTransform.extraInstructions.label")}
        minRows={2}
        value={value.extraInstructions}
        onValueChange={(s) => onChange({ ...value, extraInstructions: s })}
      />
    </div>
  );
};

export default ConfigForm;
