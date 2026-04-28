"use client";

import React, { useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { WarningOutlined } from "@ant-design/icons";

import BApi from "@/sdk/BApi";
import { Button, Chip, Modal } from "@/components/bakaui";
import type { IProperty } from "@/components/Property/models";
import type { DestroyableProps } from "@/components/bakaui/types";
import PropertyValueRenderer from "@/components/Property/components/PropertyValueRenderer";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

interface CardType {
  id: number;
  name: string;
  propertyIds?: number[];
  identityPropertyIds?: number[];
  displayTemplate?: {
    cols?: number;
    rows?: number;
    layout?: { propertyId: number; x: number; y: number; w: number; h: number }[];
  };
}

interface DataCardItem {
  id: number;
  name?: string;
  propertyValues?: { propertyId: number; value?: string; scope: number }[];
}

interface DataCardEditorProps extends DestroyableProps {
  card?: DataCardItem;
  cardType: CardType;
  allProperties: IProperty[];
  onSaved?: () => void;
}

const DataCardEditor = ({
  card,
  cardType,
  allProperties,
  onSaved,
  onDestroyed,
}: DataCardEditorProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const isEditing = !!card;

  const [visible, setVisible] = useState(true);
  const [propertyValues, setPropertyValues] = useState<Record<number, string | undefined>>(
    () => {
      const values: Record<number, string | undefined> = {};
      if (card?.propertyValues) {
        for (const pv of card.propertyValues) {
          values[pv.propertyId] = pv.value || undefined;
        }
      }
      return values;
    },
  );

  const [duplicateCard, setDuplicateCard] = useState<DataCardItem | null>(null);

  const identityPropertyIds = useMemo(() => {
    if (cardType.identityPropertyIds?.length) return cardType.identityPropertyIds;
    return cardType.propertyIds || [];
  }, [cardType]);

  const checkTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  useEffect(() => {
    if (identityPropertyIds.length === 0) {
      setDuplicateCard(null);
      return;
    }

    if (checkTimerRef.current) clearTimeout(checkTimerRef.current);
    checkTimerRef.current = setTimeout(async () => {
      const pvList = identityPropertyIds.map((pid) => ({
        propertyId: pid,
        value: propertyValues[pid] ?? undefined,
        scope: 0,
      }));

      try {
        const rsp = await BApi.dataCard.findDataCardByIdentity({
          typeId: cardType.id,
          excludeCardId: card?.id,
          propertyValues: pvList,
        });
        const found = ((rsp.data as any) || null) as DataCardItem | null;
        setDuplicateCard(found && found.id === card?.id ? null : found);
      } catch {
        setDuplicateCard(null);
      }
    }, 300);

    return () => {
      if (checkTimerRef.current) clearTimeout(checkTimerRef.current);
    };
  }, [propertyValues, identityPropertyIds, cardType.id, card?.id]);

  const layoutByPropertyId = useMemo(() => {
    const map = new Map<number, { x: number; y: number; w: number; h: number }>();
    for (const item of cardType.displayTemplate?.layout ?? []) {
      map.set(item.propertyId, { x: item.x, y: item.y, w: item.w, h: item.h });
    }
    return map;
  }, [cardType.displayTemplate]);

  const cols = cardType.displayTemplate?.cols ?? 0;
  const rows = cardType.displayTemplate?.rows ?? 0;
  const hasLayout = cols > 0 && rows > 0 && layoutByPropertyId.size > 0;

  const placedProperties = hasLayout
    ? allProperties.filter((p) => layoutByPropertyId.has(p.id!))
    : [];
  const unplacedProperties = hasLayout
    ? allProperties.filter((p) => !layoutByPropertyId.has(p.id!))
    : allProperties;

  const handleSave = async () => {
    const pvList = Object.entries(propertyValues)
      .filter(([, value]) => value != null && value.trim() !== "")
      .map(([propertyId, value]) => ({
        propertyId: Number(propertyId),
        value: value!,
        scope: 0,
      }));

    if (isEditing) {
      await BApi.dataCard.updateDataCard(card!.id, {
        propertyValues: pvList,
      });
    } else {
      await BApi.dataCard.addDataCard({
        typeId: cardType.id,
        propertyValues: pvList,
      });
    }

    onSaved?.();
  };

  const handleSwitchToExisting = () => {
    if (!duplicateCard) return;
    const existing = duplicateCard;
    setVisible(false);
    // Defer opening so the current modal can close cleanly
    setTimeout(() => {
      createPortal(DataCardEditor, {
        card: existing,
        cardType,
        allProperties,
        onSaved,
      });
    }, 0);
  };

  const renderField = (p: IProperty, fillContainer = false) => (
    <div className="flex flex-col gap-1 min-w-0">
      <label className="text-sm text-default-600">{p.name}</label>
      <div className="min-w-0">
        <PropertyValueRenderer
          property={p}
          dbValue={propertyValues[p.id!] ?? undefined}
          size={fillContainer ? "md" : "sm"}
          attachmentPropertyValueRendererProps={
            fillContainer ? { fill: true } : undefined
          }
          isEditing
          onValueChange={(dbValue) => {
            setPropertyValues((prev) => ({
              ...prev,
              [p.id!]: dbValue,
            }));
          }}
        />
      </div>
    </div>
  );

  return (
    <Modal
      visible={visible}
      size="lg"
      title={
        <span className="inline-flex items-center gap-2">
          <span>
            {`${isEditing ? t("dataCard.card.edit") : t("dataCard.card.create")} - ${cardType.name}`}
          </span>
          {isEditing && card && (
            <Chip size="sm" variant="flat">{`#${card.id}`}</Chip>
          )}
        </span>
      }
      onClose={() => setVisible(false)}
      onOk={handleSave}
      okProps={{ isDisabled: !!duplicateCard }}
      onDestroyed={onDestroyed}
    >
      <div className="flex flex-col gap-4">
        {duplicateCard && (
          <div className="flex items-start gap-2 p-3 bg-warning-50 rounded border border-warning-200">
            <WarningOutlined className="text-warning-600 text-base mt-0.5 flex-shrink-0" />
            <div className="flex-1 flex flex-col gap-2">
              <span className="text-sm text-warning-700">
                {t("dataCard.card.duplicate.message")}
              </span>
              <div>
                <Button
                  size="sm"
                  color="warning"
                  variant="flat"
                  onPress={handleSwitchToExisting}
                >
                  {t("dataCard.card.duplicate.switch", { id: duplicateCard.id })}
                </Button>
              </div>
            </div>
          </div>
        )}

        {hasLayout && (
          <div
            className="grid gap-3"
            style={{
              gridTemplateColumns: `repeat(${cols}, minmax(0, 1fr))`,
              gridAutoRows: "minmax(40px, auto)",
            }}
          >
            {placedProperties.map((p) => {
              const pos = layoutByPropertyId.get(p.id!)!;
              return (
                <div
                  key={p.id}
                  style={{
                    gridColumn: `${pos.x + 1} / span ${pos.w}`,
                    gridRow: `${pos.y + 1} / span ${pos.h}`,
                  }}
                >
                  {renderField(p, true)}
                </div>
              );
            })}
          </div>
        )}

        {unplacedProperties.length > 0 && (
          <div className="flex flex-col gap-4">
            {unplacedProperties.map((p) => (
              <React.Fragment key={p.id}>{renderField(p)}</React.Fragment>
            ))}
          </div>
        )}
      </div>
    </Modal>
  );
};

export default DataCardEditor;
