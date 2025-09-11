"use client";

import type { IProperty } from "@/components/Property/models";
import type { DestroyableProps } from "@/components/bakaui/types";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import PropertyModal from "../PropertyModal";

import Property from "@/components/Property";
import PropertyV2 from "@/components/Property/v2";
import { buildLogger } from "@/components/utils";
import { PropertyPool, PropertyType, StandardValueType } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import { Button, Chip, Divider, Modal, Spacer } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { useUiOptionsStore } from "@/stores/options.ts";

type Key = {
  id: number;
  pool: PropertyPool;
};

interface IProps extends DestroyableProps {
  selection?: Key[];
  onSubmit?: (selectedProperties: IProperty[]) => Promise<any>;
  multiple?: boolean;
  pool: PropertyPool;
  valueTypes?: StandardValueType[];
  types?: PropertyType[];
  editable?: boolean;
  addable?: boolean;
  removable?: boolean;
  title?: any;
  isDisabled?: (p: IProperty) => boolean;
  v2?: boolean;
}

const log = buildLogger("PropertySelector");

const PropertySelector = (props: IProps) => {
  log("props", props);
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const {
    selection: propsSelection,
    onSubmit: propsOnSubmit,
    multiple = true,
    pool,
    types,
    valueTypes,
    addable,
    editable,
    removable,
    title,
    onDestroyed,
    isDisabled,
    v2,
  } = props;

  const [properties, setProperties] = useState<IProperty[]>([]);
  const [selection, setSelection] = useState<Key[]>(propsSelection || []);
  const [visible, setVisible] = useState(true);
  const uiOptionsStore = useUiOptionsStore();

  // console.log('props selection', propsSelection, properties, addable, editable, removable);

  const loadProperties = async () => {
    const psr = (await BApi.property.getPropertiesByPool(pool)).data || [];

    // @ts-ignore
    setProperties(psr);
  };

  useEffect(() => {
    loadProperties();
  }, []);

  const renderProperty = (property: IProperty) => {
    const selected = selection.some((s) => s.id == property.id && s.pool == property.pool);

    const disabled = isDisabled?.(property);

    if (v2) {
      return (
        <PropertyV2
          key={`${property.id}-${property.pool}`}
          disabled={disabled}
          editable={editable}
          isSelected={selected}
          property={property}
          removable={removable}
          onClick={async () => {
            if (disabled) {
              return;
            }

            if (multiple) {
              if (selected) {
                setSelection(
                  selection.filter((s) => s.id != property.id || s.pool != property.pool),
                );
              } else {
                setSelection([
                  ...selection,
                  {
                    id: property.id,
                    pool: property.pool,
                  },
                ]);
              }
            } else {
              if (selected) {
                setSelection([]);
              } else {
                const ns: Key[] = [
                  {
                    id: property.id,
                    pool: property.pool,
                  },
                ];

                setSelection(ns);
                await onSubmit(ns);
              }
            }
          }}
          onSaved={loadProperties}
        />
      );
    }

    return (
      <Property
        key={`${property.id}-${property.pool}`}
        disabled={isDisabled?.(property)}
        editable={editable}
        property={property}
        removable={removable}
        onClick={async () => {
          if (multiple) {
            if (selected) {
              setSelection(selection.filter((s) => s.id != property.id || s.pool != property.pool));
            } else {
              setSelection([
                ...selection,
                {
                  id: property.id,
                  pool: property.pool,
                },
              ]);
            }
          } else {
            if (selected) {
              setSelection([]);
            } else {
              const ns: Key[] = [
                {
                  id: property.id,
                  pool: property.pool,
                },
              ];

              setSelection(ns);
              await onSubmit(ns);
            }
          }
        }}
        onSaved={loadProperties}
      />
    );
  };

  const onSubmit = async (selection: Key[]) => {
    if (selection.length > 0) {
      await BApi.options.addLatestUsedProperty(selection);
    }

    // console.log(customProperties, selection);
    if (propsOnSubmit) {
      await propsOnSubmit(
        selection
          .map((s) => properties.find((p) => p.id == s.id && p.pool == s.pool))
          .filter((x) => x != undefined) as IProperty[],
      );
    }
    setVisible(false);
  };

  // console.log('render', reservedProperties, customProperties);

  const renderFilter = () => {
    const filters: any[] = [];

    if (pool != PropertyPool.All) {
      Object.keys(PropertyPool).forEach((k) => {
        const v = parseInt(k, 10) as PropertyPool;

        if (Number.isNaN(v)) {
          return;
        }
        if (pool & v) {
          switch (v) {
            case PropertyPool.Internal:
            case PropertyPool.Reserved:
            case PropertyPool.Custom:
              filters.push(
                <Chip key={"pool"} size={"sm"}>
                  {t<string>(PropertyPool[v])}
                </Chip>,
              );
              break;
            default:
              break;
          }
        }
      });
    }
    if (valueTypes) {
      filters.push(
        ...valueTypes.map((vt) => (
          <Chip key={vt} size={"sm"}>
            {t<string>(StandardValueType[vt])}
          </Chip>
        )),
      );
    }

    if (types) {
      filters.push(
        ...types.map((pt) => (
          <Chip key={pt} size={"sm"}>
            {t<string>(PropertyType[pt])}
          </Chip>
        )),
      );
    }

    if (filters.length > 0) {
      return (
        <div className={"flex gap-1 items-center flex-wrap"}>
          {t<string>("Filtering")}
          <Spacer />
          {filters}
        </div>
      );
    } else {
      return null;
    }
  };

  const filteredProperties = properties.filter((p) => {
    if (valueTypes && !valueTypes.includes(p.dbValueType)) {
      return false;
    }

    if (types && !types.includes(p.type)) {
      return false;
    }

    return true;
  });

  // Sort properties with latest used properties having higher priority
  const sortedFilteredProperties = filteredProperties.sort((a, b) => {
    const latestUsedProperties = uiOptionsStore.data?.latestUsedProperties ?? [];
    let aIndex = latestUsedProperties.findIndex((l) => l.id === a.id && l.pool === a.pool);

    if (aIndex == -1) {
      aIndex = Number.MAX_SAFE_INTEGER;
    }

    let bIndex = latestUsedProperties.findIndex((l) => l.id === b.id && l.pool === b.pool);

    if (bIndex == -1) {
      bIndex = Number.MAX_SAFE_INTEGER;
    }

    return aIndex - bIndex;
  });

  console.log(
    filteredProperties,
    uiOptionsStore.data?.latestUsedProperties,
    sortedFilteredProperties,
  );

  const selectedProperties = selection
    .map((s) => sortedFilteredProperties.find((p) => p.id == s.id && p.pool == s.pool))
    .filter((x) => x)
    .map((x) => x!);
  const unselectedProperties = sortedFilteredProperties.filter(
    (p) => !selection.some((s) => s.id == p.id && s.pool == p.pool),
  );
  const propertyCount = selectedProperties.length + unselectedProperties.length;

  const renderProperties = () => {
    if (propertyCount == 0) {
      return (
        <div className={"flex items-center justify-center gap-2 mt-6"}>
          {t<string>("No properties available")}
          {addable && (
            <Button
              color={"primary"}
              size={"sm"}
              onPress={() => {
                createPortal(PropertyModal, {
                  onSaved: loadProperties,
                  validValueTypes: valueTypes?.map((v) => v as unknown as PropertyType),
                });
              }}
            >
              {t<string>("Add a property")}
            </Button>
          )}
        </div>
      );
    }

    // todo: make framer-motion up-to-date once https://github.com/heroui-inc/heroui/issues/4805 is resolved.

    return (
      <div className={"flex flex-col gap-2"}>
        <div className={"flex flex-col gap-2"}>
          <div
            className={"text-base"}
          >{`${t<string>("Selected")}(${selectedProperties.length})`}</div>
          <div className={"flex flex-wrap gap-2 items-start"}>
            {selectedProperties.map((p) => renderProperty(p))}
          </div>
        </div>
        <Divider />
        <div className={"flex flex-col gap-2"}>
          <div
            className={"text-base"}
          >{`${t<string>("Not selected")}(${unselectedProperties.length})`}</div>
          <div className={"flex flex-wrap gap-2 items-start"}>
            {unselectedProperties.map((p) => renderProperty(p))}
          </div>
        </div>
      </div>
    );
  };

  return (
    <Modal
      footer={multiple && propertyCount > 0 ? true : <Spacer />}
      size={"2xl"}
      title={title ?? t<string>(multiple ? "Select properties" : "Select a property")}
      visible={visible}
      onClose={() => {
        setVisible(false);
      }}
      onDestroyed={onDestroyed}
      onOk={async () => {
        await onSubmit(selection);
      }}
    >
      <div className="flex flex-col gap-2">
        {renderFilter()}
        <Divider />
        {renderProperties()}
        <div>
          {addable && (
            <Button
              className={"mt-2"}
              color={"primary"}
              size={"sm"}
              onPress={() => {
                createPortal(PropertyModal, {
                  onSaved: loadProperties,
                  validValueTypes: valueTypes?.map((v) => v as unknown as PropertyType),
                });
              }}
            >
              {t<string>("Add a property")}
            </Button>
          )}
        </div>
      </div>
    </Modal>
  );
};

export default PropertySelector;
