"use client";

import React, { useCallback, useState } from "react";
import { useTranslation } from "react-i18next";
import { MdTranslate } from "react-icons/md";
import {
  Checkbox,
  Modal,
  toast,
} from "@/components/bakaui";
import LanguageDropdown from "@/components/LanguageDropdown";
import type { DestroyableProps } from "@/components/bakaui/types";
import AiFeatureButton from "@/components/AiFeatureButton";
import BApi from "@/sdk/BApi";
import type { BakabaseModulesAIModelsDomainPropertyTranslation } from "@/sdk/Api";
import { AiFeature, PropertyPool, StandardValueType } from "@/sdk/constants";
import { useAppOptionsStore } from "@/stores/options";
import AiFeatureConfigShortcut from "@/components/AiFeatureConfigShortcut";
import BriefProperty from "@/components/Chips/Property/BriefProperty";
import { serializeStandardValue } from "@/components/StandardValue/helpers";
import PropertyValueRenderer from "@/components/Property/components/PropertyValueRenderer";
import type { IProperty } from "@/components/Property/models";

type Props = {
  resourceId: number;
  onTranslationApplied?: () => void;
} & DestroyableProps;

type TranslationItem = BakabaseModulesAIModelsDomainPropertyTranslation & {
  selected: boolean;
};

const buildProperty = (tr: BakabaseModulesAIModelsDomainPropertyTranslation): IProperty => ({
  id: tr.propertyId,
  pool: tr.propertyPool as any,
  type: tr.propertyType as any,
  name: tr.propertyName ?? "",
  dbValueType: tr.dbValueType ?? StandardValueType.String,
  bizValueType: tr.bizValueType ?? StandardValueType.String,
  typeName: "",
  poolName: "",
  order: 0,
});

const AiTranslateModal = ({ resourceId, onTranslationApplied, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const appLanguage = useAppOptionsStore((state) => state.data?.language);

  const [targetLanguage, setTargetLanguage] = useState(appLanguage ?? "en");
  const [loading, setLoading] = useState(false);
  const [applying, setApplying] = useState(false);
  const [items, setItems] = useState<TranslationItem[]>([]);
  const [translated, setTranslated] = useState(false);

  const handleTranslate = useCallback(async () => {
    setLoading(true);
    try {
      const r = await BApi.ai.aiTranslateResourceProperties(resourceId, {
        targetLanguage,
      });
      if (!r.code && r.data?.translations) {
        setItems(
          r.data.translations
            .filter((tr) => tr.originalText !== tr.translatedText)
            .map((tr) => ({ ...tr, selected: true })),
        );
        setTranslated(true);
      }
    } finally {
      setLoading(false);
    }
  }, [resourceId, targetLanguage]);

  const handleApply = useCallback(async () => {
    const selected = items.filter((it) => it.selected);
    if (selected.length === 0) return;

    setApplying(true);
    try {
      for (const item of selected) {
        const serializedValue = serializeStandardValue(
          item.translatedText,
          StandardValueType.String,
        );
        await BApi.resource.putResourcePropertyValue(resourceId, {
          propertyId: item.propertyId,
          isCustomProperty: item.propertyPool === PropertyPool.Custom,
          value: serializedValue,
        });
      }
      toast.success(t<string>("resource.ai.translateSuccess"));
      onTranslationApplied?.();
    } finally {
      setApplying(false);
    }
  }, [items, resourceId, onTranslationApplied, t]);

  const toggleAll = useCallback(
    (checked: boolean) => {
      setItems((prev) => prev.map((it) => ({ ...it, selected: checked })));
    },
    [],
  );

  const toggleItem = useCallback((idx: number) => {
    setItems((prev) =>
      prev.map((it, i) => (i === idx ? { ...it, selected: !it.selected } : it)),
    );
  }, []);

  const allSelected = items.length > 0 && items.every((it) => it.selected);
  const someSelected = items.some((it) => it.selected);

  return (
    <Modal
      defaultVisible
      size="lg"
      title={
        <div className="flex items-center gap-2">
          <MdTranslate className="text-lg" />
          {t("resource.action.aiTranslate")}
        </div>
      }
      footer={
        <div className="flex items-center w-full gap-2">
          <AiFeatureConfigShortcut feature={AiFeature.Translation} />
          <div className="flex-1" />
          {translated && items.length > 0 && (
            <AiFeatureButton
              feature={AiFeature.Translation}
              color="success"
              isLoading={applying}
              isDisabled={!someSelected}
              onPress={handleApply}
            >
              {t("resource.ai.applySelected")}
              {someSelected ? ` (${items.filter((it) => it.selected).length})` : ""}
            </AiFeatureButton>
          )}
          <AiFeatureButton
            feature={AiFeature.Translation}
            color="primary"
            isLoading={loading}
            onPress={handleTranslate}
          >
            {loading ? t("resource.ai.translating") : t("resource.action.aiTranslate")}
          </AiFeatureButton>
        </div>
      }
      onDestroyed={onDestroyed}
    >
      <div className="flex flex-col gap-4">
        <div className="flex items-center gap-2">
          <span className="text-sm whitespace-nowrap">{t("resource.ai.targetLanguage")}</span>
          <LanguageDropdown
            size="sm"
            value={targetLanguage}
            onValueChange={setTargetLanguage}
          />
        </div>

        {translated && items.length === 0 && (
          <p className="text-sm text-default-400">{t("resource.ai.noTextProperties")}</p>
        )}

        {items.length > 0 && (
          <div className="flex flex-col gap-2">
            <div className="text-sm font-medium text-default-600">
              {t("resource.ai.translationResults")}
            </div>
            <div className="max-h-[60vh] overflow-y-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-default-200">
                    <th className="p-2 w-8">
                      <Checkbox
                        size="sm"
                        isSelected={allSelected}
                        isIndeterminate={someSelected && !allSelected}
                        onValueChange={toggleAll}
                      />
                    </th>
                    <th className="p-2 text-left text-default-500">{t("resource.ai.property")}</th>
                    <th className="p-2 text-left text-default-500">{t("resource.ai.original")}</th>
                    <th className="p-2 text-left text-default-500">{t("resource.ai.translated")}</th>
                  </tr>
                </thead>
                <tbody>
                  {items.map((tr, idx) => {
                    const property = buildProperty(tr);
                    return (
                      <tr
                        key={idx}
                        className="border-b border-default-100 hover:bg-default-50"
                      >
                        <td className="p-2">
                          <Checkbox
                            size="sm"
                            isSelected={tr.selected}
                            onValueChange={() => toggleItem(idx)}
                          />
                        </td>
                        <td className="p-2">
                          <BriefProperty
                            property={property}
                            fields={["type", "name"]}
                            showPoolChip={false}
                          />
                        </td>
                        <td className="p-2">
                          <PropertyValueRenderer
                            property={property}
                            bizValue={serializeStandardValue(tr.originalText, StandardValueType.String)}
                            isReadonly
                            size="sm"
                          />
                        </td>
                        <td className="p-2">
                          <PropertyValueRenderer
                            property={property}
                            bizValue={serializeStandardValue(tr.translatedText, StandardValueType.String)}
                            isReadonly
                            size="sm"
                          />
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </div>
    </Modal>
  );
};

AiTranslateModal.displayName = "AiTranslateModal";

export default AiTranslateModal;
