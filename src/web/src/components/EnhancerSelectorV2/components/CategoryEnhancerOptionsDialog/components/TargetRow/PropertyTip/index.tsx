"use client";

import type { ReactElement } from "react";
import type { IProperty } from "@/components/Property/models";

import { ExclamationCircleOutlined } from "@ant-design/icons";
import { useTranslation } from "react-i18next";
import React from "react";

import { Button, Modal, Tooltip } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { PropertyType, PropertyPool } from "@/sdk/constants";

interface IProps {
  // onAllowAddingNewDataDynamicallyEnabled?: () => any;
  onPropertyBoundToCategory?: () => any;
  property: IProperty;
  category: { name: string; id: number; customPropertyIds?: number[] };
}

const PropertyTypesWithDynamicData = [
  PropertyType.Multilevel,
  PropertyType.MultipleChoice,
  PropertyType.SingleChoice,
  PropertyType.Tags,
];
const PropertyTip = ({
  // onAllowAddingNewDataDynamicallyEnabled,
  onPropertyBoundToCategory,
  property,
  category,
}: IProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const renderTips = () => {
    const tips: ReactElement[] = [];
    // const propertyChoiceOptions = property?.options as {allowAddingNewDataDynamically: boolean};
    // const allowAddingNewDataDynamicallyDisabled = property &&
    //   PropertyTypesWithDynamicData.includes(property.type!) &&
    //   !propertyChoiceOptions.allowAddingNewDataDynamically;
    // if (allowAddingNewDataDynamicallyDisabled) {
    //   tips.push(
    //     <div className={'flex items-center gap-1'} key={1}>
    //       {t<string>('Adding new data dynamically is disabled for this property, new data will not be saved.')}
    //       <Button
    //         size={'sm'}
    //         color={'primary'}
    //         variant={'light'}
    //         onClick={() => {
    //           createPortal(Modal, {
    //             title: t<string>('Allow adding new data dynamically'),
    //             defaultVisible: true,
    //             onOk: async () => {
    //               await BApi.customProperty.enableAddingNewDataDynamicallyForCustomProperty(property.id);
    //               onAllowAddingNewDataDynamicallyEnabled?.();
    //             },
    //           });
    //         }}
    //       >
    //         {t<string>('Click to enable')}
    //       </Button>
    //     </div>,
    //   );
    // }

    if (
      category.customPropertyIds?.includes(property.id) != true &&
      property.pool == PropertyPool.Custom
    ) {
      tips.push(
        <div key={2} className={"flex items-center gap-1"}>
          {t<string>(
            "This property is not bound to the category, its data will not be displayed.",
          )}
          <Button
            color={"primary"}
            size={"sm"}
            variant={"light"}
            onClick={() => {
              createPortal(Modal, {
                title: t<string>(
                  "Bind property {{propertyName}} to category {{categoryName}}",
                  {
                    propertyName: property.name,
                    categoryName: category.name,
                  },
                ),
                defaultVisible: true,
                onOk: async () => {
                  await BApi.category.bindCustomPropertyToCategory(
                    category.id,
                    property.id,
                  );
                  onPropertyBoundToCategory?.();
                },
              });
            }}
          >
            {t<string>("Bind now")}
          </Button>
        </div>,
      );
    }

    return tips;
  };

  const tips = renderTips();

  if (tips.length) {
    return (
      <Tooltip content={<div className={"flex flex-col gap-1"}>{tips}</div>}>
        <ExclamationCircleOutlined
          className={"text-small cursor-pointer"}
          style={{ color: "var(--bakaui-warning)" }}
        />
      </Tooltip>
    );
  }

  return null;
};

PropertyTip.displayName = "PropertyTip";

export default PropertyTip;
