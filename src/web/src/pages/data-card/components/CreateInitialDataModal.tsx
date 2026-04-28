"use client";

import React, { useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { LoadingOutlined, WarningOutlined } from "@ant-design/icons";
import toast from "react-hot-toast";

import BApi from "@/sdk/BApi";
import { Checkbox, Modal } from "@/components/bakaui";
import type { IProperty } from "@/components/Property/models";
import type { DestroyableProps } from "@/components/bakaui/types";
import { CustomPropertyAdditionalItem } from "@/sdk/constants";

interface CardType {
  id: number;
  name: string;
  propertyIds?: number[];
  identityPropertyIds?: number[];
}

interface PreviewResult {
  toCreate: number;
  alreadyExists: number;
}

interface CreateInitialDataModalProps extends DestroyableProps {
  typeId: number;
  typeName: string;
  onCreated?: () => void;
}

const CreateInitialDataModal = ({
  typeId,
  typeName,
  onCreated,
  onDestroyed,
}: CreateInitialDataModalProps) => {
  const { t } = useTranslation();
  const [onlyFromResources, setOnlyFromResources] = useState(true);
  const [allowNullPropertyIds, setAllowNullPropertyIds] = useState<Set<number>>(new Set());
  const [cardType, setCardType] = useState<CardType | null>(null);
  const [allProperties, setAllProperties] = useState<IProperty[]>([]);

  const [previewing, setPreviewing] = useState(false);
  const [preview, setPreview] = useState<PreviewResult | null>(null);
  const reqSeq = useRef(0);

  useEffect(() => {
    const load = async () => {
      const [typeRsp, propsRsp] = await Promise.all([
        BApi.dataCardType.getDataCardType(typeId),
        // @ts-ignore
        BApi.customProperty.getAllCustomProperties({
          additionalItems: CustomPropertyAdditionalItem.None,
        }),
      ]);
      setCardType((typeRsp.data || null) as CardType | null);
      // @ts-ignore
      setAllProperties((propsRsp.data || []) as IProperty[]);
    };
    load();
  }, [typeId]);

  const identityPropertyIds = useMemo(() => {
    const ids = cardType?.identityPropertyIds;
    if (ids?.length) return ids;
    return cardType?.propertyIds || [];
  }, [cardType]);

  const identityProperties = useMemo(
    () => identityPropertyIds
      .map((pid) => allProperties.find((p) => p.id === pid))
      .filter(Boolean) as IProperty[],
    [identityPropertyIds, allProperties],
  );

  // Run preview whenever the inputs change (and once cardType is loaded).
  useEffect(() => {
    if (!cardType) return;
    const mySeq = ++reqSeq.current;
    setPreviewing(true);
    (async () => {
      try {
        const rsp = await BApi.dataCard.previewInitialDataCards(typeId, {
          onlyFromResources,
          allowNullPropertyIds: Array.from(allowNullPropertyIds),
        });
        if (mySeq !== reqSeq.current) return;
        setPreview({
          toCreate: Number(rsp?.data?.toCreate ?? 0),
          alreadyExists: Number(rsp?.data?.alreadyExists ?? 0),
        });
        setPreviewing(false);
      } catch {
        if (mySeq !== reqSeq.current) return;
        setPreview(null);
        setPreviewing(false);
      }
    })();
  }, [cardType, onlyFromResources, allowNullPropertyIds, typeId]);

  const toggleAllowNull = (propertyId: number) => {
    setAllowNullPropertyIds((prev) => {
      const next = new Set(prev);
      if (next.has(propertyId)) {
        next.delete(propertyId);
      } else {
        next.add(propertyId);
      }
      return next;
    });
  };

  const handleCreate = async () => {
    const rsp = await BApi.dataCard.createInitialDataCards(typeId, {
      onlyFromResources,
      allowNullPropertyIds: Array.from(allowNullPropertyIds),
    });
    const count = rsp.data ?? 0;
    toast.success(t("dataCard.action.createInitialData.success", { count }));
    onCreated?.();
  };

  return (
    <Modal
      defaultVisible
      size="md"
      title={`${t("dataCard.action.createInitialData")} - ${typeName}`}
      onOk={handleCreate}
      onDestroyed={onDestroyed}
      okProps={{
        isDisabled: previewing || preview?.toCreate === 0,
      }}
    >
      <div className="flex flex-col gap-4">
        <p className="text-sm text-default-600">
          {t("dataCard.action.createInitialData.description")}
        </p>

        {/* Only from resources */}
        <div className="border border-default-200 rounded-lg p-4">
          <Checkbox
            isSelected={onlyFromResources}
            onValueChange={setOnlyFromResources}
          >
            <span className="text-sm">{t("dataCard.action.createInitialData.onlyFromResources")}</span>
          </Checkbox>
          <p className="text-xs text-default-400 mt-2 ml-7">
            {t("dataCard.action.createInitialData.onlyFromResources.description")}
          </p>
          {!onlyFromResources && (
            <div className="flex items-start gap-2 mt-3 ml-7 p-2 bg-warning-50 rounded text-warning-600">
              <WarningOutlined className="text-sm mt-0.5 flex-shrink-0" />
              <p className="text-xs">
                {t("dataCard.action.createInitialData.onlyFromResources.warning")}
              </p>
            </div>
          )}
        </div>

        {/* Per-property null control */}
        {identityProperties.length > 1 && (
          <div className="border border-default-200 rounded-lg p-4">
            <p className="text-sm font-medium mb-1">
              {t("dataCard.action.createInitialData.allowNull")}
            </p>
            <p className="text-xs text-default-400 mb-3">
              {t("dataCard.action.createInitialData.allowNull.description")}
            </p>
            <div className="flex flex-col gap-2">
              {identityProperties.map((p) => (
                <Checkbox
                  key={p.id}
                  size="sm"
                  isSelected={allowNullPropertyIds.has(p.id)}
                  onValueChange={() => toggleAllowNull(p.id)}
                >
                  <span className="text-sm">{p.name}</span>
                </Checkbox>
              ))}
            </div>
          </div>
        )}

        {/* Preview / estimation */}
        <div className="border border-default-200 rounded-lg p-3 bg-default-50 min-h-[48px] flex items-center">
          {previewing ? (
            <div className="flex items-center gap-2 text-default-500 text-sm">
              <LoadingOutlined />
              <span>{t("dataCard.action.createInitialData.preview.loading")}</span>
            </div>
          ) : preview ? (
            <div className="flex flex-col gap-1">
              <p className="text-sm">
                {t("dataCard.action.createInitialData.preview.summary", {
                  toCreate: preview.toCreate,
                  alreadyExists: preview.alreadyExists,
                })}
              </p>
              {preview.toCreate === 0 && (
                <p className="text-xs text-default-500">
                  {t("dataCard.action.createInitialData.preview.nothingToCreate")}
                </p>
              )}
            </div>
          ) : (
            <p className="text-sm text-default-400">
              {t("dataCard.action.createInitialData.preview.failed")}
            </p>
          )}
        </div>
      </div>
    </Modal>
  );
};

export default CreateInitialDataModal;
