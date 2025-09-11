"use client";

import type { EnhancerTargetFullOptions } from "../../models";
import type { EnhancerTargetDescriptor } from "../../../../models";
import type { IProperty } from "@/components/Property/models";
import type { PropertyPool } from "@/sdk/constants";

import { DeleteOutlined, EditOutlined, QuestionCircleOutlined } from "@ant-design/icons";
import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { useUpdateEffect } from "react-use";
import { AiOutlineCheckCircle, AiOutlineClose } from "react-icons/ai";

import { createEnhancerTargetOptions } from "../../models";

import TargetOptions from "./TargetOptions";

import { Autocomplete, AutocompleteItem, Button, Chip, Tooltip } from "@/components/bakaui";
import { SpecialTextType, StandardValueType } from "@/sdk/constants";
import { IntegrateWithSpecialTextLabel } from "@/components/SpecialText";
import { buildLogger } from "@/components/utils";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import PropertyMatcher from "@/components/PropertyMatcher";
import BriefProperty from "@/components/Chips/Property/BriefProperty";

interface Props {
  dynamicTarget?: string;
  descriptor: EnhancerTargetDescriptor;
  options?: EnhancerTargetFullOptions;
  otherDynamicTargetsInGroup?: string[];
  onDeleted?: () => any;
  propertyMap?: { [key in PropertyPool]?: Record<number, IProperty> };
  onPropertyChanged?: () => any;
  onChange?: (options: EnhancerTargetFullOptions) => any;
  dynamicTargetCandidates?: string[];
}

const StdValueSpecialTextIntegrationMap: {
  [key in StandardValueType]?: SpecialTextType;
} = {
  [StandardValueType.DateTime]: SpecialTextType.DateTime,
};

const log = buildLogger("TargetRow");
const TargetRow = (props: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const {
    dynamicTarget,
    options: propsOptions,
    onDeleted,
    otherDynamicTargetsInGroup,
    propertyMap,
    descriptor,
    onPropertyChanged,
    onChange,
    dynamicTargetCandidates,
  } = props;

  const [options, setOptions] = useState<EnhancerTargetFullOptions>(
    propsOptions ?? createEnhancerTargetOptions(descriptor),
  );
  const [dynamicTargetError, setDynamicTargetError] = useState<string>();
  const dynamicTargetInputValueRef = useRef<string>();
  const [editingDynamicTarget, setEditingDynamicTarget] = useState(false);

  const validateDynamicTarget = (newTarget: string) => {
    let error;

    if (otherDynamicTargetsInGroup?.includes(newTarget)) {
      error = t<string>("Duplicate dynamic target is found");
    }
    if (newTarget.length == 0) {
      error = t<string>("This field is required");
    }
    if (dynamicTargetError != error) {
      setDynamicTargetError(error);
    }

    return error == undefined;
  };

  useUpdateEffect(() => {
    setOptions(propsOptions ?? createEnhancerTargetOptions(descriptor));
  }, [propsOptions]);

  const patchTargetOptions = async (patches: Partial<EnhancerTargetFullOptions>) => {
    const newOptions = {
      ...options,
      ...patches,
    };

    setOptions(newOptions);
    log("Patch target options", newOptions);

    if (
      !newOptions.autoBindProperty &&
      (newOptions.propertyPool == undefined || newOptions.propertyId == undefined)
    ) {
      return;
    }

    onChange?.(newOptions);
  };

  const dt = options.dynamicTarget ?? dynamicTarget;

  const targetLabel = descriptor.isDynamic ? (dt ?? t<string>("Default")) : descriptor.name;
  const isDefaultTargetOfDynamic = descriptor.isDynamic && dt == undefined;
  const integratedSpecialTextType = StdValueSpecialTextIntegrationMap[descriptor.valueType];

  let property: IProperty | undefined;

  if (options.propertyPool != undefined && options.propertyId != undefined) {
    property = propertyMap?.[options.propertyPool]?.[options.propertyId];
  }

  const noPropertyBound =
    !options.autoBindProperty && (!options.propertyId || !options.propertyPool);

  log(props, options, propsOptions, property);

  return (
    <div className={"flex items-center gap-1"}>
      <div className={"w-[80px] flex justify-center"}>
        {isDefaultTargetOfDynamic ? (
          "-"
        ) : noPropertyBound ? (
          <Chip color={"warning"} size={"sm"} variant={"light"}>
            <AiOutlineClose className={"text-lg text-warning"} />
          </Chip>
        ) : (
          <Chip color={"success"} size={"sm"} variant={"light"}>
            <AiOutlineCheckCircle className={"text-lg"} />
          </Chip>
        )}
      </div>
      <div className={"w-4/12"}>
        <div className={"flex flex-col gap-2"}>
          <div className={"flex items-center gap-1"}>
            {descriptor.isDynamic && !isDefaultTargetOfDynamic ? (
              editingDynamicTarget ? (
                <Autocomplete
                  allowsCustomValue
                  isRequired
                  defaultInputValue={dynamicTargetInputValueRef.current}
                  errorMessage={dynamicTargetError}
                  isInvalid={dynamicTargetError != undefined}
                  items={dynamicTargetCandidates?.map((k) => ({ label: k, value: k })) ?? []}
                  size={"sm"}
                  onBlur={() => {
                    if (
                      dynamicTargetInputValueRef.current != undefined &&
                      dynamicTargetInputValueRef.current.length > 0
                    ) {
                      patchTargetOptions({
                        dynamicTarget: dynamicTargetInputValueRef.current,
                      });
                    }
                    dynamicTargetInputValueRef.current = undefined;
                    setEditingDynamicTarget(false);
                  }}
                  onInputChange={(v) => {
                    if (validateDynamicTarget(v)) {
                      dynamicTargetInputValueRef.current = v;
                    }
                  }}
                >
                  {(c) => <AutocompleteItem key={c.value}>{c.label}</AutocompleteItem>}
                </Autocomplete>
              ) : (
                <Button
                  // size={'sm'}
                  variant={"light"}
                  // color={'success'}
                  onClick={() => {
                    dynamicTargetInputValueRef.current = dt;
                    setEditingDynamicTarget(true);
                  }}
                >
                  {targetLabel ?? t<string>("Click to specify target")}
                  <EditOutlined className={"text-base"} />
                </Button>
              )
            ) : (
              <>
                <BriefProperty
                  fields={["name", "type"]}
                  property={{
                    type: descriptor.propertyType,
                    name: descriptor.name,
                  }}
                />
                {/* {targetLabel} */}
                {integratedSpecialTextType && (
                  <IntegrateWithSpecialTextLabel type={integratedSpecialTextType} />
                )}
                {descriptor.description && (
                  <Tooltip
                    className="max-w-[400px]"
                    color="secondary"
                    content={descriptor.description}
                    placement={"right"}
                  >
                    <QuestionCircleOutlined className={"text-base"} />
                  </Tooltip>
                )}
              </>
            )}
          </div>
          {/* <div className={'flex items-center gap-1 opacity-60'}> */}
          {/*   <StandardValueIcon valueType={target.valueType} className={'text-small'} /> */}
          {/*   {t<string>(`StandardValueType.${StandardValueType[target.valueType]}`)} */}
          {/* </div> */}
        </div>
      </div>
      <div className={"w-1/4"}>
        {isDefaultTargetOfDynamic ? (
          <Chip variant={"light"}>/</Chip>
        ) : (
          <div className={"flex items-center gap-1"}>
            <PropertyMatcher
              isClearable
              matchedProperty={property}
              name={descriptor.isDynamic ? options.dynamicTarget : descriptor.name}
              type={descriptor.propertyType}
              onValueChanged={(property) => {
                if (!property) {
                  onDeleted?.();

                  return;
                }
                patchTargetOptions({
                  propertyId: property.id,
                  propertyPool: property.pool,
                });
                onPropertyChanged?.();
              }}
            />
          </div>
        )}
      </div>
      <div className={"w-1/4"}>
        <div className={"flex flex-col gap-2"}>
          <TargetOptions
            isDisabled={!options.autoBindProperty && (!options.propertyId || !options.propertyPool)}
            options={options}
            optionsItems={descriptor.optionsItems}
            onChange={patchTargetOptions}
          />
        </div>
      </div>
      <div className={"w-1/12"}>
        <div className={"flex flex-col gap-1"}>
          {descriptor.isDynamic && !isDefaultTargetOfDynamic && (
            <Button
              isIconOnly
              color={"danger"}
              size={"sm"}
              variant={"light"}
              onPress={() => {
                onDeleted?.();
              }}
            >
              <DeleteOutlined className={"text-base"} />
            </Button>
          )}
        </div>
      </div>
    </div>
  );
};

TargetRow.displayName = "TargetRow";

export default TargetRow;
