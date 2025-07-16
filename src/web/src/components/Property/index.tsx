"use client";

import type { IProperty } from "./models";

import { useTranslation } from "react-i18next";
import { useState } from "react";
import {
  DatabaseOutlined,
  DisconnectOutlined,
  LinkOutlined,
} from "@ant-design/icons";
import { MdEdit, MdDelete } from "react-icons/md";

import { useBakabaseContext } from "../ContextProvider/BakabaseContextProvider";

import styles from "./index.module.scss";
import Label from "./components/Label";

import PropertyModal from "@/components/PropertyModal";
import { Button, Chip, Modal, Tooltip } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { PropertyPool, PropertyType } from "@/sdk/constants";
import PropertyTypeIcon from "@/components/Property/components/PropertyTypeIcon";

type Props = {
  property: IProperty;
  onClick?: () => any;
  disabled?: boolean;

  removable?: boolean;
  editable?: boolean;
  editablePortal?: "click" | "edit-icon";
  onSaved?: (property: IProperty) => any;
  onRemoved?: () => any;

  onDialogDestroyed?: () => any;
};

export { Label as PropertyLabel };

export default ({
  property,
  onClick,
  editablePortal = "edit-icon",
  onSaved,
  onRemoved,
  onDialogDestroyed,
  disabled,
  ...props
}: Props) => {
  const { t } = useTranslation();

  const [removeConfirmingDialogVisible, setRemoveConfirmingDialogVisible] =
    useState(false);
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

  return (
    <div
      key={property.id}
      className={`${styles.property} group px-2 py-1 rounded ${disabled ? "cursor-not-allowed opacity-60" : "cursor-pointer hover:bg-[var(--bakaui-overlap-background)]"}`}
      onClick={() => {
        if (disabled) {
          return;
        }
        if (editable && editablePortal == "click") {
          showDetail();
        }
        onClick?.();
      }}
    >
      <Modal
        title={t<string>("Delete a property")}
        visible={removeConfirmingDialogVisible}
        onClose={() => setRemoveConfirmingDialogVisible(false)}
        onOk={async () => {
          await BApi.customProperty.removeCustomProperty(property.id);
          onRemoved?.();
        }}
      >
        {t<string>("This operation can not be undone, are you sure?")}
      </Modal>
      <div className={`${styles.line1} flex item-center justify-between gap-1`}>
        <div className={`${styles.left}`}>
          <div className={"flex items-center gap-1"}>
            <Chip radius={"sm"} size={"sm"}>
              {t<string>(`${PropertyPool[property.pool]}`)}
            </Chip>
            {property.name}
          </div>
          <Tooltip
            color={"foreground"}
            content={t<string>(PropertyType[property.type!])}
          >
            <div className={styles.type}>
              <PropertyTypeIcon textVariant={"none"} type={property.type} />
            </div>
          </Tooltip>
        </div>
        {property.pool == PropertyPool.Custom && (
          <div
            className={
              "ml-1 flex gap-0.5 items-center invisible group-hover:visible"
            }
          >
            {editable && editablePortal == "edit-icon" && (
              <Button
                isIconOnly
                className={"text-base"}
                color={"default"}
                size={"sm"}
                variant={"light"}
                onPress={() => {
                  showDetail();
                }}
              >
                <MdEdit className={"text-base"} />
              </Button>
            )}
            {removable && (
              <Button
                isIconOnly
                className={"text-base"}
                color={"danger"}
                size={"sm"}
                variant={"light"}
                onPress={async () => {
                  setRemoveConfirmingDialogVisible(true);
                }}
              >
                <MdDelete className={"text-base"} />
              </Button>
            )}
          </div>
        )}
      </div>
      {renderBottom()}
    </div>
  );
};
