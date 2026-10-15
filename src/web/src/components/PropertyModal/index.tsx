"use client";

import type { PropertyType } from "@/sdk/constants";
import type { DestroyableProps } from "@/components/bakaui/types";
import type { IProperty } from "@/components/Property/models";

import { useState } from "react";
import { useTranslation } from "react-i18next";

import ModalContent from "./components/ModalContent";

import "./index.scss";
import { useResourceApi, useResourceOwner } from "@/features/management/resourceContext";
import { Modal } from "@/components/bakaui";

type Props = {
  value?: CustomPropertyForm;
  onSaved?: (property: IProperty) => any;
  validValueTypes?: PropertyType[];
  /** Keep a foreign definition bound to its owner throughout editing. */
  saveProperty?: (model: CustomPropertyForm & { options: string }) => Promise<IProperty>;
  checkOptionUsage?: (propertyId: number, optionId: string) => Promise<number>;
  ownerScoped?: boolean;
} & DestroyableProps;

type CustomPropertyForm = {
  id?: number;
  name: string;
  type: PropertyType;
  options?: any;
};

const PropertyModal = ({
  value,
  onSaved,
  validValueTypes,
  saveProperty,
  checkOptionUsage,
  ownerScoped = false,
  ...props
}: Props) => {
  const { t } = useTranslation();
  const BApi = useResourceApi();
  const owner = useResourceOwner();
  const readOnly = !!owner && (!owner.available || owner.metadata?.denied);
  const [visible, setVisible] = useState(true);
  const [property, setProperty] = useState<CustomPropertyForm>();

  const close = () => {
    setVisible(false);
  };

  return (
    <Modal
      className="property-editor-dialog"
      footer={{
        actions: ["ok", "cancel"],
        okProps: {
          isDisabled: !property || readOnly,
          children: t<string>("property.editor.save"),
        },
        cancelProps: { children: t<string>("property.editor.cancel"), color: "default" },
      }}
      scrollBehavior="inside"
      size="4xl"
      title={t<string>(value?.id ? "property.editor.editTitle" : "property.editor.createTitle")}
      visible={visible}
      onClose={close}
      onOk={async () => {
        if (readOnly) throw new Error(t("management.catalog.ownerUnavailable"));
        if (!property) {
          throw new Error("Property is required");
        }
        const model = {
          ...property,
          // todo: this serialization is a hardcode
          options: JSON.stringify(property.options),
        };

        if (saveProperty) {
          const saved = await saveProperty(model);

          onSaved?.(saved);
          close();

          return;
        }

        const isUpdate = property.id != undefined && property.id > 0;

        const rsp = isUpdate
          ? await BApi.customProperty.putCustomProperty(property.id!, model)
          : await BApi.customProperty.addCustomProperty(model);

        if (!rsp.code) {
          // console.log('on saved', onSaved);
          onSaved?.(rsp.data!);
          close();
        }
      }}
      {...props}
    >
      {readOnly && (
        <p className="mb-3 text-sm text-warning" role="status">
          {t("management.catalog.ownerUnavailable")}
        </p>
      )}
      <fieldset className="min-w-0" disabled={readOnly}>
        <ModalContent
          checkOptionUsage={checkOptionUsage}
          ownerScoped={ownerScoped}
          validValueTypes={validValueTypes}
          value={value}
          onChange={(v) => setProperty(v)}
        />
      </fieldset>
    </Modal>
  );
};

PropertyModal.displayName = "PropertyModal";

export default PropertyModal;
