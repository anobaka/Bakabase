import type { components } from "@/sdk/BApi2";
import type { SubscriptionUpdatedFilter } from "./types";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import BApi from "@/sdk/BApi";
import { Select, Spinner } from "@/components/bakaui";

type SubscriptionVm =
  components["schemas"]["Bakabase.Modules.Subscription.Abstractions.Models.View.SubscriptionViewModel"];

interface Props {
  value: SubscriptionUpdatedFilter;
  onChange: (v: SubscriptionUpdatedFilter) => void;
}

const FilterForm: React.FC<Props> = ({ value, onChange }) => {
  const { t } = useTranslation();
  const [subs, setSubs] = useState<SubscriptionVm[] | null>(null);

  // Fetch current subscriptions so the user picks by display name rather than
  // typing IDs. Refresh is on-open only — workflows aren't expected to be
  // edited often enough to warrant live updates here.
  useEffect(() => {
    let cancelled = false;
    void (async () => {
      const rsp = await BApi.subscription.searchSubscriptions({});
      if (!cancelled) setSubs((rsp.data ?? []) as SubscriptionVm[]);
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  if (subs === null) {
    return (
      <div className="flex justify-center py-3">
        <Spinner size="sm" />
      </div>
    );
  }

  // De-duplicate the set of kinds across all existing subscriptions for the kind picker.
  const allKinds = Array.from(new Set(subs.map((s) => s.kind))).sort();

  return (
    <div className="flex flex-col gap-3">
      <Select
        dataSource={subs.map((s) => ({
          value: String(s.id),
          label: s.displayName,
          description: s.kind,
        }))}
        description={t<string>("workflow.trigger.subscriptionUpdated.subscriptionIds.description")}
        label={t<string>("workflow.trigger.subscriptionUpdated.subscriptionIds.label")}
        selectedKeys={value.subscriptionIds.map(String)}
        selectionMode="multiple"
        onSelectionChange={(keys) => {
          const ids = Array.from(keys).map((k) => Number(k)).filter((n) => !isNaN(n));
          onChange({ ...value, subscriptionIds: ids });
        }}
      />

      {allKinds.length > 0 && (
        <Select
          // Localize each kind via the subscription provider's `subKindLabel` key — the same
          // human-readable label the subscription editor uses for its kind picker. Falls back
          // to the raw kind so a renamed/new provider still shows up.
          dataSource={allKinds.map((k) => ({
            value: k,
            label: t<string>(`subscription.provider.${k}.subKindLabel`, { defaultValue: k }),
          }))}
          description={t<string>("workflow.trigger.subscriptionUpdated.kinds.description")}
          label={t<string>("workflow.trigger.subscriptionUpdated.kinds.label")}
          selectedKeys={value.kinds}
          selectionMode="multiple"
          onSelectionChange={(keys) => {
            onChange({ ...value, kinds: Array.from(keys).map(String) });
          }}
        />
      )}
    </div>
  );
};

export default FilterForm;
