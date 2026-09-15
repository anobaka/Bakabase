"use client";

import type { CollectionModel } from "@/stores/collections";

import React, { useState } from "react";
import { useTranslation } from "react-i18next";

import BApi from "@/sdk/BApi";
import { Button, Checkbox, Input, NumberInput, Textarea, toast } from "@/components/bakaui";

type Props = {
  collection: CollectionModel;
  onSaved: () => void;
};

const SettingsTab: React.FC<Props> = ({ collection, onSaved }) => {
  const { t } = useTranslation();
  const [draft, setDraft] = useState<CollectionModel>({ ...collection });
  const [saving, setSaving] = useState(false);

  const save = async () => {
    if (!draft.name?.trim()) {
      toast.danger(t<string>("collection.error.nameEmpty"));

      return;
    }

    setSaving(true);
    try {
      await BApi.collection.putCollection(collection.id, {
        name: draft.name.trim(),
        description: draft.description,
        color: draft.color,
        coverPath: draft.coverPath,
        // The rule is edited in its own tab; sending the draft's copy keeps this save from
        // silently reverting a rule saved a moment ago.
        ruleSearchJson: collection.ruleSearchJson,
        autoAcquire: draft.autoAcquire,
        acquisitionSettingsJson: draft.acquisitionSettingsJson,
        order: draft.order ?? 0,
      });
      toast.success(t<string>("collection.saved"));
      onSaved();
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="flex flex-col gap-3 max-w-2xl">
      <Input
        label={t<string>("collection.label.name")}
        value={draft.name ?? ""}
        onValueChange={(v) => setDraft({ ...draft, name: v })}
      />
      <Textarea
        label={t<string>("collection.label.description")}
        value={draft.description ?? ""}
        onValueChange={(v) => setDraft({ ...draft, description: v })}
      />
      <NumberInput
        className="w-40"
        label={t<string>("collection.label.order")}
        value={draft.order ?? 0}
        onValueChange={(v) => setDraft({ ...draft, order: Number(v ?? 0) })}
      />
      <Checkbox
        isSelected={draft.autoAcquire ?? false}
        onValueChange={(v) => setDraft({ ...draft, autoAcquire: v })}
      >
        <div className="flex flex-col">
          <span>{t<string>("collection.label.autoAcquire")}</span>
          <span className="text-xs text-default-500">
            {t<string>("collection.label.autoAcquireDescription")}
          </span>
        </div>
      </Checkbox>
      <div>
        <Button color="primary" isLoading={saving} onPress={save}>
          {t<string>("collection.action.save")}
        </Button>
      </div>
    </div>
  );
};

SettingsTab.displayName = "SettingsTab";

export default SettingsTab;
