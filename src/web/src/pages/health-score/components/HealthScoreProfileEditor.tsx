import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";

import { RulesEditor } from "./RulesEditor";

import {
  Input,
  Modal,
  NumberInput,
  Tab,
  Tabs,
  Tooltip,
} from "@/components/bakaui";
import { ResourceFilterController } from "@/components/ResourceFilter";
import type { SearchFilterGroup } from "@/components/ResourceFilter/models";
import BApi from "@/sdk/BApi";
import {
  HealthScoreProfile,
  apiFilterGroupToInternal,
  internalFilterGroupToApi,
} from "@/pages/health-score/types";

interface Props {
  profile: HealthScoreProfile;
  onClose: () => void;
  onSaved: () => void;
}

const isEmptyInternalFilter = (g: SearchFilterGroup | null | undefined): boolean => {
  if (!g) return true;
  const filters = (g.filters ?? []).length;
  const groups = (g.groups ?? []).length;
  return filters === 0 && groups === 0;
};

export const HealthScoreProfileEditor = ({ profile, onClose, onSaved }: Props) => {
  const { t } = useTranslation();
  const [draft, setDraft] = useState<HealthScoreProfile>({ ...profile });
  // Filter is edited in the ResourceFilterController's internal shape (`dbValue`),
  // then converted back to the API's DB shape (`value`) when persisting.
  const [filterGroup, setFilterGroup] = useState<SearchFilterGroup | undefined>(
    () => apiFilterGroupToInternal(profile.membershipFilter),
  );
  const [saving, setSaving] = useState(false);
  const isEmpty = useMemo(() => isEmptyInternalFilter(filterGroup), [filterGroup]);

  const save = async () => {
    setSaving(true);
    try {
      await BApi.healthScore.patchHealthScoreProfile(draft.id, {
        name: draft.name,
        enabled: draft.enabled,
        priority: draft.priority,
        baseScore: draft.baseScore,
        membershipFilter: internalFilterGroupToApi(filterGroup) as any,
        rules: draft.rules as any,
      });
      toast.success(t<string>("healthScore.toast.saved"));
      onSaved();
      onClose();
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["cancel", "ok"],
        okProps: { children: t<string>("healthScore.action.save"), isLoading: saving },
      }}
      size="4xl"
      title={draft.name}
      onClose={onClose}
      onOk={save}
    >
      <Tabs>
        <Tab key="basic" title={t<string>("healthScore.tab.basic")}>
          <div className="flex flex-col gap-3 mt-2">
            <Input
              label={t<string>("healthScore.label.name")}
              value={draft.name ?? ""}
              onValueChange={(v) => setDraft({ ...draft, name: v })}
            />
            <div className="flex items-center gap-3">
              <Tooltip content={t<string>("healthScore.tip.priority")}>
                <NumberInput
                  className="w-40"
                  label={t<string>("healthScore.label.priority")}
                  value={draft.priority ?? 0}
                  onValueChange={(v) => setDraft({ ...draft, priority: Number(v ?? 0) })}
                />
              </Tooltip>
              <NumberInput
                className="w-40"
                label={t<string>("healthScore.label.baseScore")}
                value={draft.baseScore ?? 100}
                onValueChange={(v) => setDraft({ ...draft, baseScore: Number(v ?? 100) })}
              />
            </div>
          </div>
        </Tab>

        <Tab key="resources" title={t<string>("healthScore.tab.resources")}>
          <div className="mt-2 flex flex-col gap-2">
            {isEmpty && (
              <div className="text-default-500 text-sm">
                {t<string>("healthScore.empty.allResources")}
              </div>
            )}
            <ResourceFilterController
              group={filterGroup}
              onGroupChange={setFilterGroup}
              filterLayout="vertical"
            />
          </div>
        </Tab>

        <Tab key="rules" title={t<string>("healthScore.tab.rules")}>
          <div className="mt-2">
            <RulesEditor
              rules={draft.rules ?? []}
              onChange={(rules) => setDraft({ ...draft, rules })}
            />
          </div>
        </Tab>
      </Tabs>
    </Modal>
  );
};
