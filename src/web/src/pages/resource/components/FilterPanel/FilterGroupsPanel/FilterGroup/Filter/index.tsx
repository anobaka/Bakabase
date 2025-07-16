"use client";

"use strict";

import type { ResourceSearchFilter } from "../../models";

import { useTranslation } from "react-i18next";
import { useState } from "react";
import { useUpdateEffect } from "react-use";
import { AiOutlineClose } from "react-icons/ai";
import { MdOutlineFilterAlt, MdOutlineFilterAltOff } from "react-icons/md";

import PropertySelector from "@/components/PropertySelector";
import { PropertyPool, SearchOperation } from "@/sdk/constants";
import {
  Button,
  Dropdown,
  Tooltip,
  DropdownItem,
  DropdownMenu,
  DropdownTrigger,
} from "@/components/bakaui";
import { buildLogger } from "@/components/utils";
import BApi from "@/sdk/BApi";
import PropertyValueRenderer from "@/components/Property/components/PropertyValueRenderer";

interface IProps {
  filter: ResourceSearchFilter;
  onRemove?: () => any;
  onChange?: (filter: ResourceSearchFilter) => any;
}

const log = buildLogger("Filter");

export default ({ filter: propsFilter, onRemove, onChange }: IProps) => {
  const { t } = useTranslation();

  const a = t<string>("a");

  const [filter, setFilter] = useState<ResourceSearchFilter>(propsFilter);

  useUpdateEffect(() => {
    setFilter(propsFilter);
  }, [propsFilter]);

  const changeFilter = (newFilter: ResourceSearchFilter) => {
    setFilter(newFilter);
    onChange?.(newFilter);
  };

  log(propsFilter, filter);

  const renderOperations = () => {
    if (filter.propertyId == undefined) {
      return (
        <Tooltip content={t<string>("Please select a property first")}>
          <Button
            className={"min-w-fit pl-2 pr-2 cursor-not-allowed"}
            color={"secondary"}
            size={"sm"}
            variant={"light"}
          >
            {filter.operation == undefined
              ? t<string>("Condition")
              : t<string>(
                `SearchOperation.${SearchOperation[filter.operation]}`,
              )}
          </Button>
        </Tooltip>
      );
    }

    const operations = filter.availableOperations ?? [];

    log(operations);
    if (operations.length == 0) {
      return (
        <Tooltip content={t<string>("Can not operate on this property")}>
          <Button
            className={"min-w-fit pl-2 pr-2 cursor-not-allowed"}
            color={"secondary"}
            size={"sm"}
            variant={"light"}
          >
            {filter.operation == undefined
              ? t<string>("Condition")
              : t<string>(
                `SearchOperation.${SearchOperation[filter.operation]}`,
              )}
          </Button>
        </Tooltip>
      );
    } else {
      return (
        <Dropdown placement={"bottom-start"}>
          <DropdownTrigger>
            <Button
              className={"min-w-fit pl-2 pr-2"}
              color={"secondary"}
              size={"sm"}
              variant={"light"}
            >
              {filter.operation == undefined
                ? t<string>("Condition")
                : t<string>(
                  `SearchOperation.${SearchOperation[filter.operation]}`,
                )}
            </Button>
          </DropdownTrigger>
          <DropdownMenu>
            {operations.map((operation) => {
              const descriptionKey = `SearchOperation.${SearchOperation[operation]}.description`;
              const description = t<string>(descriptionKey);

              return (
                <DropdownItem
                  key={operation}
                  description={
                    description == descriptionKey ? undefined : description
                  }
                  onClick={() => {
                    refreshValue({
                      ...filter,
                      operation: operation,
                    });
                  }}
                >
                  {t<string>(`SearchOperation.${SearchOperation[operation]}`)}
                </DropdownItem>
              );
            })}
          </DropdownMenu>
        </Dropdown>
      );
    }
  };

  log("rendering filter", filter);

  const refreshValue = (filter: ResourceSearchFilter) => {
    if (!filter.propertyPool || !filter.propertyId || !filter.operation) {
      filter.valueProperty = undefined;
      filter.dbValue = undefined;
      filter.bizValue = undefined;
      changeFilter({
        ...filter,
      });
    } else {
      BApi.resource.getFilterValueProperty(filter).then((r) => {
        const p = r.data;

        filter.valueProperty = p;
        changeFilter({
          ...filter,
        });
      });
    }
  };

  const renderValue = () => {
    if (!filter.valueProperty) {
      return null;
    }

    if (
      filter.operation == SearchOperation.IsNull ||
      filter.operation == SearchOperation.IsNotNull
    ) {
      return null;
    }

    return (
      <PropertyValueRenderer
        bizValue={filter.bizValue}
        dbValue={filter.dbValue}
        defaultEditing={filter.dbValue == undefined}
        property={filter.valueProperty}
        variant={"light"}
        onValueChange={(dbValue, bizValue) => {
          console.log("123", dbValue, bizValue);
          changeFilter({
            ...filter,
            dbValue: dbValue,
            bizValue: bizValue,
          });
        }}
      />
    );
  };

  return (
    <Tooltip
      showArrow
      placement='bottom'
      content={
        <>
          <div className={"flex items-center"}>
            <Button
              color={filter.disabled ? 'success' : 'warning'}
              size={'sm'}
              variant={'light'}
              // className={'w-auto min-w-fit px-1'}
              onPress={() => {
                changeFilter({
                  ...filter,
                  disabled: !filter.disabled,
                });
              }}
            >
              {filter.disabled ? (
                <>
                  <MdOutlineFilterAlt className={"text-lg"} />
                  {t<string>("Enable filter")}
                </>
              ) : (
                <>
                  <MdOutlineFilterAltOff className={"text-lg"} />
                  {t<string>("Disable filter")}
                </>
              )}
            </Button>
            <Button
              color={'danger'}
              size={'sm'}
              variant={'light'}
              // className={'w-auto min-w-fit px-1'}
              onPress={onRemove}
            >
              <AiOutlineClose className={"text-base"} />
              {t<string>("Delete filter")}
            </Button>
          </div>
        </>
      }
      offset={0}
    >
      <div
        className={`flex rounded p-1 items-center ${filter.disabled ? "" : "group/filter-operations"} relative rounded`}
        style={{ backgroundColor: "var(--bakaui-overlap-background)" }}
      >
        {filter.disabled && (
          <div
            className={
              "absolute top-0 left-0 w-full h-full flex items-center justify-center z-20 group/filter-disable-cover rounded cursor-not-allowed"
            }
          >
            <MdOutlineFilterAltOff className={"text-lg text-warning"} />
          </div>
        )}
        <div
          className={`flex items-center ${filter.disabled ? "opacity-60" : ""}`}
        >
          <div className={""}>
            <Button
              className={"min-w-fit pl-2 pr-2"}
              color={"primary"}
              size={"sm"}
              variant={"light"}
              onPress={() => {
                PropertySelector.show({
                  v2: true,
                  selection:
                    filter.propertyId == undefined
                      ? undefined
                      : [
                        {
                          id: filter.propertyId,
                          pool: filter.propertyPool!,
                        },
                      ],
                  onSubmit: async (selectedProperties) => {
                    const property = selectedProperties[0]!;
                    const availableOperations =
                      (
                        await BApi.resource.getSearchOperationsForProperty({
                          propertyPool: property.pool,
                          propertyId: property.id,
                        })
                      ).data || [];
                    const nf: ResourceSearchFilter = {
                      ...filter,
                      propertyId: property.id,
                      propertyPool: property.pool,
                      dbValue: undefined,
                      bizValue: undefined,
                      property,
                      availableOperations,
                      operation: availableOperations[0],
                    };

                    refreshValue(nf);
                  },
                  multiple: false,
                  pool: PropertyPool.All,
                  addable: false,
                  editable: false,
                });
              }}
            >
              {filter.property
                ? (filter.property.name ?? t<string>("Unknown property"))
                : t<string>("Property")}
            </Button>
          </div>
          <div className={""}>{renderOperations()}</div>
          {renderValue()}
        </div>
      </div>
    </Tooltip>
  );
};
