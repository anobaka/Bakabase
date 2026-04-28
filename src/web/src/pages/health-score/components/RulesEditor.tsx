import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";
import { DeleteOutlined, FolderAddOutlined, PlusOutlined } from "@ant-design/icons";

import { PredicateParametersEditor } from "./PredicateParametersEditor";
import { PropertyLeafEditor } from "./PropertyLeafEditor";

import {
  Button,
  Card,
  Input,
  NumberInput,
  Select,
  Switch,
  Tooltip,
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import {
  FilePredicateDescriptor,
  HealthScoreRule,
  ResourceMatcher,
  ResourceMatcherLeaf,
  ResourceMatcherLeafKind,
  SearchCombinator,
} from "@/pages/health-score/types";

interface Props {
  rules: HealthScoreRule[];
  onChange: (rules: HealthScoreRule[]) => void;
}

const COMBINATORS = [
  { value: SearchCombinator.And, label: "And" },
  { value: SearchCombinator.Or, label: "Or" },
];

const blankLeaf = (predicateId: string): ResourceMatcherLeaf => ({
  kind: ResourceMatcherLeafKind.File,
  negated: false,
  disabled: false,
  filePredicateId: predicateId,
  filePredicateParametersJson: "{}",
});

const blankPropertyLeaf = (): ResourceMatcherLeaf => ({
  kind: ResourceMatcherLeafKind.Property,
  negated: false,
  disabled: false,
});

const parseParams = (json?: string | null): any => {
  if (!json) return null;
  try { return JSON.parse(json); }
  catch { return null; }
};

const blankGroup = (): ResourceMatcher => ({
  combinator: SearchCombinator.And,
  disabled: false,
  leaves: [],
  groups: [],
});

export const RulesEditor = ({ rules, onChange }: Props) => {
  const { t } = useTranslation();
  const [predicates, setPredicates] = useState<FilePredicateDescriptor[]>([]);

  useEffect(() => {
    BApi.healthScore.getAllFilePredicates().then((r) => {
      setPredicates(r.data ?? []);
    });
  }, []);

  const updateRule = (i: number, patch: Partial<HealthScoreRule>) => {
    const next = rules.slice();
    next[i] = { ...next[i], ...patch } as HealthScoreRule;
    onChange(next);
  };

  const addRule = () => {
    onChange([
      ...rules,
      {
        id: 0,
        delta: -10,
        match: blankGroup(),
      } as HealthScoreRule,
    ]);
  };

  const removeRule = (i: number) => {
    const next = rules.slice();
    next.splice(i, 1);
    onChange(next);
  };

  return (
    <div className="flex flex-col gap-3">
      {rules.length === 0 && (
        <div className="text-default-500 text-sm">{t<string>("healthScore.empty.noRules")}</div>
      )}

      {rules.map((rule, i) => (
        <Card key={i} className="p-3">
          <div className="flex flex-wrap items-center gap-3">
            <span className="text-sm text-default-500 font-mono">{i + 1}.</span>
            <Input
              className="w-60"
              size="sm"
              label={t<string>("healthScore.label.ruleName")}
              value={rule.name ?? ""}
              onValueChange={(v) => updateRule(i, { name: v })}
            />
            <Tooltip content={t<string>("healthScore.tip.delta")}>
              <NumberInput
                className="w-32"
                size="sm"
                label={t<string>("healthScore.label.delta")}
                value={rule.delta}
                onValueChange={(v) => updateRule(i, { delta: Number(v ?? 0) })}
              />
            </Tooltip>
            <div className="ml-auto">
              <Button
                isIconOnly
                color="danger"
                size="sm"
                variant="light"
                onPress={() => removeRule(i)}
              >
                <DeleteOutlined className="text-lg" />
              </Button>
            </div>
          </div>

          <div className="mt-3">
            <MatcherEditor
              matcher={(rule.match as ResourceMatcher) ?? blankGroup()}
              predicates={predicates}
              onChange={(m) => updateRule(i, { match: m } as Partial<HealthScoreRule>)}
              depth={0}
              path={`${i + 1}`}
            />
          </div>
        </Card>
      ))}

      <div>
        <Button startContent={<PlusOutlined className="text-lg" />} onPress={addRule}>
          {t<string>("healthScore.action.addRule")}
        </Button>
      </div>
    </div>
  );
};

interface MatcherEditorProps {
  matcher: ResourceMatcher;
  predicates: FilePredicateDescriptor[];
  onChange: (next: ResourceMatcher) => void;
  depth: number;
  /** Numbering prefix for this group (e.g. "1.2") */
  path: string;
}

/**
 * Recursive editor for a ResourceMatcher node. Renders a combinator selector,
 * a flat list of leaves, and any nested sub-groups (each rendered by another
 * MatcherEditor). Indents nested groups so the tree shape stays readable.
 *
 * The <c>path</c> prop carries dotted numbering (e.g. "1.2.3") which is
 * inherited by leaves and sub-groups so users can refer to conditions
 * unambiguously when rules grow large.
 */
const MatcherEditor = ({ matcher, predicates, onChange, depth, path }: MatcherEditorProps) => {
  const { t } = useTranslation();

  const setCombinator = (c: SearchCombinator) => onChange({ ...matcher, combinator: c });
  const setLeaves = (leaves: ResourceMatcherLeaf[]) => onChange({ ...matcher, leaves });
  const setGroups = (groups: ResourceMatcher[]) => onChange({ ...matcher, groups });

  const addFileLeaf = () => {
    const first = predicates[0];
    setLeaves([...(matcher.leaves ?? []), blankLeaf(first?.id ?? "")]);
  };

  const addPropertyLeaf = () => {
    setLeaves([...(matcher.leaves ?? []), blankPropertyLeaf()]);
  };

  const addGroup = () => {
    setGroups([...(matcher.groups ?? []), blankGroup()]);
  };

  const leafCount = matcher.leaves?.length ?? 0;

  return (
    <div
      className={depth > 0 ? "border-l-2 border-default-200 pl-3" : undefined}
    >
      <div className="flex items-center gap-2">
        {depth > 0 && (
          <span className="text-sm text-default-500 font-mono">{path}</span>
        )}
        <Select
          className="w-28"
          size="sm"
          label={t<string>("healthScore.label.combinator")}
          dataSource={COMBINATORS.map((c) => ({ value: String(c.value), label: c.label }))}
          selectedKeys={[String(matcher.combinator ?? SearchCombinator.And)]}
          onSelectionChange={(keys: any) => {
            const v = Array.from(keys)[0];
            if (v != null) setCombinator(Number(v) as SearchCombinator);
          }}
        />
        <Switch
          isSelected={matcher.disabled ?? false}
          size="sm"
          onValueChange={(v) => onChange({ ...matcher, disabled: v })}
        >
          {t<string>("healthScore.label.disabled")}
        </Switch>
      </div>

      <div className="mt-2 flex flex-col gap-2">
        {(matcher.leaves ?? []).map((leaf, i) => (
          <LeafRow
            key={`leaf-${i}`}
            leaf={leaf}
            predicates={predicates}
            label={`${path}.${i + 1}`}
            onChange={(next) => {
              const list = (matcher.leaves ?? []).slice();
              list[i] = next;
              setLeaves(list);
            }}
            onRemove={() => {
              const list = (matcher.leaves ?? []).slice();
              list.splice(i, 1);
              setLeaves(list);
            }}
          />
        ))}

        {(matcher.groups ?? []).map((group, i) => (
          <div
            key={`group-${i}`}
            className="rounded-medium bg-default-50 p-2 flex items-start gap-2"
          >
            <div className="flex-1">
              <MatcherEditor
                matcher={group}
                predicates={predicates}
                onChange={(next) => {
                  const list = (matcher.groups ?? []).slice();
                  list[i] = next;
                  setGroups(list);
                }}
                depth={depth + 1}
                path={`${path}.${leafCount + i + 1}`}
              />
            </div>
            <Button
              isIconOnly
              color="danger"
              size="sm"
              variant="light"
              onPress={() => {
                const list = (matcher.groups ?? []).slice();
                list.splice(i, 1);
                setGroups(list);
              }}
            >
              <DeleteOutlined className="text-lg" />
            </Button>
          </div>
        ))}
      </div>

      <div className="mt-2 flex gap-2">
        <Button
          size="sm"
          startContent={<PlusOutlined className="text-lg" />}
          variant="flat"
          onPress={addFileLeaf}
        >
          {t<string>("healthScore.action.addFileLeaf")}
        </Button>
        <Button
          size="sm"
          startContent={<PlusOutlined className="text-lg" />}
          variant="flat"
          onPress={addPropertyLeaf}
        >
          {t<string>("healthScore.action.addPropertyLeaf")}
        </Button>
        <Button
          size="sm"
          startContent={<FolderAddOutlined className="text-lg" />}
          variant="flat"
          onPress={addGroup}
        >
          {t<string>("healthScore.action.addGroup")}
        </Button>
      </div>
    </div>
  );
};

interface LeafRowProps {
  leaf: ResourceMatcherLeaf;
  predicates: FilePredicateDescriptor[];
  label: string;
  onChange: (next: ResourceMatcherLeaf) => void;
  onRemove: () => void;
}

const LeafRow = ({ leaf, predicates, label, onChange, onRemove }: LeafRowProps) => {
  const { t } = useTranslation();
  const isProperty = leaf.kind === ResourceMatcherLeafKind.Property;

  return (
    <div className="flex flex-wrap items-center gap-2 border-l-2 border-default-200 pl-3">
      <span className="text-sm text-default-500 font-mono">{label}</span>
      <Switch
        isSelected={leaf.negated ?? false}
        size="sm"
        onValueChange={(v) => onChange({ ...leaf, negated: v })}
      >
        {t<string>("healthScore.label.negated")}
      </Switch>

      {isProperty ? (
        <PropertyLeafEditor leaf={leaf} onChange={onChange} />
      ) : (
        <>
          <Select
            className="w-56"
            size="sm"
            label={t<string>("healthScore.label.predicate")}
            dataSource={predicates.map((p) => ({
              value: p.id ?? "",
              label: t<string>(p.displayNameKey ?? p.id ?? ""),
            }))}
            selectedKeys={leaf.filePredicateId ? [leaf.filePredicateId] : []}
            onSelectionChange={(keys: any) => {
              const v = Array.from(keys)[0];
              if (v) onChange({ ...leaf, filePredicateId: String(v), filePredicateParametersJson: "{}" });
            }}
          />
          {leaf.filePredicateId && (
            <PredicateParametersEditor
              predicateId={leaf.filePredicateId}
              parameters={parseParams(leaf.filePredicateParametersJson)}
              onChange={(p) =>
                onChange({ ...leaf, filePredicateParametersJson: JSON.stringify(p ?? {}) })
              }
            />
          )}
        </>
      )}

      <Button
        isIconOnly
        className="ml-auto"
        color="danger"
        size="sm"
        variant="light"
        onPress={onRemove}
      >
        <DeleteOutlined className="text-lg" />
      </Button>
    </div>
  );
};
