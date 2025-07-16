"use client";

import type { IProperty } from "./models";
import type { PropertyType } from "@/sdk/constants";

import { useTranslation } from "react-i18next";
import {
  DatabaseOutlined,
  DisconnectOutlined,
  LinkOutlined,
} from "@ant-design/icons";
import { AiOutlineDelete, AiOutlineEdit } from "react-icons/ai";

import { useBakabaseContext } from "../ContextProvider/BakabaseContextProvider";

import styles from "./index.module.scss";
import Label from "./components/Label";

import PropertyModal from "@/components/PropertyModal";
import {
  Button,
  Card,
  CardBody,
  Chip,
  Tooltip,
  Modal,
} from "@/components/bakaui";
import { PropertyPool } from "@/sdk/constants";
import PropertyPoolIcon from "@/components/Property/components/PropertyPoolIcon";
import PropertyTypeIcon from "@/components/Property/components/PropertyTypeIcon";
import BApi from "@/sdk/BApi.tsx";

type Props = {
  property: IProperty;
  onClick?: () => any;
  disabled?: boolean;

  hidePool?: boolean;
  hideType?: boolean;

  removable?: boolean;
  editable?: boolean;
  onSaved?: (property: IProperty) => any;
  onRemoved?: () => any;

  onDialogDestroyed?: () => any;
};

export { Label as PropertyLabel };

export default ({
  property,
  hidePool,
  hideType,
  onClick,
  onSaved,
  onRemoved,
  onDialogDestroyed,
  disabled,
  ...props
}: Props) => {
  const { t } = useTranslation();

  const { createPortal } = useBakabaseContext();

  const editable = property.pool == PropertyPool.Custom && props.editable;
  const removable = property.pool == PropertyPool.Custom && props.removable;

  const renderBottom = () => {
    if (property.pool != PropertyPool.Custom) {
      return null;
    }
    const categories = property.categories || [];

    return (
      <div className={`${styles.bottom} mt-1 pt-1 flex flex-wrap gap-2`}>
        {categories.length > 0 ? (
          <Tooltip
            content={
              <div className={"flex flex-wrap gap-1 max-w-[600px]"}>
                {categories.map((c) => {
                  return (
                    <Chip key={c.id} radius={"sm"} size={"sm"}>
                      {c.name}
                    </Chip>
                  );
                })}
              </div>
            }
            placement={"bottom"}
          >
            <div className={"flex gap-0.5 items-center"}>
              <LinkOutlined className={"text-sm"} />
              {categories.length}
            </div>
            {/* <Chip */}
            {/*   radius={'sm'} */}
            {/*   size={'sm'} */}
            {/*   classNames={{}} */}
            {/* >{t<string>('{{count}} categories', { count: categories.length })}</Chip> */}
          </Tooltip>
        ) : (
          <Tooltip
            content={
              <div>
                <div>{t<string>("No category bound")}</div>
                <div>
                  {t<string>("You can bind properties in category page")}
                </div>
              </div>
            }
            placement={"bottom"}
          >
            <DisconnectOutlined className={"text-sm"} />
          </Tooltip>
        )}
        {property.valueCount != undefined && (
          <Tooltip
            content={t<string>("{{count}} values", {
              count: property.valueCount,
            })}
            placement={"bottom"}
          >
            <div className={"flex gap-0.5 items-center"}>
              <DatabaseOutlined className={"text-sm"} />
              {property.valueCount}
            </div>
          </Tooltip>
        )}
      </div>
    );
  };

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

  if (editable) {
    actions.push(
      <Button
        isIconOnly
        size={"sm"}
        variant={"light"}
        onPress={(e) => {
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
            children: t<string>(
              "This operation can not be undone, are you sure?",
            ),
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

  const card = (
    <Card isPressable onPress={onClick}>
      <CardBody className={"flex flex-col gap-1"}>
        {(!hidePool || !hideType) && (
          <div className="flex items-center gap-1">
            {!hideType && <PropertyTypeIcon type={property.type} />}
            {!hidePool && <PropertyPoolIcon pool={property.pool} />}
          </div>
        )}
        <div className={"text-base text-left"}>{property.name}</div>
      </CardBody>
    </Card>
  );

  if (actions.length > 0) {
    return (
      <Tooltip
        content={<div className={"flex items-center gap-1"}>{actions}</div>}
      >
        {card}
      </Tooltip>
    );
  }

  return card;
};
