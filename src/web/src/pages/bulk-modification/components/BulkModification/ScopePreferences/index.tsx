"use client";

import type { BulkModificationScopePreferenceConfig } from "@/pages/bulk-modification/components/BulkModification/models";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { MdAdd } from "react-icons/md";

import ScopePreferenceConfigModal from "./Modal";

import { Button, Card, CardBody, Chip, Modal } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BriefProperty from "@/components/Chips/Property/BriefProperty";
import { PropertyValueScopeLabel } from "@/sdk/constants";

type Props = {
  configs?: BulkModificationScopePreferenceConfig[];
  disabled?: boolean;
  onChange?: (configs: BulkModificationScopePreferenceConfig[]) => void;
};

const ScopePreferences = ({ configs: propsConfigs, disabled, onChange }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [configs, setConfigs] = useState<BulkModificationScopePreferenceConfig[]>(propsConfigs ?? []);

  useEffect(() => {
    setConfigs(propsConfigs ?? []);
  }, [propsConfigs]);

  const renderConfig = (c: BulkModificationScopePreferenceConfig, index: number) => {
    const openEdit = () => {
      createPortal(ScopePreferenceConfigModal, {
        config: c,
        onSubmit: (updated: BulkModificationScopePreferenceConfig) => {
          const next = [...configs];

          next[index] = updated;
          setConfigs(next);
          onChange?.(next);
        },
      });
    };

    const openDelete = () => {
      createPortal(Modal, {
        defaultVisible: true,
        title: t<string>("bulkModification.scopePreference.action.delete"),
        children: t<string>("bulkModification.scopePreference.confirm.delete", {
          name: c.property?.name ?? "",
        }),
        onOk: () => {
          const next = configs.filter((_, i) => i !== index);

          setConfigs(next);
          onChange?.(next);
        },
      });
    };

    const summary = c.priorities && c.priorities.length > 0
      ? c.priorities
          .map((p) => `${PropertyValueScopeLabel[p.scope]}${p.fallbackOnEmpty ? "↘" : "·"}`)
          .join(" → ")
      : t<string>("bulkModification.scopePreference.willClear");

    return (
      <Card
        isPressable
        className="cursor-pointer hover:bg-[var(--bakaui-overlap-background)]"
        radius="sm"
        shadow="none"
        onPress={openEdit}
      >
        <CardBody className="p-2 gap-1">
          <div className="flex items-center gap-2">
            <Chip
              isCloseable
              radius="sm"
              size="sm"
              variant="bordered"
              onClose={() => {
                openDelete();
              }}
            >
              {c.property ? <BriefProperty property={c.property} /> : `#${c.propertyId}`}
            </Chip>
            <span className="text-xs opacity-70 truncate">{summary}</span>
          </div>
        </CardBody>
      </Card>
    );
  };

  return (
    <div className="flex flex-col gap-2">
      {configs.length > 0 && (
        <div className="flex flex-col gap-1">
          {configs.map((c, i) => (
            <React.Fragment key={`${c.propertyPool}-${c.propertyId}-${i}`}>
              {renderConfig(c, i)}
            </React.Fragment>
          ))}
        </div>
      )}
      <div>
        <Button
          color="secondary"
          isDisabled={disabled}
          size="sm"
          startContent={<MdAdd size={14} />}
          variant="ghost"
          onPress={() => {
            createPortal(ScopePreferenceConfigModal, {
              onSubmit: (added: BulkModificationScopePreferenceConfig) => {
                // Prevent duplicates: same (pool, id) replaces existing
                const filtered = configs.filter(
                  (x) => !(x.propertyPool === added.propertyPool && x.propertyId === added.propertyId),
                );
                const next = [...filtered, added];

                setConfigs(next);
                onChange?.(next);
              },
            });
          }}
        >
          {t<string>("bulkModification.scopePreference.action.add")}
        </Button>
      </div>
    </div>
  );
};

ScopePreferences.displayName = "ScopePreferences";

export default ScopePreferences;
