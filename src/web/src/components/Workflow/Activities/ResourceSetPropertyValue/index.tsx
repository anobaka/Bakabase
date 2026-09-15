"use client";

import type { WorkflowActivityUI } from "../types";
import type { IProperty } from "@/components/Property/models";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import PropertySelector from "@/components/PropertySelector";
import PropertyValueEditorModal from "@/components/PropertyValueEditorModal";
import { Button } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BApi from "@/sdk/BApi";
import { PropertyPool, WorkflowActivityCategory } from "@/sdk/constants";

interface Config {
  propertyId?: number;
  pool?: PropertyPool;
  value?: string;
  isBizValue?: boolean;
}

/**
 * Reads back the chosen property so the form can show its name and edit its value. There is no
 * "one property by id" endpoint, so it asks for the pool and picks — the pools are small.
 */
const useProperty = (config: Config) => {
  const [property, setProperty] = useState<IProperty>();

  useEffect(() => {
    if (config.propertyId == null) {
      setProperty(undefined);

      return;
    }

    BApi.property
      .getPropertiesByPool((config.pool ?? PropertyPool.Custom) as any)
      .then((r) =>
        setProperty(
          ((r.data ?? []) as IProperty[]).find(
            (p) => p.id === config.propertyId && p.pool === (config.pool ?? PropertyPool.Custom),
          ),
        ),
      );
  }, [config.propertyId, config.pool]);

  return property;
};

const ConfigForm: React.FC<{ value: Config; onChange: (v: Config) => void }> = ({
  value,
  onChange,
}) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const property = useProperty(value);

  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center gap-2">
        <span className="text-sm text-default-500">
          {t<string>("workflow.resource.setPropertyValue.property")}
        </span>
        <Button
          size="sm"
          variant="flat"
          onPress={() =>
            createPortal(PropertySelector, {
              v2: true,
              // Custom and reserved only: an internal property is derived, not something
              // anybody sets.
              pool: PropertyPool.Custom | PropertyPool.Reserved,
              multiple: false,
              onSubmit: async (selection: IProperty[]) => {
                const picked = selection[0];

                if (!picked) return;
                // The value belongs to the old property; keeping it would silently write
                // nonsense into the new one.
                onChange({ propertyId: picked.id, pool: picked.pool, value: undefined });
              },
            })
          }
        >
          {property?.name ?? t<string>("workflow.resource.setPropertyValue.pick")}
        </Button>
      </div>

      {property && (
        <div className="flex items-center gap-2">
          <span className="text-sm text-default-500">
            {t<string>("workflow.resource.setPropertyValue.value")}
          </span>
          <Button
            size="sm"
            variant="flat"
            onPress={() =>
              createPortal(PropertyValueEditorModal, {
                property,
                onSubmit: (serialized: string) => onChange({ ...value, value: serialized }),
              })
            }
          >
            {value.value ?? t<string>("workflow.resource.setPropertyValue.empty")}
          </Button>
        </div>
      )}
    </div>
  );
};

export const ResourceSetPropertyValueUI: WorkflowActivityUI<Config> = {
  kind: "action.resource.setPropertyValue",
  displayNameKey: "workflow.resource.setPropertyValue.displayName",
  category: WorkflowActivityCategory.Action,
  defaultConfig: () => ({ pool: PropertyPool.Custom }),
  parseConfig: (json) => {
    try {
      return json ? (JSON.parse(json) as Config) : { pool: PropertyPool.Custom };
    } catch {
      return { pool: PropertyPool.Custom };
    }
  },
  serializeConfig: (config) => JSON.stringify(config),
  // Without a property the run would fail at this step; saying so in the editor is cheaper than
  // saying it in a run history.
  isValid: (config) => config.propertyId != null,
  ConfigForm,
  Summary: ({ config }) => {
    const { t } = useTranslation();
    const property = useProperty(config);

    if (config.propertyId == null) {
      return (
        <span className="text-xs text-danger">
          {t<string>("workflow.resource.setPropertyValue.pick")}
        </span>
      );
    }

    return (
      <span className="text-xs text-default-500">
        {property?.name ?? `#${config.propertyId}`}
        {config.value ? ` = ${config.value}` : ""}
      </span>
    );
  },
};
