"use client";

import type { IProperty } from "./models";
import type { PropertyType } from "@/sdk/constants";

import { useTranslation } from "react-i18next";
import { AiOutlineDelete, AiOutlineEdit, AiOutlineCheck } from "react-icons/ai";

import { useBakabaseContext } from "../ContextProvider/BakabaseContextProvider";

import Label from "./components/Label";
import PropertyExtra from "./components/PropertyExtra";

import PropertyModal from "@/components/PropertyModal";
import { Button, Card, CardBody, Tooltip, Modal } from "@/components/bakaui";
import { PropertyPool } from "@/sdk/constants";
import PropertyPoolIcon from "@/components/Property/components/PropertyPoolIcon";
import PropertyTypeIcon from "@/components/Property/components/PropertyTypeIcon";
import BApi from "@/sdk/BApi.tsx";

type Props = {
  property: IProperty;
  onClick?: () => any;
  disabled?: boolean;

  isSelected?: boolean;

  hidePool?: boolean;
  hideType?: boolean;

  removable?: boolean;
  editable?: boolean;
  openEditModalOnClick?: boolean;
  onSaved?: (property: IProperty) => any;
  onRemoved?: () => any;

  onDialogDestroyed?: () => any;
};

export { Label as PropertyLabel };
const V2 = ({
  property,
  hidePool,
  hideType,
  onClick,
  onSaved,
  onRemoved,
  onDialogDestroyed,
  isSelected,
  disabled,
  openEditModalOnClick,
  ...props
}: Props) => {
  const { t } = useTranslation();

  const { createPortal } = useBakabaseContext();

  const editable = property.pool == PropertyPool.Custom && props.editable;
  const removable = property.pool == PropertyPool.Custom && props.removable;

  const selected = isSelected === true;

  const showDetail = () => {
    createPortal(PropertyModal, {
      value: {
        ...property,
        type: property.type as unknown as PropertyType,
      },
      onSaved: (p) =>
        onSaved?.({
          ...p,
        }),
      onDestroyed: onDialogDestroyed,
    });
  };

  const actions: any[] = [];

  if (editable && !openEditModalOnClick) {
    actions.push(
      <Button
        isIconOnly
        size={"sm"}
        variant={"light"}
        onPress={() => {
          showDetail();
        }}
      >
        <AiOutlineEdit className={"text-base"} />
      </Button>,
    );
  }
  if (removable) {
    actions.push(
      <Button
        isIconOnly
        color={"danger"}
        size={"sm"}
        variant={"light"}
        onPress={() => {
          createPortal(Modal, {
            defaultVisible: true,
            title: t<string>("Delete a property"),
            children: t<string>("This operation can not be undone, are you sure?"),
            onOk: async () => {
              await BApi.customProperty.removeCustomProperty(property.id);
              onRemoved?.();
            },
          });
        }}
      >
        <AiOutlineDelete className={"text-base"} />
      </Button>,
    );
  }

  const pressHandler = openEditModalOnClick && editable ? showDetail : onClick;

  const card = (
    <Card
      className={disabled ? "cursor-not-allowed opacity-60" : ""}
      isPressable={!disabled}
      onPress={pressHandler}
    >
      <CardBody className={"flex flex-col gap-1"}>
        {(!hidePool || !hideType) && (
          <div className="flex items-center gap-1">
            {!hideType && <PropertyTypeIcon type={property.type} />}
            {!hidePool && <PropertyPoolIcon pool={property.pool} />}
          </div>
        )}
        <div className={"flex items-center gap-1"}>
          <div className={"text-base text-left"}>{property.name}</div>
          <PropertyExtra property={property} />
        </div>
      </CardBody>
    </Card>
  );

  const cardWithSelection = (
    <div
      aria-selected={selected}
      className={`relative ${selected ? "ring-2 ring-green-500 rounded-lg" : ""}`}
    >
      {selected && (
        <div className="pointer-events-none absolute -top-1.5 -right-1.5 bg-green-500 text-white rounded-full w-5 h-5 flex items-center justify-center text-xs shadow z-1">
          <AiOutlineCheck />
        </div>
      )}
      {card}
    </div>
  );

  if (actions.length > 0) {
    return (
      <Tooltip content={<div className={"flex items-center gap-1"}>{actions}</div>}>
        {cardWithSelection}
      </Tooltip>
    );
  }

  return cardWithSelection;
};

V2.displayName = "V2";

export default V2;
