"use client";

import React, { useCallback, useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { EditOutlined, LayoutOutlined, SearchOutlined } from "@ant-design/icons";

import BApi from "@/sdk/BApi";
import {
  Accordion,
  AccordionItem,
  Button,
  Card,
  CardBody,
  Chip,
  Spinner,
  Tooltip,
} from "@/components/bakaui";
import type { IProperty } from "@/components/Property/models";
import DataCardBody, { computeFixedOuterWidth } from "@/components/DataCardBody";
import { CustomPropertyAdditionalItem } from "@/sdk/constants";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { useSearchByDataCard } from "@/hooks/useSearchByDataCard";
import DataCardEditor from "@/pages/data-card/components/DataCardEditor";
import DataCardTypeEditor from "@/pages/data-card/components/DataCardTypeEditor";
import DisplayTemplateDesigner from "@/pages/data-card/components/DisplayTemplateDesigner";

interface LayoutItem {
  propertyId: number;
  x: number;
  y: number;
  w: number;
  h: number;
  hideLabel?: boolean;
  hideEmpty?: boolean;
}

interface CardType {
  id: number;
  name: string;
  order?: number;
  propertyIds?: number[];
  identityPropertyIds?: number[];
  nameTemplate?: string;
  matchRules?: any;
  displayTemplate?: { cols?: number; rows?: number; layout?: LayoutItem[] };
}

interface DataCardItem {
  id: number;
  typeId: number;
  name?: string;
  propertyValues?: { propertyId: number; value?: string; scope: number }[];
}

interface GroupedCards {
  typeId: number;
  type?: CardType;
  items: DataCardItem[];
}

interface DataCardAssociationPanelProps {
  resourceId: number;
  /**
   * Called right before navigating to the resource search page after the
   * user clicks the per-card search button and confirms. The host (e.g.
   * the resource detail modal) typically uses this to close itself, since
   * leaving it open would obscure the destination.
   */
  onBeforeNavigateToSearch?: () => void;
}

// Fixed per-cell pixel size for cards rendered in the detail modal. Each
// card's outer width = cols * CELL_PX + padding, which keeps cards at a
// stable WYSIWYG size regardless of how many are in a row.
const CELL_PX = 72;
// Fallback width used when a card type has no layout template.
const FALLBACK_CARD_WIDTH = 240;

const DataCardAssociationPanel = ({
  resourceId,
  onBeforeNavigateToSearch,
}: DataCardAssociationPanelProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const { canSearch, triggerSearch } = useSearchByDataCard();
  const [cards, setCards] = useState<DataCardItem[]>([]);
  const [cardTypes, setCardTypes] = useState<CardType[]>([]);
  const [loading, setLoading] = useState(true);
  const [allProperties, setAllProperties] = useState<IProperty[]>([]);

  const load = useCallback(async () => {
    try {
      const [cardsRsp, typesRsp, propsRsp] = await Promise.all([
        BApi.dataCard.getAssociatedDataCards(resourceId),
        BApi.dataCardType.getAllDataCardTypes(),
        // @ts-ignore
        BApi.customProperty.getAllCustomProperties({
          additionalItems: CustomPropertyAdditionalItem.None,
        }),
      ]);
      setCards((cardsRsp.data || []) as DataCardItem[]);
      setCardTypes((typesRsp.data || []) as CardType[]);
      // @ts-ignore
      setAllProperties((propsRsp.data || []) as IProperty[]);
    } finally {
      setLoading(false);
    }
  }, [resourceId]);

  useEffect(() => {
    setLoading(true);
    load();
  }, [load]);

  const grouped = useMemo<GroupedCards[]>(() => {
    const typeById = new Map(cardTypes.map((ct) => [ct.id, ct]));
    const bucket = new Map<number, DataCardItem[]>();
    for (const c of cards) {
      const arr = bucket.get(c.typeId);
      if (arr) {
        arr.push(c);
      } else {
        bucket.set(c.typeId, [c]);
      }
    }
    return Array.from(bucket.entries())
      .map(([typeId, items]) => ({
        typeId,
        type: typeById.get(typeId),
        items,
      }))
      .sort((a, b) => {
        const oa = a.type?.order ?? Number.MAX_SAFE_INTEGER;
        const ob = b.type?.order ?? Number.MAX_SAFE_INTEGER;
        if (oa !== ob) return oa - ob;
        return (a.type?.name ?? "").localeCompare(b.type?.name ?? "");
      });
  }, [cards, cardTypes]);

  const defaultExpandedKeys = useMemo(() => {
    if (grouped.length === 0) return [] as string[];
    return [String(grouped[0].typeId)];
  }, [grouped]);

  const openCardEditor = (card: DataCardItem, cardType: CardType) => {
    const typeProperties = allProperties.filter(
      (p) => cardType.propertyIds?.includes(p.id!) ?? false,
    );
    createPortal(DataCardEditor, {
      card,
      cardType,
      allProperties: typeProperties,
      onSaved: load,
    });
  };

  const openTypeEditor = (cardType: CardType) => {
    createPortal(DataCardTypeEditor, {
      cardType,
      allProperties,
      onSaved: load,
    });
  };

  const openLayoutDesigner = (cardType: CardType) => {
    createPortal(DisplayTemplateDesigner, {
      cardTypeId: cardType.id,
      propertyIds: cardType.propertyIds || [],
      allProperties,
      displayTemplate: cardType.displayTemplate,
      onSaved: load,
    });
  };

  const renderCard = (card: DataCardItem, cardType?: CardType) => {
    const outerWidth =
      (cardType && computeFixedOuterWidth(cardType, CELL_PX)) ??
      FALLBACK_CARD_WIDTH;

    const searchEnabled =
      !!cardType && canSearch(card, cardType, allProperties);

    return (
      <Card
        key={card.id}
        className="group relative shrink-0 border border-default-200"
        style={{ width: outerWidth, maxWidth: "100%" }}
      >
        <div className="absolute top-1 right-1 flex gap-1 opacity-0 group-hover:opacity-100 transition-opacity z-10">
          <Tooltip
            content={
              searchEnabled
                ? t<string>("dataCard.search.button.tooltip")
                : t<string>("dataCard.search.disabled.tooltip")
            }
          >
            <span>
              <Button
                isIconOnly
                className="bg-background/80 backdrop-blur"
                isDisabled={!searchEnabled}
                size="sm"
                variant="flat"
                onPress={() => {
                  if (cardType) {
                    triggerSearch(card, cardType, allProperties, {
                      confirm: true,
                      beforeNavigate: onBeforeNavigateToSearch,
                    });
                  }
                }}
              >
                <SearchOutlined className="text-sm" />
              </Button>
            </span>
          </Tooltip>
          <Tooltip content={t<string>("common.action.edit")}>
            <Button
              isIconOnly
              className="bg-background/80 backdrop-blur"
              isDisabled={!cardType}
              size="sm"
              variant="flat"
              onPress={() => {
                if (cardType) openCardEditor(card, cardType);
              }}
            >
              <EditOutlined className="text-sm" />
            </Button>
          </Tooltip>
        </div>
        <CardBody className="p-3">
          {card.name && (
            <div className="font-medium text-sm mb-2 truncate">{card.name}</div>
          )}
          {cardType && (
            <DataCardBody
              allProperties={allProperties}
              card={card}
              cardType={cardType}
              cellPx={CELL_PX}
            />
          )}
        </CardBody>
      </Card>
    );
  };

  if (loading) {
    return (
      <div className="flex items-center gap-2 py-4">
        <Spinner size="sm" />
        <span className="text-sm text-default-400">
          {t("dataCard.association.loading")}
        </span>
      </div>
    );
  }

  if (cards.length === 0) {
    return null;
  }

  return (
    <div className="flex flex-col gap-2">
      <div className="text-sm font-medium flex items-center gap-2">
        <span>{t("dataCard.association.title")}</span>
        <Chip size="sm" variant="flat">
          {cards.length}
        </Chip>
      </div>
      <Accordion
        defaultExpandedKeys={defaultExpandedKeys}
        itemClasses={{
          trigger: "py-2",
          title: "text-sm",
          content: "pt-0 pb-2",
        }}
        selectionMode="multiple"
        variant="splitted"
      >
        {grouped.map(({ type, typeId, items }) => (
          <AccordionItem
            key={String(typeId)}
            aria-label={type?.name ?? `#${typeId}`}
            title={
              <div className="group flex items-center gap-2 pr-2">
                <span className="text-sm">{type?.name ?? `#${typeId}`}</span>
                <Chip size="sm" variant="flat">
                  {items.length}
                </Chip>
                {type && (
                  <div
                    className="ml-auto flex gap-1 opacity-0 group-hover:opacity-100 transition-opacity"
                    onClick={(e) => e.stopPropagation()}
                    onPointerDown={(e) => e.stopPropagation()}
                  >
                    <Tooltip content={t<string>("common.action.edit")}>
                      <Button
                        isIconOnly
                        size="sm"
                        variant="light"
                        onPress={() => openTypeEditor(type)}
                      >
                        <EditOutlined className="text-base" />
                      </Button>
                    </Tooltip>
                    <Tooltip content={t<string>("dataCard.type.displayTemplate")}>
                      <Button
                        isIconOnly
                        size="sm"
                        variant="light"
                        onPress={() => openLayoutDesigner(type)}
                      >
                        <LayoutOutlined className="text-base" />
                      </Button>
                    </Tooltip>
                  </div>
                )}
              </div>
            }
          >
            <div className="flex flex-wrap gap-2 pb-1">
              {items.map((card) => renderCard(card, type))}
            </div>
          </AccordionItem>
        ))}
      </Accordion>
    </div>
  );
};

export default DataCardAssociationPanel;
