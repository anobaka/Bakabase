"use client";

import type { PropertyType } from "@/sdk/constants";
import type { IProperty } from "@/components/Property/models";

import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";
import { SearchOutlined, SortAscendingOutlined } from "@ant-design/icons";

import PropertyModal from "@/components/PropertyModal";
import BApi from "@/sdk/BApi";
import { CustomPropertyAdditionalItem } from "@/sdk/constants";
import { Button, Input } from "@/components/bakaui";
import Property from "@/components/Property/v2";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import TypeConversionRuleOverviewDialog from "@/pages/custom-property/components/TypeConversionRuleOverviewDialog";
import CustomPropertySortModal from "@/components/CustomPropertySortModal";
import PropertyTypeIcon from "@/components/Property/components/PropertyTypeIcon";
const CustomPropertyPage = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [properties, setProperties] = useState<IProperty[]>([]);

  const [keyword, setKeyword] = useState("");

  const loadProperties = async () => {
    // @ts-ignore
    const rsp = await BApi.customProperty.getAllCustomProperties({
      additionalItems:
        CustomPropertyAdditionalItem.Category |
        CustomPropertyAdditionalItem.ValueCount,
    });

    // @ts-ignore
    setProperties((rsp.data || []).sort((a, b) => a.order - b.order));
  };

  useEffect(() => {
    loadProperties();
    // createPortal(TypeConversionOverviewDialog, {});
  }, []);

  const filteredProperties = properties.filter(
    (p) =>
      keyword == undefined ||
      keyword.length == 0 ||
      p.name!.toLowerCase().includes(keyword.toLowerCase()),
  );
  const groupedFilteredProperties: { [key in PropertyType]?: IProperty[] } =
    filteredProperties.reduce<{ [key in PropertyType]?: IProperty[] }>(
      (s, t) => {
        (s[t.type!] ??= []).push(t);

        return s;
      },
      {},
    );

  console.log("[CustomProperty] render", groupedFilteredProperties);

  return (
    <div>
      <div className={"flex items-center justify-between gap-2 mb-4"}>
        <div className={"flex items-center gap-2"}>
          <Button
            color={"primary"}
            size={"sm"}
            onPress={() => {
              createPortal(PropertyModal, {
                onSaved: loadProperties,
              });
            }}
          >
            {t<string>("Add")}
          </Button>
          <div>
            <Input
              size={"sm"}
              startContent={<SearchOutlined className={"text-small"} />}
              value={keyword}
              onValueChange={(v) => setKeyword(v)}
            />
          </div>
          <Button
            color={"secondary"}
            size={"sm"}
            variant={"light"}
            onPress={async () => {
              createPortal(TypeConversionRuleOverviewDialog, {});
            }}
          >
            {t<string>("Check type conversion rules")}
          </Button>
          <Button
            color={"default"}
            size={"sm"}
            variant={"light"}
            onPress={async () => {
              createPortal(CustomPropertySortModal, {
                properties,
                onDestroyed: loadProperties,
              });
            }}
          >
            <SortAscendingOutlined className={"text-base"} />
            {t<string>("Adjust display orders")}
          </Button>
        </div>
        <div />
      </div>
      <div className={"grid gap-4 grid-cols-1 lg:grid-cols-2 xl:grid-cols-3"}>
        {(Object.keys(groupedFilteredProperties) as unknown as PropertyType[]).map((k) => {
          const ps = groupedFilteredProperties[k]!.sort(
            (a: IProperty, b: IProperty) => a.order! - b.order!,
          );

          return (
            <div
              key={k}
              className={"rounded-lg border border-default-200 bg-content1 overflow-hidden"}
            >
              <div className={"flex items-center gap-2 px-4 py-3 bg-default-100 border-b border-default-200"}>
                <PropertyTypeIcon textVariant={"default"} type={ps[0].type} />
                <span className={"text-default-500 text-sm"}>({ps.length})</span>
              </div>
              <div className={"p-4"}>
                <div className={"flex flex-wrap gap-2"}>
                  {ps.map((p) => {
                    return (
                      <Property
                        key={p.id}
                        editable
                        hidePool
                        hideType
                        removable
                        property={p}
                        onDialogDestroyed={loadProperties}
                        onRemoved={() => {
                          loadProperties();
                        }}
                        onSaved={loadProperties}
                      />
                    );
                  })}
                </div>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
};

CustomPropertyPage.displayName = "CustomPropertyPage";

export default CustomPropertyPage;
