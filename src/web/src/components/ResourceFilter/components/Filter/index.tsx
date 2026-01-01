"use client";

"use strict";

import type { SearchFilter } from "../../models";

import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";
import { useUpdateEffect } from "react-use";
import { AiOutlineClose } from "react-icons/ai";
import { MdOutlineFilterAlt, MdOutlineFilterAltOff } from "react-icons/md";

import { useFilterConfig } from "../../context/FilterContext";

import { SearchOperation } from "@/sdk/constants";
import {
  Button,
  Dropdown,
  Tooltip,
  DropdownItem,
  DropdownMenu,
  DropdownTrigger,
  Chip,
} from "@/components/bakaui";
import { buildLogger } from "@/components/utils";

interface IProps {
  filter: SearchFilter;
  onRemove?: () => any;
  onChange?: (filter: SearchFilter) => any;
  isReadonly?: boolean;
  isNew?: boolean;
  disableTooltipOperations?: boolean;
  removeBackground?: boolean;
  /** Auto trigger property selector on mount (for newly added filters) */
  autoTriggerPropertySelector?: boolean;
  /** Called when user cancels property selection for a new filter */
  onCancelNewFilter?: () => void;
}

const log = buildLogger("Filter");

const Filter = ({
  filter: propsFilter,
  onRemove,
  onChange,
  isReadonly,
  isNew,
  disableTooltipOperations,
  removeBackground,
  autoTriggerPropertySelector,
  onCancelNewFilter,
}: IProps) => {
  const { t } = useTranslation();
  const config = useFilterConfig();
  const [filter, setFilter] = useState<SearchFilter>(propsFilter);
  // Track if this filter was just created (vs restored from storage)
  // Only newly created filters should auto-trigger value editing
  const [isNewlyCreated, setIsNewlyCreated] = useState(isNew || autoTriggerPropertySelector);

  useEffect(() => {
    if (isNew || autoTriggerPropertySelector) {
      changeProperty({
        onCancel: onCancelNewFilter,
      });
    } else if (filter.propertyPool && filter.propertyId && filter.operation) {
      // Restored filter needs to fetch property info and valueProperty for display
      if (!filter.property) {
        // Fetch property info
        config.api.getValueProperty(filter).then((p) => {
          if (p) {
            setFilter((prev) => ({
              ...prev,
              property: p,
              valueProperty: p,
            }));
          }
        });
      } else if (!filter.valueProperty) {
        // Only fetch valueProperty
        config.api.getValueProperty(filter).then((p) => {
          if (p) {
            setFilter((prev) => ({
              ...prev,
              valueProperty: p,
            }));
          }
        });
      }
    }
  }, []);

  useUpdateEffect(() => {
    setFilter(propsFilter);
  }, [propsFilter]);

  const changeFilter = (newFilter: SearchFilter) => {
    setFilter(newFilter);
    onChange?.(newFilter);
    if (newFilter.propertyPool && newFilter.propertyId && newFilter.operation) {
      config.api.saveRecentFilter(newFilter);
    }
  };

  log(propsFilter, filter);

  const changeProperty = (options?: { onCancel?: () => void }) => {
    config.renderers.openPropertySelector(
      filter.propertyId == undefined
        ? undefined
        : {
            id: filter.propertyId,
            pool: filter.propertyPool!,
          },
      (property, availableOperations) => {
        const nf: SearchFilter = {
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
      options?.onCancel
    );
  };

  const renderOperations = () => {
    if (isReadonly) {
      return (
        <Chip color="secondary" size="sm" variant="light">
          {filter.operation == undefined
            ? t<string>("Condition")
            : t<string>(`SearchOperation.${SearchOperation[filter.operation]}`)}
        </Chip>
      );
    }

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
              : t<string>(`SearchOperation.${SearchOperation[filter.operation]}`)}
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
              : t<string>(`SearchOperation.${SearchOperation[filter.operation]}`)}
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
                : t<string>(`SearchOperation.${SearchOperation[filter.operation]}`)}
            </Button>
          </DropdownTrigger>
          <DropdownMenu>
            {operations.map((operation) => {
              const descriptionKey = `SearchOperation.${SearchOperation[operation]}.description`;
              const description = t<string>(descriptionKey);

              return (
                <DropdownItem
                  key={operation}
                  description={description == descriptionKey ? undefined : description}
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

  const refreshValue = (filter: SearchFilter) => {
    if (!filter.propertyPool || !filter.propertyId || !filter.operation) {
      filter.valueProperty = undefined;
      filter.dbValue = undefined;
      filter.bizValue = undefined;
      changeFilter({
        ...filter,
      });
    } else {
      // Clear value for operations that don't need it (IsNull, IsNotNull)
      if (filter.operation === SearchOperation.IsNull || filter.operation === SearchOperation.IsNotNull) {
        filter.dbValue = undefined;
        filter.bizValue = undefined;
      }
      config.api.getValueProperty(filter).then((p) => {
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

    return config.renderers.renderValueInput(
      filter.valueProperty,
      filter.dbValue,
      filter.bizValue,
      (dbValue, bizValue) => {
        // Once value is set, this filter is no longer "newly created"
        setIsNewlyCreated(false);
        changeFilter({
          ...filter,
          dbValue: dbValue,
          bizValue: bizValue,
        });
      },
      {
        // Only auto-edit for newly created filters, not restored ones
        defaultEditing: isNewlyCreated && filter.dbValue == undefined,
        size: "sm",
        variant: "light",
        isReadonly,
        operation: filter.operation,
      }
    );
  };

  const renderInner = () => {
    return (
      <div
        className={`flex rounded p-1 items-center ${filter.disabled ? "" : "group/filter-operations"} relative rounded`}
        style={
          removeBackground ? undefined : { backgroundColor: "var(--bakaui-overlap-background)" }
        }
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
        <div className={`flex items-center ${filter.disabled ? "opacity-60" : ""}`}>
          <div className={""}>
            {isReadonly ? (
              <Chip color="success" size="sm" variant="light">
                {filter.property
                  ? (filter.property.name ?? t<string>("Unknown property"))
                  : t<string>("Property")}
              </Chip>
            ) : (
              <Button
                className={"min-w-fit pl-2 pr-2"}
                color={"success"}
                size={"sm"}
                variant={"light"}
                onPress={isReadonly ? undefined : () => changeProperty()}
              >
                {filter.property
                  ? (filter.property.name ?? t<string>("Unknown property"))
                  : t<string>("Property")}
              </Button>
            )}
          </div>
          <div className={""}>{renderOperations()}</div>
          <div className={"pr-2"}>{renderValue()}</div>
        </div>
      </div>
    );
  };

  if (disableTooltipOperations) {
    return renderInner();
  }

  return (
    <Tooltip
      showArrow
      closeDelay={0}
      content={
        <>
          <div className={"flex items-center"}>
            <Button
              color={filter.disabled ? "success" : "warning"}
              size={"sm"}
              variant={"light"}
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
              color={"danger"}
              size={"sm"}
              variant={"light"}
              onPress={onRemove}
            >
              <AiOutlineClose className={"text-base"} />
              {t<string>("Delete filter")}
            </Button>
          </div>
        </>
      }
      isDisabled={isReadonly}
      offset={0}
      placement="top"
    >
      {renderInner()}
    </Tooltip>
  );
};

Filter.displayName = "Filter";

export default Filter;
