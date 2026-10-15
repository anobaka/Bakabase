import type { IProperty } from "@/components/Property/models";
import type { FilterConfig, SearchFilter } from "@/components/ResourceFilter/models";
import type { ManagementSource } from "@/features/management/api";
import type { SearchForm } from "@/pages/resource/models";

import { useMemo, useRef } from "react";
import { useTranslation } from "react-i18next";

import PropertyValueRenderer from "@/components/Property/components/PropertyValueRenderer";
import Filter from "@/components/ResourceFilter/components/Filter";
import valueStyles from "@/components/ResourceFilter/components/Filter/value.module.scss";
import { FilterProvider } from "@/components/ResourceFilter/context/FilterContext";
import { deserializeStandardValue, serializeStandardValue } from "@/components/StandardValue";
import DeviceDot from "@/features/management/DeviceDot";
import {
  FilterDisplayMode,
  PropertyPool,
  PropertyType,
  SearchOperation,
  StandardValueType,
} from "@/sdk/constants";

interface Props {
  sources: ManagementSource[];
  owner?: SearchForm["owner"];
  deviceIds?: string[] | null;
  onChange: (deviceIds: string[] | undefined) => void;
}

export function shouldShowDeviceFilter(
  sources: ManagementSource[],
  selection: Pick<Props, "owner" | "deviceIds">,
) {
  return (
    sources.some((source) => !source.isLocal) || selection.deviceIds != null || !!selection.owner
  );
}

/** A presentation-only property: only deviceIds, never this descriptor, enters a query. */
export default function DeviceFilter(props: Props) {
  // A saved restriction (including no devices) must stay editable even when the
  // last managed device has been removed. Default single-device pages stay unchanged.
  if (!shouldShowDeviceFilter(props.sources, props)) return null;

  return <DeviceFilterEditor {...props} />;
}

function DeviceFilterEditor({ sources, owner, deviceIds, onChange }: Props) {
  const { t } = useTranslation();
  const current = useRef({ sources, onChange });

  current.current = { sources, onChange };
  const selectedIds = owner
    ? [owner.serverId]
    : (deviceIds ?? sources.map((source) => source.serverId));
  const missingIds = selectedIds.filter((id) => !sources.some((source) => source.serverId === id));
  const choices = [
    ...sources.map((source) => ({
      value: source.serverId,
      label: source.isLocal ? t("management.thisDevice") : source.name,
      unavailable:
        !source.available ||
        (owner?.serverId === source.serverId && owner.libraryEpoch !== source.libraryEpoch),
    })),
    ...missingIds.map((id) => ({
      value: id,
      label: t("management.unavailableDevice", { id: id.slice(0, 8) }),
      unavailable: true,
    })),
  ];
  // The shared choice editor caches its option catalog. Refresh it when devices,
  // names or availability change, while keeping selection in the search form.
  const optionsKey = JSON.stringify(choices);
  const property = useMemo<IProperty>(
    () => ({
      id: -1,
      pool: PropertyPool.Custom,
      name: t("management.deviceFilter"),
      type: PropertyType.MultipleChoice,
      dbValueType: StandardValueType.ListString,
      bizValueType: StandardValueType.ListString,
      typeName: "MultipleChoice",
      poolName: "Custom",
      order: 0,
      options: { choices },
    }),
    [optionsKey, t],
  );
  const config = useMemo<FilterConfig>(
    () => ({
      // This scope is computed locally, not a saved custom property. In particular,
      // editing it must not fetch property counts or save a fake recent filter.
      api: {
        getAvailableOperations: async () => [SearchOperation.In],
        getAvailableOperationsByPropertyType: async () => [SearchOperation.In],
        getProperty: async () => property,
        getValueProperty: async () => property,
        saveRecentFilter: async () => {},
        getRecentFilters: async () => [],
      },
      renderers: {
        openPropertySelector: () => {},
        renderValueInput: (valueProperty, dbValue, bizValue, onValueChange, options) => (
          <div className={valueStyles.value}>
            <PropertyValueRenderer
              key={optionsKey}
              bizValue={bizValue}
              dbValue={dbValue}
              isEditing={options?.isEditing}
              isReadonly={options?.isReadonly}
              property={valueProperty}
              renderOptionExtra={({ value }) => (
                <>
                  <DeviceDot
                    className="ml-1 h-2 w-2"
                    name={choices.find((choice) => choice.value === value)?.label}
                    serverId={value}
                  />
                  {choices.find((choice) => choice.value === value)?.unavailable && (
                    <span className="ml-1 text-xs text-warning" title={value}>
                      {t("management.offline")}
                    </span>
                  )}
                </>
              )}
              size={options?.size}
              variant={options?.variant}
              onValueChange={onValueChange}
            />
          </div>
        ),
      },
    }),
    [property, optionsKey, t],
  );
  const filter = useMemo<SearchFilter>(
    () => ({
      propertyId: property.id,
      propertyPool: property.pool,
      property,
      valueProperty: property,
      availableOperations: [SearchOperation.In],
      operation: SearchOperation.In,
      disabled: false,
      dbValue: serializeStandardValue(selectedIds, StandardValueType.ListString),
    }),
    [property, JSON.stringify(selectedIds)],
  );

  return (
    <div aria-label={t("management.deviceFilter")}>
      <FilterProvider config={config}>
        <Filter
          hideInternalActions
          filter={filter}
          filterDisplayMode={FilterDisplayMode.Simple}
          layout="vertical"
          onChange={(next) => {
            const ids =
              (deserializeStandardValue(next.dbValue ?? null, StandardValueType.ListString) as
                | string[]
                | undefined) ?? [];
            // Full option editors can stay open across a source refresh. Do not
            // turn an old catalog's complete selection into all newly added devices.
            const latestSources = current.current.sources;
            const allKnownDevices =
              latestSources.length > 0 &&
              ids.length === latestSources.length &&
              latestSources.every((source) => ids.includes(source.serverId));

            current.current.onChange(allKnownDevices ? undefined : ids);
          }}
        />
      </FilterProvider>
    </div>
  );
}
