"use client";

import type { SearchFilterGroup } from "@/components/ResourceFilter/models";

import { useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import { buildRuleSearchJson, isEmptyGroup, parseRuleSearchJson } from "./ruleJson";

import { Button, Chip, Spinner } from "@/components/bakaui";
import { ResourceFilterController } from "@/components/ResourceFilter";
import BApi from "@/sdk/BApi";

type Props = {
  /** The collection's stored rule, or null when it has none. */
  value?: string | null;
  onChange: (ruleSearchJson: string | null) => void;
  /** How many matching resources to name in the preview. */
  sampleSize?: number;
};

type Preview = { totalCount: number; sampleResourceIds: number[] } | "failed" | undefined;

/**
 * Writing a rule and seeing what it catches, at the same time.
 *
 * Writing one blind — save it, open the collection, come back — is how a collection ends
 * up quietly holding the wrong hundred things, so the count is never more than a moment
 * behind what is on screen.
 */
const CollectionRuleEditor = ({ value, onChange, sampleSize = 8 }: Props) => {
  const { t } = useTranslation();
  const [group, setGroup] = useState<SearchFilterGroup | undefined>(() =>
    parseRuleSearchJson(value),
  );
  const [preview, setPreview] = useState<Preview>();
  const [checking, setChecking] = useState(false);

  const ruleJson = useMemo(() => buildRuleSearchJson(group), [group]);
  const empty = isEmptyGroup(group);

  // Guards against an earlier, slower preview landing after a later one and showing a
  // count for a rule that is no longer on screen.
  const requestId = useRef(0);

  useEffect(() => {
    if (empty) {
      setPreview(undefined);
      setChecking(false);

      return;
    }

    const id = ++requestId.current;

    setChecking(true);

    // Typing a value into a filter changes the rule on every keystroke; a request per
    // keystroke would be a full resource search per keystroke.
    const handle = setTimeout(async () => {
      try {
        const rsp = await BApi.collection.previewCollectionRule({
          ruleSearchJson: ruleJson ?? undefined,
          sampleSize,
        });

        if (id === requestId.current) setPreview(rsp.data ?? "failed");
      } catch {
        if (id === requestId.current) setPreview("failed");
      } finally {
        if (id === requestId.current) setChecking(false);
      }
    }, 400);

    return () => clearTimeout(handle);
  }, [ruleJson, empty, sampleSize]);

  const apply = (next: SearchFilterGroup | undefined) => {
    setGroup(next);
    onChange(buildRuleSearchJson(next));
  };

  return (
    <div className="flex flex-col gap-2">
      <div className="text-sm text-default-500">{t<string>("collection.rule.description")}</div>

      <ResourceFilterController filterLayout="vertical" group={group} onGroupChange={apply} />

      <div className="flex items-center gap-2 min-h-8">
        {empty ? (
          <span className="text-sm text-default-400">{t<string>("collection.rule.empty")}</span>
        ) : checking ? (
          <>
            <Spinner size="sm" />
            <span className="text-sm text-default-400">
              {t<string>("collection.rule.checking")}
            </span>
          </>
        ) : preview === "failed" ? (
          <span className="text-sm text-danger">{t<string>("collection.rule.failed")}</span>
        ) : preview ? (
          <>
            <Chip color={preview.totalCount > 0 ? "success" : "default"} size="sm" variant="flat">
              {preview.totalCount > 0
                ? t<string>("collection.rule.matches", { count: preview.totalCount })
                : t<string>("collection.rule.matchesNone")}
            </Chip>
            {preview.sampleResourceIds.length > 0 && (
              <span className="text-xs text-default-400">
                {t<string>("collection.rule.sample")}:{" "}
                {preview.sampleResourceIds.map((id) => `#${id}`).join(", ")}
              </span>
            )}
          </>
        ) : null}

        {!empty && (
          <Button className="ml-auto" size="sm" variant="light" onPress={() => apply(undefined)}>
            {t<string>("collection.rule.clear")}
          </Button>
        )}
      </div>
    </div>
  );
};

CollectionRuleEditor.displayName = "CollectionRuleEditor";

export default CollectionRuleEditor;
export { buildMultilevelSubtreeRule, buildRuleSearchJson, parseRuleSearchJson } from "./ruleJson";
