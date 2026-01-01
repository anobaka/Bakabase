"use client";

import type { Property, Resource } from "@/core/models/Resource";
import type { PropertyContainerProps } from "@/components/Resource/components/DetailModal/Properties/PropertyContainer";
import type { IProperty } from "@/components/Property/models";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useUpdate } from "react-use";

import { PropertyPool, PropertyValueScope, propertyValueScopes } from "@/sdk/constants";
import { useResourceOptionsStore } from "@/stores/options";
import PropertyContainer from "@/components/Resource/components/DetailModal/Properties/PropertyContainer";
import BApi from "@/sdk/BApi";
import { buildLogger } from "@/components/utils";
import { deserializeStandardValue } from "@/components/StandardValue/helpers";
import { Button } from "@/components/bakaui";

type Props = {
  resource: Resource;
  reload: () => Promise<any>;
  className?: string;
  restrictedPropertyPool?: PropertyPool;
  restrictedPropertyIds?: number[];
  propertyInnerDirection?: "hoz" | "ver";
  hidePropertyName?: boolean;
  propertyClassNames?: PropertyContainerProps["classNames"];
  noPropertyContent?: any;
  sortable?: boolean;
};

type PropertyRenderContext = {
  property: IProperty;
  propertyValues: Property;
  propertyPool: PropertyPool;
  visible: boolean;
  order: number;
};

export type RenderContext = PropertyRenderContext[];

const log = buildLogger("Properties");
const Properties = (props: Props) => {
  const {
    resource,
    className,
    reload,
    restrictedPropertyPool,
    restrictedPropertyIds,
    propertyInnerDirection = "hoz",
    hidePropertyName = false,
    propertyClassNames,
    noPropertyContent,
    sortable = false,
  } = props;
  const { t } = useTranslation();
  const forceUpdate = useUpdate();
  const cps = resource.properties;
  const resourceOptions = useResourceOptionsStore((state) => state.data);
  const [valueScopePriority, setValueScopePriority] = useState<PropertyValueScope[]>([]);
  const [builtinPropertyMap, setBuiltinPropertyMap] = useState<Record<number, IProperty>>({});
  const [customPropertyMap, setCustomPropertyMap] = useState<Record<number, IProperty>>({});
  const [showInvisibleProperties, setShowInvisibleProperties] = useState(false);

  useEffect(() => {
    const c = resourceOptions.propertyValueScopePriority;
    const vsp = c && c.length > 0 ? c.slice() : propertyValueScopes.map((s) => s.value);

    for (const scope of propertyValueScopes) {
      if (!vsp.includes(scope.value)) {
        if (scope.value == PropertyValueScope.Manual) {
          vsp.splice(0, 0, scope.value);
        } else {
          vsp.push(scope.value);
        }
      }
    }
    // console.log(vsp);
    setValueScopePriority(vsp);
  }, [resourceOptions.propertyValueScopePriority]);

  useEffect(() => {
    // @ts-ignore
    BApi.property.getPropertiesByPool(PropertyPool.Reserved | PropertyPool.Internal).then((r) => {
      const ps = r.data || [];

      setBuiltinPropertyMap(
        ps.reduce<Record<number, IProperty>>((s, t) => {
          // @ts-ignore
          s[t.id!] = t;

          return s;
        }, {}),
      );
    });

    if (restrictedPropertyPool == undefined || restrictedPropertyPool == PropertyPool.Custom) {
      const customPropertyMap = resource.properties?.[PropertyPool.Custom] || {};
      const customPropertyIds = Object.keys(customPropertyMap).map((x) => parseInt(x, 10));

      if (customPropertyIds.length > 0) {
        BApi.customProperty.getCustomPropertyByKeys({ ids: customPropertyIds }).then((r) => {
          const ps = r.data || [];

          setCustomPropertyMap(
            ps.reduce<Record<number, IProperty>>((s, t) => {
              // @ts-ignore
              s[t.id!] = t;

              return s;
            }, {}),
          );
        });
      }
    }
  }, []);

  if (!cps || Object.keys(cps).length == 0) {
    return (
      <div className={"opacity-60"}>
        {t<string>("There is no property bound yet, you can bind properties in resource profile.")}
      </div>
    );
  }

  const onValueChange = async (propertyId: number, isCustomProperty: boolean, value?: string) => {
    await BApi.resource.putResourcePropertyValue(resource.id, {
      value,
      isCustomProperty,
      propertyId,
    });
    reload();
  };

  log(props, "reserved property map", builtinPropertyMap);

  const buildRenderContext = () => {
    const renderContext: RenderContext = [];

    Object.keys(resource.properties ?? {}).forEach((propertyPoolStr) => {
      const pp = parseInt(propertyPoolStr, 10) as PropertyPool;

      if (restrictedPropertyPool != undefined && restrictedPropertyPool != pp) {
        return;
      }
      const propertyMap = resource.properties?.[pp] ?? {};

      Object.keys(propertyMap).forEach((idStr) => {
        const pId = parseInt(idStr, 10);

        if (restrictedPropertyIds != undefined && !restrictedPropertyIds.includes(pId)) {
          return;
        }

        const sp = propertyMap[pId];
        let property: IProperty | undefined;

        switch (pp) {
          case PropertyPool.Internal:
            break;
          case PropertyPool.Reserved:
            property = builtinPropertyMap[pId];
            break;
          case PropertyPool.Custom: {
            const p = customPropertyMap?.[pId];

            property = p;
            break;
          }
        }

        if (property) {
          renderContext.push({
            property,
            propertyPool: pp,
            propertyValues: sp!,
            visible: sp!.visible ?? false,
            order: sp!.order,
          });
        }
      });
    });

    return renderContext.sort((a, b) => a.order - b.order);
  };

  const renderProperty = (pCtx: PropertyRenderContext) => {
    const { property, propertyValues, propertyPool } = pCtx;

    // log(pCtx);
    return (
      <PropertyContainer
        key={`${propertyPool}-${property.id}`}
        categoryId={resource.categoryId}
        classNames={propertyClassNames}
        hidePropertyName={hidePropertyName}
        isLinked={pCtx.visible}
        property={property}
        valueScopePriority={valueScopePriority}
        values={propertyValues.values}
        onValueChange={(sdv, sbv) => {
          const dv = deserializeStandardValue(sdv ?? null, property!.dbValueType!);
          const bv = deserializeStandardValue(sbv ?? null, property!.bizValueType!);

          log("OnValueChange", "dv", dv, "bv", bv, "sdv", sdv, "sbv", sbv, property);

          let manualValue = propertyValues.values?.find(
            (v) => v.scope == PropertyValueScope.Manual,
          );

          if (!manualValue) {
            manualValue = {
              scope: PropertyValueScope.Manual,
            };
            (propertyValues.values ??= []).push(manualValue);
          }
          manualValue.bizValue = bv;
          manualValue.aliasAppliedBizValue = bv;
          manualValue.value = dv;
          forceUpdate();

          onValueChange(property.id, property!.pool == PropertyPool.Custom, sdv);
        }}
        onValueScopePriorityChange={reload}
      />
    );
  };

  const renderContext = buildRenderContext();

  // log(renderContext);

  return (
    <div>
      {propertyInnerDirection == "hoz" ? (
        <div
          className={`grid gap-x-4 gap-y-1 ${className} items-center overflow-visible`}
          style={{ gridTemplateColumns: "max(120px) minmax(0, 1fr)" }}
        >
          {renderContext
            .filter((x) => showInvisibleProperties || x.visible)
            .map((pCtx) => {
              return renderProperty(pCtx);
            })}
        </div>
      ) : (
        <div className={"flex flex-col gap-4"}>
          {renderContext
            .filter((x) => showInvisibleProperties || x.visible)
            .map((pCtx) => {
              return <div className={"flex flex-col gap-2"}>{renderProperty(pCtx)}</div>;
            })}
        </div>
      )}
      {renderContext.length == 0 ? noPropertyContent : null}
      {!showInvisibleProperties && renderContext.some((r) => !r.visible) && (
        <div className={"flex items-center justify-center my-2"}>
          <Button
            color={"primary"}
            size={"sm"}
            variant={"light"}
            onClick={() => {
              setShowInvisibleProperties(true);
            }}
          >
            {t<string>("Show properties not bound to resource profiles")}
          </Button>
        </div>
      )}
    </div>
  );
};

Properties.displayName = "Properties";

export default Properties;
