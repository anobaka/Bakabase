"use client";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { DeleteOutlined, EditOutlined, LayoutOutlined, PlusCircleOutlined, WarningOutlined } from "@ant-design/icons";
import toast from "react-hot-toast";

import BApi from "@/sdk/BApi";
import {
  Button,
  Card,
  CardBody,
  CardHeader,
  Divider,
  Modal,
  Tooltip,
} from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import type { IProperty } from "@/components/Property/models";
import { CustomPropertyAdditionalItem } from "@/sdk/constants";

import DataCardTypeEditor from "./components/DataCardTypeEditor";
import DataCardList from "./components/DataCardList";
import DisplayTemplateDesigner from "./components/DisplayTemplateDesigner";

interface DataCardType {
  id: number;
  name: string;
  propertyIds?: number[];
  identityPropertyIds?: number[];
  nameTemplate?: string;
  matchRules?: any;
  displayTemplate?: any;
  order: number;
  createdAt: string;
  updatedAt: string;
}

const DataCardPage = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [cardTypes, setCardTypes] = useState<DataCardType[]>([]);
  const [allProperties, setAllProperties] = useState<IProperty[]>([]);
  const [selectedTypeId, setSelectedTypeId] = useState<number | undefined>();

  const loadCardTypes = async () => {
    const rsp = await BApi.dataCardType.getAllDataCardTypes();
    const types = (rsp.data || []) as DataCardType[];
    setCardTypes(types);
    if (types.length > 0 && !selectedTypeId) {
      setSelectedTypeId(types[0].id);
    }
  };

  const loadProperties = async () => {
    // @ts-ignore
    const rsp = await BApi.customProperty.getAllCustomProperties({
      additionalItems: CustomPropertyAdditionalItem.None,
    });
    // @ts-ignore
    setAllProperties((rsp.data || []) as IProperty[]);
  };

  useEffect(() => {
    loadCardTypes();
    loadProperties();
  }, []);

  const handleDeleteType = (typeId: number) => {
    createPortal(Modal, {
      defaultVisible: true,
      title: t("common.action.delete"),
      children: t("dataCard.type.delete.confirm"),
      onOk: async () => {
        await BApi.dataCardType.deleteDataCardType(typeId);
        toast.success(t("common.success.deleted"));
        if (selectedTypeId === typeId) {
          setSelectedTypeId(undefined);
        }
        await loadCardTypes();
      },
    });
  };

  const selectedType = cardTypes.find((ct) => ct.id === selectedTypeId);

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between">
        <h2 className="text-xl font-semibold">{t("dataCard.title")}</h2>
      </div>

      <div className="flex gap-4">
        {/* Left: Card Type List */}
        <div className="w-80 flex-shrink-0">
          <Card>
            <CardHeader className="flex items-center justify-between">
              <span className="font-semibold">{t("dataCard.type.title")}</span>
              <Button
                size="sm"
                color="primary"
                variant="light"
                isIconOnly
                onPress={() => {
                  createPortal(DataCardTypeEditor, {
                    allProperties,
                    onSaved: loadCardTypes,
                  });
                }}
              >
                <PlusCircleOutlined className="text-lg" />
              </Button>
            </CardHeader>
            <Divider />
            <CardBody className="p-0">
              {cardTypes.length === 0 ? (
                <div className="p-4 text-center text-default-400 text-sm">
                  {t("dataCard.card.noCards")}
                </div>
              ) : (
                <div className="flex flex-col">
                  {cardTypes.map((ct) => (
                    <div
                      key={ct.id}
                      className={`flex items-center justify-between px-4 py-3 cursor-pointer hover:bg-default-100 transition-colors ${
                        selectedTypeId === ct.id ? "bg-primary-50" : ""
                      }`}
                      onClick={() => setSelectedTypeId(ct.id)}
                    >
                      <div className="flex items-center gap-1.5 min-w-0">
                        <span className="text-sm truncate">{ct.name}</span>
                        {!ct.matchRules?.autoBindEnabled && (
                          <Tooltip content={t<string>("dataCard.matchRules.autoBindDisabled.tooltip")}>
                            <span
                              className="inline-flex items-center flex-shrink-0"
                              style={{ color: "hsl(var(--heroui-warning))" }}
                            >
                              <WarningOutlined className="text-base" />
                            </span>
                          </Tooltip>
                        )}
                      </div>
                      <div className="flex gap-1">
                        <Button
                          size="sm"
                          variant="light"
                          isIconOnly
                          onPress={() => {
                            createPortal(DataCardTypeEditor, {
                              cardType: ct,
                              allProperties,
                              onSaved: loadCardTypes,
                            });
                          }}
                        >
                          <EditOutlined className="text-lg" />
                        </Button>
                        <Button
                          size="sm"
                          variant="light"
                          isIconOnly
                          onPress={() => {
                            createPortal(DisplayTemplateDesigner, {
                              cardTypeId: ct.id,
                              propertyIds: ct.propertyIds || [],
                              allProperties,
                              displayTemplate: ct.displayTemplate,
                              onSaved: loadCardTypes,
                            });
                          }}
                        >
                          <LayoutOutlined className="text-lg" />
                        </Button>
                        <Button
                          size="sm"
                          variant="light"
                          color="danger"
                          isIconOnly
                          onPress={() => handleDeleteType(ct.id)}
                        >
                          <DeleteOutlined className="text-lg" />
                        </Button>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </CardBody>
          </Card>
        </div>

        {/* Right: Card List for Selected Type */}
        <div className="flex-1">
          {selectedType ? (
            <DataCardList
              cardType={selectedType}
              allProperties={allProperties}
            />
          ) : (
            <Card>
              <CardBody>
                <div className="text-center text-default-400 py-8">
                  {t("dataCard.card.noCards")}
                </div>
              </CardBody>
            </Card>
          )}
        </div>
      </div>
    </div>
  );
};

DataCardPage.displayName = "DataCardPage";

export default DataCardPage;
