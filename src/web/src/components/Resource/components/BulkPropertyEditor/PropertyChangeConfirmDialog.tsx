"use client";

import React, { useState } from "react";
import { useTranslation } from "react-i18next";
import { ExclamationCircleOutlined } from "@ant-design/icons";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { IProperty } from "@/components/Property/models";

import { Chip, Modal } from "@/components/bakaui";
import BriefProperty from "@/components/Chips/Property/BriefProperty";

export interface PropertyChangeConfirmDialogProps extends DestroyableProps {
  property: IProperty;
  affectedResources: Array<{
    resourceId: number;
    resourceName: string;
    oldBizValue?: string;
  }>;
  newBizValue?: string;
  onConfirm: () => Promise<void>;
}

const PropertyChangeConfirmDialog: React.FC<PropertyChangeConfirmDialogProps> = ({
  property,
  affectedResources,
  newBizValue,
  onConfirm,
  onDestroyed,
}) => {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(true);
  const [confirming, setConfirming] = useState(false);

  const handleConfirm = async () => {
    setConfirming(true);
    try {
      await onConfirm();
      setVisible(false);
    } finally {
      setConfirming(false);
    }
  };

  return (
    <Modal
      size="lg"
      title={t("Confirm property change")}
      visible={visible}
      footer={{
        actions: ["cancel", "ok"],
        okProps: {
          children: t("Confirm"),
          isLoading: confirming,
          color: "primary",
        },
      }}
      onClose={() => setVisible(false)}
      onDestroyed={onDestroyed}
      onOk={handleConfirm}
    >
      <div className="flex flex-col gap-3">
        <div className="flex items-center gap-2">
          <span className="text-sm">{t("Setting")}</span>
          <BriefProperty fields={["pool", "name"]} property={property} />
          <span className="text-sm">{t("to")}</span>
          <Chip size="sm" color="primary" variant="flat">
            {newBizValue || `(${t("empty")})`}
          </Chip>
        </div>
        <div className="text-sm text-warning flex items-center gap-2">
          <ExclamationCircleOutlined />
          {t("This will override different values on {{count}} resources", { count: affectedResources.length })}
        </div>
        <div
          className="max-h-[300px] overflow-auto border rounded p-3"
          style={{ borderColor: "var(--bakaui-overlap-background)" }}
        >
          <div className="flex flex-col gap-1">
            {affectedResources.slice(0, 20).map((resource) => (
              <div
                key={resource.resourceId}
                className="text-sm text-default-600 flex items-center gap-2"
              >
                <span className="truncate max-w-[200px]">{resource.resourceName}</span>
                <span className="text-default-400">:</span>
                <span className="text-default-400 truncate max-w-[150px]">
                  {resource.oldBizValue || `(${t("empty")})`}
                </span>
                <span>→</span>
                <span className="text-primary truncate max-w-[150px]">
                  {newBizValue || `(${t("empty")})`}
                </span>
              </div>
            ))}
            {affectedResources.length > 20 && (
              <div className="text-sm text-default-400">
                ... {t("and {{count}} more", { count: affectedResources.length - 20 })}
              </div>
            )}
          </div>
        </div>
      </div>
    </Modal>
  );
};

PropertyChangeConfirmDialog.displayName = "PropertyChangeConfirmDialog";

export default PropertyChangeConfirmDialog;
