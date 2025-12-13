"use client";

import type { SearchFilter } from "../../models";
import type { IProperty } from "@/components/Property/models";

import { SearchOutlined } from "@ant-design/icons";
import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import { PropertyPool, ResourceProperty, SearchOperation } from "@/sdk/constants";
import { Button, Spinner } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

const quickFilterMap: Record<number, SearchOperation> = {
  [ResourceProperty.Filename]: SearchOperation.Contains,
  [ResourceProperty.MediaLibraryV2Multi]: SearchOperation.In,
};

interface QuickFiltersProps {
  onAdded: (filter: SearchFilter) => void;
}

const QuickFilters = ({ onAdded }: QuickFiltersProps) => {
  const { t } = useTranslation();
  const [loading, setLoading] = useState(true);
  const [properties, setProperties] = useState<IProperty[]>([]);

  useEffect(() => {
    BApi.property.getPropertiesByPool(PropertyPool.Internal).then((r) => {
      setProperties(
        (r.data || []).filter((x) => x.id in quickFilterMap).sort((a, b) => b.id - a.id),
      );
      setLoading(false);
    });
  }, []);

  return (
    <>
      <div>{t<string>("Quick filter")}</div>
      <div className="flex items-center gap-2 flex-wrap">
        {loading ? (
          <Spinner size="sm" />
        ) : (
          properties.map((p) => {
            const operation = quickFilterMap[p.id];
            const pool = PropertyPool.Internal;

            return (
              <Button
                key={p.id}
                size="sm"
                onPress={async () => {
                  const vp = (
                    await BApi.resource.getFilterValueProperty({
                      propertyPool: pool,
                      propertyId: p.id,
                      operation: operation,
                    })
                  ).data;
                  const availableOperations =
                    (
                      await BApi.resource.getSearchOperationsForProperty({
                        propertyPool: pool,
                        propertyId: p.id,
                      })
                    ).data ?? [];
                  const newFilter: SearchFilter = {
                    propertyId: p.id,
                    operation: operation,
                    propertyPool: p.pool,
                    property: p,
                    valueProperty: vp,
                    availableOperations,
                    disabled: false,
                  };

                  onAdded(newFilter);
                }}
              >
                <SearchOutlined className="text-base" />
                {p.name}
              </Button>
            );
          })
        )}
      </div>
    </>
  );
};

QuickFilters.displayName = "QuickFilters";

export default QuickFilters;
