"use client";

import type { EnhancerTargetFullOptions } from "../models";
import type { EnhancerDescriptor, EnhancerTargetDescriptor } from "../../../models";
import type { PropertyMap } from "@/components/types";

import { ApartmentOutlined, PlusCircleOutlined } from "@ant-design/icons";
import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";
import { useUpdate } from "react-use";
import _ from "lodash";

import { createEnhancerTargetOptions } from "../models";

import TargetRow from "./TargetRow";

import {
  Button,
  Divider,
  Popover,
  Table,
  TableBody,
  TableColumn,
  TableHeader,
} from "@/components/bakaui";
import { buildLogger, generateNextWithPrefix } from "@/components/utils";
import OtherOptionsTip from "@/components/EnhancerSelectorV2/components/EnhancerOptionsModal/components/OtherOptionsTip";
import PropertiesMatcher from "@/components/PropertiesMatcher";

type Props = {
  propertyMap?: PropertyMap;
  enhancer: EnhancerDescriptor;
  onPropertyChanged?: () => any;
  optionsList?: EnhancerTargetFullOptions[];
  onChange?: (options: EnhancerTargetFullOptions[]) => any;
  candidateTargetsMap?: Record<number, string[] | undefined>;
};

type Group = {
  descriptor: EnhancerTargetDescriptor;
  subOptions: EnhancerTargetFullOptions[];
  candidateTargets?: string[];
};

const log = buildLogger("DynamicTargets");

const buildGroups = (
  descriptors: EnhancerTargetDescriptor[],
  optionsList?: EnhancerTargetFullOptions[],
  candidateTargetsMap?: Record<number, string[] | undefined>,
) => {
  return descriptors.map((descriptor) => {
    const subOptions = optionsList?.filter((x) => x.target == descriptor.id) || [];
    let defaultOptions = subOptions.find((x) => x.dynamicTarget == undefined);

    if (defaultOptions == undefined) {
      defaultOptions = createEnhancerTargetOptions(descriptor);
    } else {
      const defaultIdx = subOptions.findIndex((x) => x == defaultOptions);

      subOptions.splice(defaultIdx, 1);
    }
    subOptions.splice(0, 0, defaultOptions);

    return {
      descriptor: descriptor,
      subOptions: subOptions,
      candidateTargets: candidateTargetsMap?.[descriptor.id],
    };
  });
};
const DynamicTargets = (props: Props) => {
  const { t } = useTranslation();
  const forceUpdate = useUpdate();

  const { propertyMap, optionsList, enhancer, onPropertyChanged, onChange, candidateTargetsMap } =
    props;
  const dynamicTargetDescriptors = enhancer.targets.filter((x) => x.isDynamic);
  const [groups, setGroups] = useState<Group[]>([]);

  useEffect(() => {
    setGroups(buildGroups(dynamicTargetDescriptors, optionsList, candidateTargetsMap));
  }, [candidateTargetsMap]);

  const updateGroups = (groups: Group[]) => {
    setGroups([...groups]);

    const ol = _.flatMap(groups, (g) => g.subOptions);

    onChange?.(ol);
  };

  log("rendering", optionsList, groups);

  return groups.length > 0 ? (
    <div className={"flex flex-col gap-y-2"}>
      {groups.map((g) => {
        const { descriptor, subOptions, candidateTargets } = g;
        const notEmptyTargets = subOptions.filter((x) => x.dynamicTarget != undefined);

        return (
          <div>
            {/* NextUI doesn't support the wrap of TableRow, use div instead for now, waiting the updates of NextUI */}
            {/* see https://github.com/nextui-org/nextui/issues/729 */}
            <Table removeWrapper aria-label={"Dynamic target"}>
              <TableHeader>
                <TableColumn align={"center"} width={80}>
                  {t<string>("Configured")}
                </TableColumn>
                <TableColumn width={"33.3333%"}>
                  {descriptor.name}
                  &nbsp;
                  <Popover trigger={<ApartmentOutlined className={"text-base"} />}>
                    {t<string>(
                      "This is not a fixed enhancement target, which will be replaced with other content when data is collected",
                    )}
                  </Popover>
                </TableColumn>
                <TableColumn width={"25%"}>
                  <div className={"flex items-center gap-1"}>
                    {t<string>("Bind property")}
                    {notEmptyTargets.length > 0 && (
                      <PropertiesMatcher
                        properties={notEmptyTargets.map((td) => ({
                          type: descriptor.propertyType,
                          name: td.dynamicTarget!,
                        }))}
                        onValueChanged={(ps) => {
                          for (let i = 0; i < ps.length; i++) {
                            const p = ps[i];

                            if (p) {
                              subOptions[i] = {
                                ...subOptions[i],
                                propertyId: p.id,
                                propertyPool: p.pool,
                                target: descriptor.id,
                              };

                              if (propertyMap) {
                                const pMap = (propertyMap[p.pool] ??= {});

                                if (!(p.id in pMap)) {
                                  pMap[p.id] = p;
                                }
                              }
                            }
                          }
                          updateGroups(groups);
                        }}
                      />
                    )}
                  </div>
                </TableColumn>
                <TableColumn width={"25%"}>
                  <div className={"flex items-center gap-1"}>
                    {t<string>("Other options")}
                    <OtherOptionsTip />
                  </div>
                </TableColumn>
                <TableColumn>{t<string>("Operations")}</TableColumn>
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
                      descriptor={descriptor}
                      dynamicTarget={data.dynamicTarget}
                      dynamicTargetCandidates={candidateTargets}
                      options={data}
                      propertyMap={propertyMap}
                      onChange={(newOptions) => {
                        subOptions[i] = newOptions;
                        updateGroups(groups);
                      }}
                      onDeleted={() => {
                        subOptions?.splice(i, 1);
                        updateGroups(groups);
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
              onPress={() => {
                const currentTargets = subOptions
                  .filter((x) => x.dynamicTarget != undefined)
                  .map((x) => x.dynamicTarget!);
                const nextTarget = generateNextWithPrefix(t<string>("Target"), currentTargets);
                const newOptions = createEnhancerTargetOptions(descriptor);

                newOptions.dynamicTarget = nextTarget;
                subOptions.push(newOptions);
                setGroups([...groups]);
              }}
            >
              <PlusCircleOutlined className={"text-sm"} />
              {t<string>("Specify dynamic target")}
            </Button>
          </div>
        );
      })}
    </div>
  ) : null;
};

DynamicTargets.displayName = "DynamicTargets";

export default DynamicTargets;
