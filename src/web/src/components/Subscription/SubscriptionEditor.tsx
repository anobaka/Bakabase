"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { components } from "@/sdk/BApi2";

import React, { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";

import { getProviderUI } from "./Providers";

import BApi from "@/sdk/BApi";
import { Input, Modal, Select, Switch, toast } from "@/components/bakaui";
import ThirdPartyLabel from "@/components/ThirdPartyLabel";
import { ThirdPartyId } from "@/sdk/constants";

type SubscriptionVm =
  components["schemas"]["Bakabase.Modules.Subscription.Abstractions.Models.View.SubscriptionViewModel"];
type ProviderVm =
  components["schemas"]["Bakabase.Modules.Subscription.Abstractions.Models.View.SubscriptionProviderViewModel"];

interface SubscriptionEditorProps extends DestroyableProps {
  subscription?: SubscriptionVm;
  providers: ProviderVm[];
  onSaved?: () => void;
}

/**
 * Group registered server-side providers by the ThirdParty their frontend UI claims.
 * Server kinds that don't have a corresponding frontend UI registration are dropped —
 * we can't render a form for them anyway.
 */
function groupByThirdParty(providers: ProviderVm[]): Map<ThirdPartyId, ProviderVm[]> {
  const grouped = new Map<ThirdPartyId, ProviderVm[]>();
  for (const p of providers) {
    const ui = getProviderUI(p.kind);
    if (!ui) continue;
    const list = grouped.get(ui.thirdPartyId) ?? [];
    list.push(p);
    grouped.set(ui.thirdPartyId, list);
  }
  return grouped;
}

const SubscriptionEditor = ({
  subscription,
  providers,
  onSaved,
  onDestroyed,
}: SubscriptionEditorProps) => {
  const { t } = useTranslation();
  const isEditing = !!subscription;

  const groupedProviders = useMemo(() => groupByThirdParty(providers), [providers]);
  const availableThirdParties = useMemo(() => Array.from(groupedProviders.keys()), [groupedProviders]);

  // Pick the existing subscription's ThirdParty when editing, otherwise default to
  // the first ThirdParty that has at least one supported kind.
  const initialThirdParty: ThirdPartyId | null = (() => {
    if (subscription) {
      const ui = getProviderUI(subscription.kind);
      if (ui) return ui.thirdPartyId;
    }
    return availableThirdParties[0] ?? null;
  })();

  const [thirdPartyId, setThirdPartyId] = useState<ThirdPartyId | null>(initialThirdParty);

  const initialKind =
    subscription?.kind ??
    (initialThirdParty != null ? groupedProviders.get(initialThirdParty)?.[0]?.kind ?? "" : "");
  const [kind, setKind] = useState(initialKind);

  const [displayName, setDisplayName] = useState(subscription?.displayName ?? "");
  const [enabled, setEnabled] = useState(subscription?.enabled ?? true);

  const providerUi = useMemo(() => getProviderUI(kind), [kind]);

  const [target, setTarget] = useState<any>(() => {
    if (!providerUi) return {};
    return subscription?.targetJson
      ? providerUi.parseTarget(subscription.targetJson)
      : providerUi.defaultTarget();
  });

  const onThirdPartyChange = (next: ThirdPartyId) => {
    setThirdPartyId(next);
    // Auto-pick the first available kind for the new ThirdParty (and reset target).
    const firstKind = groupedProviders.get(next)?.[0]?.kind ?? "";
    setKind(firstKind);
    const nextUi = firstKind ? getProviderUI(firstKind) : undefined;
    setTarget(nextUi ? nextUi.defaultTarget() : {});
  };

  const onKindChange = (next: string) => {
    setKind(next);
    const nextUi = getProviderUI(next);
    setTarget(nextUi ? nextUi.defaultTarget() : {});
  };

  const kindOptions = useMemo(() => {
    if (thirdPartyId == null) return [];
    return (groupedProviders.get(thirdPartyId) ?? []).map((p) => ({
      value: p.kind,
      label: t<string>(`subscription.provider.${p.kind}.subKindLabel`, {
        defaultValue: p.displayName,
      }),
    }));
  }, [groupedProviders, thirdPartyId, t]);

  const isNameValid = displayName.trim().length > 0;
  const isKindValid = kind.length > 0 && !!providerUi;
  const isTargetValid = providerUi ? providerUi.isValid(target) : false;
  const isValid = isNameValid && isKindValid && isTargetValid;

  const handleSave = async () => {
    if (!isValid) return;
    const targetJson = JSON.stringify(target);
    try {
      if (isEditing) {
        await BApi.subscription.patchSubscription(subscription!.id, {
          displayName,
          enabled,
          targetJson,
        });
      } else {
        await BApi.subscription.addSubscription({
          kind,
          displayName,
          enabled,
          targetJson,
        });
      }
      onSaved?.();
    } catch (e: any) {
      toast.danger(e?.message ?? String(e));
      throw e;
    }
  };

  const FormComponent = providerUi?.Form;

  return (
    <Modal
      defaultVisible
      okProps={{ isDisabled: !isValid }}
      size="xl"
      title={isEditing ? t<string>("subscription.edit.title") : t<string>("subscription.create.title")}
      onDestroyed={onDestroyed}
      onOk={handleSave}
    >
      <div className="flex flex-col gap-4">
        <Input
          isRequired
          errorMessage={!isNameValid ? t<string>("subscription.validation.displayNameRequired") : undefined}
          isInvalid={!isNameValid}
          label={t<string>("subscription.field.displayName")}
          placeholder={t<string>("subscription.field.displayName.placeholder")}
          value={displayName}
          onValueChange={setDisplayName}
        />

        <div className="grid grid-cols-2 gap-3">
          <Select
            isRequired
            // Each item embeds the ThirdParty icon next to its localized name, and
            // renderValue mirrors that in the trigger so the picked source is
            // visually identifiable at a glance.
            dataSource={availableThirdParties.map((tpId) => ({
              value: tpId,
              label: <ThirdPartyLabel thirdPartyId={tpId} />,
              textValue: ThirdPartyId[tpId],
            }))}
            isDisabled={isEditing}
            label={t<string>("subscription.field.thirdParty")}
            renderValue={(items) => {
              const v = (items[0]?.data as { value?: ThirdPartyId } | undefined)?.value;
              if (v == null) return null;
              return <ThirdPartyLabel thirdPartyId={v} />;
            }}
            selectedKeys={thirdPartyId != null ? [String(thirdPartyId)] : []}
            onSelectionChange={(keys) => {
              const raw = Array.from(keys)[0];
              if (raw == null) return;
              const next = Number(raw) as ThirdPartyId;
              onThirdPartyChange(next);
            }}
          />

          <Select
            isRequired
            dataSource={kindOptions}
            // Hide / disable the kind picker when the ThirdParty only ships one kind —
            // there's nothing to choose, but we keep the slot for layout stability.
            isDisabled={isEditing || kindOptions.length <= 1}
            label={t<string>("subscription.field.kind")}
            selectedKeys={kind ? [kind] : []}
            onSelectionChange={(keys) => {
              const next = Array.from(keys)[0] as string | undefined;
              if (next) onKindChange(next);
            }}
          />
        </div>

        {FormComponent ? (
          <FormComponent value={target} onChange={setTarget} />
        ) : (
          <p className="text-xs text-warning-600">
            {t<string>("subscription.validation.kindRequired")}
          </p>
        )}

        <Switch isSelected={enabled} onValueChange={setEnabled}>
          {t<string>("subscription.field.enabled")}
        </Switch>
      </div>
    </Modal>
  );
};

export default SubscriptionEditor;
