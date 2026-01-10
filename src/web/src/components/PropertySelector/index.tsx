"use client";

import type { IProperty } from "@/components/Property/models";
import type { DestroyableProps } from "@/components/bakaui/types";

import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { SearchOutlined } from "@ant-design/icons";
import { AiOutlineCheckCircle } from "react-icons/ai";

import PropertyModal from "../PropertyModal";

import Property from "@/components/Property";
import PropertyV2 from "@/components/Property/v2";
import PropertyTypeIcon from "@/components/Property/components/PropertyTypeIcon";
import { buildLogger } from "@/components/utils";
import { PropertyPool, PropertyType, ResourceProperty, StandardValueType } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import { Button, Chip, Divider, Input, Modal, Spacer } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

type Key = {
  id: number;
  pool: PropertyPool;
};

interface IProps extends DestroyableProps {
  selection?: Key[];
  onSubmit?: (selectedProperties: IProperty[]) => Promise<any>;
  /** Called when modal is closed without making a selection */
  onCancel?: () => void;
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

// Deprecated internal properties that should not be selectable
const deprecatedInternalPropertyIds: number[] = [
  ResourceProperty.Category,
  ResourceProperty.MediaLibrary,
  ResourceProperty.MediaLibraryV2,
];

const PropertySelector = (props: IProps) => {
  log("props", props);
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const {
    selection: propsSelection,
    onSubmit: propsOnSubmit,
    onCancel,
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
  const [keyword, setKeyword] = useState("");
  const hasSubmittedRef = useRef(false);

  const loadProperties = async () => {
    const psr = (await BApi.property.getPropertiesByPool(pool)).data || [];
    // @ts-ignore
    setProperties(psr);
  };

  useEffect(() => {
    loadProperties();
  }, []);

  const isSelected = (property: IProperty): boolean => {
    return selection.some((s) => s.id === property.id && s.pool === property.pool);
  };

  const toggleProperty = async (property: IProperty) => {
    const isDeprecatedInternal = property.pool === PropertyPool.Internal &&
      deprecatedInternalPropertyIds.includes(property.id);
    if (isDeprecatedInternal || isDisabled?.(property)) {
      return;
    }

    const selected = isSelected(property);

    if (multiple) {
      if (selected) {
        setSelection(selection.filter((s) => !(s.id === property.id && s.pool === property.pool)));
      } else {
        setSelection([...selection, { id: property.id, pool: property.pool }]);
      }
    } else {
      if (selected) {
        setSelection([]);
      } else {
        const ns: Key[] = [{ id: property.id, pool: property.pool }];
        setSelection(ns);
        await onSubmit(ns);
      }
    }
  };

  const onSubmit = async (sel: Key[]) => {
    if (sel.length > 0) {
      await BApi.options.addLatestUsedProperty(sel);
    }

    if (propsOnSubmit) {
      await propsOnSubmit(
        sel
          .map((s) => properties.find((p) => p.id === s.id && p.pool === s.pool))
          .filter((x) => x != undefined) as IProperty[],
      );
    }
    hasSubmittedRef.current = true;
    setVisible(false);
  };

  // Filter by keyword, valueTypes, and types
  const filteredProperties = properties.filter((p) => {
    if (keyword && !p.name!.toLowerCase().includes(keyword.toLowerCase())) {
      return false;
    }
    if (valueTypes && !valueTypes.includes(p.dbValueType)) {
      return false;
    }
    if (types && !types.includes(p.type)) {
      return false;
    }
    return true;
  });

  // Group properties by pool first, then by type
  const reservedProperties = filteredProperties.filter((p) => p.pool === PropertyPool.Reserved);
  const customProperties = filteredProperties.filter((p) => p.pool === PropertyPool.Custom);
  const internalProperties = filteredProperties.filter((p) => p.pool === PropertyPool.Internal);

  // Group custom properties by type
  const groupedCustomProperties: { [key in PropertyType]?: IProperty[] } =
    customProperties.reduce<{ [key in PropertyType]?: IProperty[] }>(
      (s, t) => {
        (s[t.type!] ??= []).push(t);
        return s;
      },
      {},
    );

  const renderFilter = () => {
    const filters: any[] = [];

    if (pool !== PropertyPool.All) {
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
                <Chip key={`pool-${v}`} size="sm">
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
          <Chip key={`vt-${vt}`} size="sm">
            {t<string>(StandardValueType[vt])}
          </Chip>
        )),
      );
    }

    if (types) {
      filters.push(
        ...types.map((pt) => (
          <Chip key={`pt-${pt}`} size="sm">
            {t<string>(PropertyType[pt])}
          </Chip>
        )),
      );
    }

    if (filters.length > 0) {
      return (
        <div className="flex gap-1 items-center flex-wrap">
          <span className="text-default-500 text-sm">{t<string>("common.state.filtering")}</span>
          {filters}
        </div>
      );
    }

    return null;
  };

  const renderPropertyItem = (property: IProperty, showTypeIcon?: boolean) => {
    const selected = isSelected(property);
    const isDeprecatedInternal = property.pool === PropertyPool.Internal &&
      deprecatedInternalPropertyIds.includes(property.id);
    const disabled = isDeprecatedInternal || isDisabled?.(property);

    return (
      <div
        key={`${property.pool}-${property.id}`}
        className={`relative cursor-pointer rounded-md transition-all ${
          selected ? "ring-2 ring-success ring-offset-1" : ""
        } ${disabled ? "opacity-50 cursor-not-allowed" : ""}`}
      >
        {v2 ? (
          <PropertyV2
            disabled={disabled}
            editable={editable}
            hidePool
            hideType={showTypeIcon}
            property={property}
            removable={removable}
            onSaved={loadProperties}
            onClick={() => toggleProperty(property)}
          />
        ) : (
          <Property
            disabled={disabled}
            editable={editable}
            property={property}
            removable={removable}
            onSaved={loadProperties}
            onClick={() => toggleProperty(property)}
          />
        )}
        {selected && (
          <div className="absolute -top-1 -right-1 bg-success rounded-full p-0.5">
            <AiOutlineCheckCircle className="text-white text-xs" />
          </div>
        )}
      </div>
    );
  };

  const renderPropertyGroup = (groupTitle: string, props: IProperty[], showTypeIcon?: boolean) => {
    if (props.length === 0) return null;

    return (
      <div className="rounded-lg border border-default-200 bg-content1 overflow-hidden">
        <div className="flex items-center gap-2 px-4 py-3 bg-default-100 border-b border-default-200">
          {showTypeIcon && props[0] && <PropertyTypeIcon textVariant="default" type={props[0].type} />}
          {!showTypeIcon && <span className="font-medium">{groupTitle}</span>}
          <span className="text-default-500 text-sm">({props.length})</span>
        </div>
        <div className="p-4">
          <div className="flex flex-wrap gap-2">
            {props.map((p) => renderPropertyItem(p, showTypeIcon))}
          </div>
        </div>
      </div>
    );
  };

  const renderProperties = () => {
    const totalCount = filteredProperties.length;

    if (totalCount === 0) {
      return (
        <div className="flex flex-col items-center justify-center gap-2 py-8">
          <span className="text-default-400">{t<string>("property.empty.noPropertiesAvailable")}</span>
          {addable && (
            <Button
              color="primary"
              size="sm"
              onPress={() => {
                createPortal(PropertyModal, {
                  onSaved: loadProperties,
                  validValueTypes: valueTypes?.map((v) => v as unknown as PropertyType),
                });
              }}
            >
              {t<string>("property.action.addProperty")}
            </Button>
          )}
        </div>
      );
    }

    return (
      <div className="grid gap-4 grid-cols-1 lg:grid-cols-2">
        {/* Internal Properties */}
        {(pool & PropertyPool.Internal) !== 0 && internalProperties.length > 0 &&
          renderPropertyGroup(t("common.label.internalProperties"), internalProperties, false)}

        {/* Reserved Properties */}
        {(pool & PropertyPool.Reserved) !== 0 && reservedProperties.length > 0 &&
          renderPropertyGroup(t("common.label.reservedProperties"), reservedProperties, false)}

        {/* Custom Properties grouped by type */}
        {(pool & PropertyPool.Custom) !== 0 &&
          (Object.keys(groupedCustomProperties) as unknown as PropertyType[]).map((k) => {
            const ps = groupedCustomProperties[k]!;
            return (
              <div key={k}>
                {renderPropertyGroup("", ps, true)}
              </div>
            );
          })}
      </div>
    );
  };

  return (
    <Modal
      classNames={{ base: "max-w-[70vw]" }}
      footer={multiple && filteredProperties.length > 0 ? true : <Spacer />}
      size="3xl"
      title={title ?? t<string>(multiple ? "property.modal.selectProperties" : "property.modal.selectProperty")}
      visible={visible}
      onClose={() => {
        setVisible(false);
      }}
      onDestroyed={() => {
        if (!hasSubmittedRef.current) {
          onCancel?.();
        }
        onDestroyed?.();
      }}
      onOk={async () => {
        await onSubmit(selection);
      }}
    >
      <div className="flex flex-col gap-4">
        {(() => {
          const filter = renderFilter();
          return filter && (
            <>
              {filter}
              <Divider />
            </>
          );
        })()}
        <div className="flex items-center justify-between">
          <Input
            size="sm"
            placeholder={t<string>("property.action.searchProperties")}
            startContent={<SearchOutlined className="text-small" />}
            value={keyword}
            onValueChange={setKeyword}
            className="w-64"
          />
          <div className="flex items-center gap-4">
            <div className="text-sm text-default-500">
              {t("common.state.selected")}: {selection.length}
            </div>
            {addable && (
              <Button
                color="primary"
                size="sm"
                onPress={() => {
                  createPortal(PropertyModal, {
                    onSaved: loadProperties,
                    validValueTypes: valueTypes?.map((v) => v as unknown as PropertyType),
                  });
                }}
              >
                {t<string>("property.action.addProperty")}
              </Button>
            )}
          </div>
        </div>

        {renderProperties()}
      </div>
    </Modal>
  );
};

export default PropertySelector;
