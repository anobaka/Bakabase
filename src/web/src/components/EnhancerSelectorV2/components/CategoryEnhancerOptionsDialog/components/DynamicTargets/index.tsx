"use client";

import type {
  EnhancerFullOptions,
  EnhancerTargetFullOptions,
} from "../../models";
import type {
  EnhancerDescriptor,
  EnhancerTargetDescriptor,
} from "../../../../models";
import type { IProperty } from "@/components/Property/models";
import type { PropertyPool } from "@/sdk/constants";

import { PlusCircleOutlined } from "@ant-design/icons";
import { useTranslation } from "react-i18next";
import { useEffect, useRef, useState } from "react";
import { useUpdate, useUpdateEffect } from "react-use";

import { defaultCategoryEnhancerTargetOptions } from "../../models";
import DynamicTargetLabel from "../DynamicTargetLabel";
import TargetRow from "../TargetRow";

import {
  Button,
  Divider,
  Table,
  TableBody,
  TableColumn,
  TableHeader,
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { buildLogger } from "@/components/utils";

const sortOptions = (
  a: EnhancerTargetFullOptions,
  b: EnhancerTargetFullOptions,
) => {
  if (a.dynamicTarget == undefined) {
    return -1;
  }
  if (b.dynamicTarget == undefined) {
    return 1;
  }

  return a.dynamicTarget.localeCompare(b.dynamicTarget);
};

interface Props {
  propertyMap?: { [key in PropertyPool]?: Record<number, IProperty> };
  options?: EnhancerFullOptions;
  category: { name: string; id: number; customPropertyIds?: number[] };
  enhancer: EnhancerDescriptor;
  onPropertyChanged?: () => any;
  onCategoryChanged?: () => any;
}

type Group = {
  descriptor: EnhancerTargetDescriptor;
  subOptions: EnhancerTargetFullOptions[];
};

const log = buildLogger("DynamicTargets");
const DynamicTargets = (props: Props) => {
  const { t } = useTranslation();
  const forceUpdate = useUpdate();

  const {
    propertyMap,
    options: propsOptions,
    category,
    enhancer,
    onPropertyChanged,
    onCategoryChanged,
  } = props;
  const dynamicTargetDescriptors = enhancer.targets.filter((x) => x.isDynamic);
  const [groups, setGroups] = useState<Group[]>([]);
  const [options, setOptions] = useState(propsOptions ?? {});
  const optionsRef = useRef(options);

  useEffect(() => {}, []);

  useUpdateEffect(() => {
    setOptions(propsOptions ?? {});
  }, [propsOptions]);

  useEffect(() => {
    optionsRef.current = options;
    updateGroups();
  }, [options]);

  const updateGroups = () => {
    const newGroups = dynamicTargetDescriptors.map((descriptor) => {
      const subOptions =
        optionsRef.current?.targetOptions?.filter(
          (x) => x.target == descriptor.id,
        ) || [];
      let defaultOptions = subOptions.find((x) => x.dynamicTarget == undefined);

      if (defaultOptions == undefined) {
        defaultOptions = defaultCategoryEnhancerTargetOptions(descriptor);
      } else {
        const defaultIdx = subOptions.findIndex((x) => x == defaultOptions);

        subOptions.splice(defaultIdx, 1);
      }
      subOptions.splice(0, 0, defaultOptions);

      return {
        descriptor: descriptor,
        subOptions: subOptions,
      };
    });

    setGroups(newGroups);
    log("updateGroups", newGroups);
  };

  const createNewOptions = (
    descriptor: EnhancerTargetDescriptor,
    otherOptions: EnhancerTargetFullOptions[] | undefined,
  ) => {
    const options: EnhancerTargetFullOptions =
      defaultCategoryEnhancerTargetOptions(descriptor);
    let maxNo = 0;

    if (otherOptions != undefined) {
      const regex = new RegExp(
        String.raw`^${t<string>("enhancer.target.label")}(?<no>\d+)$`,
        "g",
      );

      for (const o of otherOptions) {
        if (o.dynamicTarget != undefined) {
          regex.lastIndex = 0;
          const match = regex.exec(o.dynamicTarget)?.groups?.no;

          if (match != undefined) {
            const no = parseInt(match, 10);

            // console.log(`${o.dynamicTarget}`, no);
            if (no > maxNo) {
              maxNo = no;
            }
          }
        }
      }
    }
    // console.log('current max no', maxNo, otherOptions);
    options.dynamicTarget = `${t<string>("enhancer.target.label")}${maxNo + 1}`;

    return options;
  };

  log("rendering", options, groups);

  return groups.length > 0 ? (
    <div className={"flex flex-col gap-y-2"}>
      {groups.map((g) => {
        const { descriptor, subOptions } = g;

        return (
          <div>
            {/* NextUI doesn't support the wrap of TableRow, use div instead for now, waiting the updates of NextUI */}
            {/* see https://github.com/nextui-org/nextui/issues/729 */}
            <Table removeWrapper aria-label={"Dynamic target"}>
              <TableHeader>
                <TableColumn width={"41.666667%"}>
                  {descriptor.name}
                  &nbsp;
                  <DynamicTargetLabel />
                </TableColumn>
                <TableColumn width={"25%"}>
                  {t<string>("enhancer.target.saveAsProperty.label")}
                </TableColumn>
                <TableColumn width={"25%"}>
                  {t<string>("enhancer.target.otherOptions.label")}
                </TableColumn>
                <TableColumn>{t<string>("common.label.operations")}</TableColumn>
              </TableHeader>
              {/* @ts-ignore */}
              <TableBody />
            </Table>
            <div className={"flex flex-col gap-y-2"}>
              {subOptions.map((data, i) => {
                return (
                  <>
                    <TargetRow
                      key={i}
                      category={category}
                      descriptor={descriptor}
                      dynamicTarget={data.dynamicTarget}
                      enhancer={enhancer}
                      options={data}
                      propertyMap={propertyMap}
                      target={data.target}
                      onCategoryChanged={onCategoryChanged}
                      onChange={(newOptions) => {
                        Object.assign(data, newOptions);
                        forceUpdate();
                      }}
                      onDeleted={() => {
                        const idx = options.targetOptions?.findIndex(
                          (x) => x == data,
                        );

                        if (idx != undefined) {
                          options.targetOptions?.splice(idx, 1);
                          setOptions({ ...options });
                        }
                      }}
                      onPropertyChanged={onPropertyChanged}
                    />
                    <Divider orientation={"horizontal"} />
                  </>
                );
              })}
            </div>
            <Button
              color={"success"}
              size={"sm"}
              variant={"light"}
              onClick={async () => {
                const newTargetOptions = createNewOptions(
                  descriptor,
                  subOptions,
                );

                await BApi.category.patchCategoryEnhancerTargetOptions(
                  category.id,
                  enhancer.id,
                  {
                    target: descriptor.id,
                    dynamicTarget: newTargetOptions.dynamicTarget,
                  },
                  newTargetOptions,
                );
                options.targetOptions ??= [];
                options.targetOptions.push(newTargetOptions);
                setOptions({ ...options });
                console.log("updating options", options);
              }}
            >
              <PlusCircleOutlined className={"text-sm"} />
              {t<string>("enhancer.target.specifyDynamic.action")}
            </Button>
          </div>
        );
      })}
    </div>
  ) : null;
};

DynamicTargets.displayName = "DynamicTargets";

export default DynamicTargets;
