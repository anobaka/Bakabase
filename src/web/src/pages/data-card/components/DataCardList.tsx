"use client";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { DatabaseOutlined, DeleteOutlined, EditOutlined, PlusCircleOutlined, SearchOutlined } from "@ant-design/icons";
import toast from "react-hot-toast";

import BApi from "@/sdk/BApi";
import {
  Button,
  Card,
  CardBody,
  CardHeader,
  Chip,
  Divider,
  Input,
  Modal,
  Pagination,
  Tooltip,
} from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import type { IProperty } from "@/components/Property/models";

import DataCardBody from "@/components/DataCardBody";
import { useSearchByDataCard } from "@/hooks/useSearchByDataCard";

import DataCardEditor from "./DataCardEditor";
import CreateInitialDataModal from "./CreateInitialDataModal";

interface DataCardType {
  id: number;
  name: string;
  propertyIds?: number[];
  identityPropertyIds?: number[];
  nameTemplate?: string;
  matchRules?: any;
  displayTemplate?: any;
}

interface DataCardItem {
  id: number;
  typeId: number;
  name?: string;
  propertyValues?: { id: number; cardId: number; propertyId: number; value?: string; scope: number }[];
  createdAt: string;
  updatedAt: string;
}

interface DataCardListProps {
  cardType: DataCardType;
  allProperties: IProperty[];
}

const DataCardList = ({ cardType, allProperties }: DataCardListProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const { canSearch, triggerSearch } = useSearchByDataCard();
  const [cards, setCards] = useState<DataCardItem[]>([]);
  const [keyword, setKeyword] = useState("");
  const [pageIndex, setPageIndex] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const pageSize = 20;

  const typeProperties = allProperties.filter(
    (p) => cardType.propertyIds?.includes(p.id!) ?? false,
  );

  const loadCards = async (page?: number, kw?: string) => {
    const rsp = await BApi.dataCard.searchDataCards({
      typeId: cardType.id,
      keyword: (kw ?? keyword) || undefined,
      pageIndex: page ?? pageIndex,
      pageSize,
    });
    setCards((rsp.data || []) as DataCardItem[]);
    setTotalCount((rsp as any).totalCount || 0);
  };

  const handleSearch = () => {
    setPageIndex(1);
    loadCards(1);
  };

  useEffect(() => {
    setKeyword("");
    setPageIndex(1);
    loadCards(1, "");
  }, [cardType.id]);

  const handleDelete = (cardId: number) => {
    createPortal(Modal, {
      defaultVisible: true,
      title: t("common.action.delete"),
      children: t("dataCard.card.delete.confirm"),
      onOk: async () => {
        await BApi.dataCard.deleteDataCard(cardId);
        toast.success(t("common.success.deleted"));
        await loadCards();
      },
    });
  };

  const totalPages = Math.ceil(totalCount / pageSize);

  return (
    <Card>
      <CardHeader className="flex items-center justify-between px-4 py-3 gap-2">
        <div className="flex items-center gap-2 flex-shrink-0">
          <span className="font-semibold whitespace-nowrap">
            {cardType.name} - {t("dataCard.card.title")}
            <span className="text-default-400 text-sm ml-2">({totalCount})</span>
          </span>
        </div>
        <div className="flex items-center gap-2 flex-wrap justify-end">
          <Input
            size="sm"
            className="w-40"
            placeholder={t("dataCard.card.search.placeholder")}
            value={keyword}
            onValueChange={setKeyword}
            onKeyDown={(e) => {
              if (e.key === "Enter") {
                handleSearch();
              }
            }}
            endContent={
              <Button
                size="sm"
                variant="light"
                isIconOnly
                onPress={handleSearch}
              >
                <SearchOutlined className="text-small" />
              </Button>
            }
          />
          <Button
            size="sm"
            color="primary"
            className="whitespace-nowrap"
            onPress={() => {
              createPortal(DataCardEditor, {
                cardType,
                allProperties: typeProperties,
                onSaved: () => loadCards(),
              });
            }}
          >
            <PlusCircleOutlined className="text-lg" />
            {t("dataCard.card.create")}
          </Button>
          <Button
            size="sm"
            color="secondary"
            variant="flat"
            className="whitespace-nowrap"
            onPress={() => {
              createPortal(CreateInitialDataModal, {
                typeId: cardType.id,
                typeName: cardType.name,
                onCreated: () => loadCards(),
              });
            }}
          >
            <DatabaseOutlined className="text-lg" />
            {t("dataCard.action.createInitialData")}
          </Button>
          <Button
            size="sm"
            color="danger"
            variant="light"
            className="whitespace-nowrap"
            isDisabled={totalCount === 0}
            onPress={() => {
              createPortal(Modal, {
                defaultVisible: true,
                title: t("dataCard.action.deleteAll"),
                children: t("dataCard.action.deleteAll.confirm"),
                onOk: async () => {
                  // @ts-ignore - SDK will update after server restart
                  await BApi.dataCard.deleteDataCardsByType(cardType.id);
                  toast.success(t("common.success.deleted"));
                  await loadCards();
                },
              });
            }}
          >
            <DeleteOutlined className="text-lg" />
            {t("dataCard.action.deleteAll")}
          </Button>
        </div>
      </CardHeader>
      <Divider />
      <CardBody>
        {cards.length === 0 ? (
          <div className="text-center text-default-400 py-8">
            {t("dataCard.card.noCards")}
          </div>
        ) : (
          <>
            <div
              className="grid gap-3"
              style={{ gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))" }}
            >
              {cards.map((card) => {
                const searchEnabled = canSearch(card, cardType, allProperties);
                return (
                <Card key={card.id} className="border border-default-200">
                  <CardBody className="p-3">
                    <div className="flex items-start justify-between mb-2">
                      <span className="font-medium text-sm truncate">
                        {card.name || "-"}
                      </span>
                      <div className="flex gap-1 flex-shrink-0">
                        <Tooltip
                          content={
                            searchEnabled
                              ? t<string>("dataCard.search.button.tooltip")
                              : t<string>("dataCard.search.disabled.tooltip")
                          }
                        >
                          <span>
                            <Button
                              size="sm"
                              variant="light"
                              isIconOnly
                              isDisabled={!searchEnabled}
                              onPress={() =>
                                triggerSearch(card, cardType, allProperties)
                              }
                            >
                              <SearchOutlined className="text-lg" />
                            </Button>
                          </span>
                        </Tooltip>
                        <Button
                          size="sm"
                          variant="light"
                          isIconOnly
                          onPress={() => {
                            createPortal(DataCardEditor, {
                              card,
                              cardType,
                              allProperties: typeProperties,
                              onSaved: () => loadCards(),
                            });
                          }}
                        >
                          <EditOutlined className="text-lg" />
                        </Button>
                        <Button
                          size="sm"
                          variant="light"
                          color="danger"
                          isIconOnly
                          onPress={() => handleDelete(card.id)}
                        >
                          <DeleteOutlined className="text-lg" />
                        </Button>
                      </div>
                    </div>
                    <DataCardBody
                      allProperties={allProperties}
                      card={card}
                      cardType={cardType}
                    />
                  </CardBody>
                </Card>
                );
              })}
            </div>

            {totalPages > 1 && (
              <div className="flex justify-center mt-4">
                <Pagination
                  total={totalPages}
                  page={pageIndex}
                  onChange={(page) => {
                    setPageIndex(page);
                    loadCards(page);
                  }}
                />
              </div>
            )}
          </>
        )}
      </CardBody>
    </Card>
  );
};

export default DataCardList;
