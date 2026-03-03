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
  hideBindingAndConfig?: boolean;
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
    // Only include dynamic targets that have a name (non-empty dynamicTarget)
    const subOptions = (optionsList?.filter((x) => x.target == descriptor.id && x.dynamicTarget != undefined) || []);

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

  const { propertyMap, optionsList, enhancer, onPropertyChanged, onChange, candidateTargetsMap, hideBindingAndConfig } =
    props;
  const dynamicTargetDescriptors = enhancer.targets.filter((x) => x.isDynamic);
  const [groups, setGroups] = useState<Group[]>([]);

  useEffect(() => {
    const newGroups = buildGroups(dynamicTargetDescriptors, optionsList, candidateTargetsMap);

    // When candidates exist (e.g. regex capture groups), sync targets to match exactly:
    // add missing candidates, remove stale targets no longer in candidates.
    let changed = false;
    for (const group of newGroups) {
      const candidates = group.candidateTargets;
      if (!candidates || candidates.length === 0) continue;

      const candidateSet = new Set(candidates);
      const existingByName = new Map(
        group.subOptions
          .filter((x) => x.dynamicTarget != undefined)
          .map((x) => [x.dynamicTarget!, x]),
      );

      const synced: EnhancerTargetFullOptions[] = [];
      for (const candidate of candidates) {
        const existing = existingByName.get(candidate);
        if (existing) {
          synced.push(existing);
        } else {
          const newOpts = createEnhancerTargetOptions(group.descriptor);
          newOpts.dynamicTarget = candidate;
          synced.push(newOpts);
          changed = true;
        }
      }

      // Keep manually added targets (those not matching any candidate)
      // only when they were NOT previously driven by candidates
      // For candidate-driven groups, stale targets are removed
      if (synced.length !== group.subOptions.length) {
        changed = true;
      }

      group.subOptions = synced;
    }

    setGroups(newGroups);

    if (changed) {
      const ol = _.flatMap(newGroups, (g) => g.subOptions);
      onChange?.(ol);
    }
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
        const isCandidateDriven = candidateTargets && candidateTargets.length > 0;
        const hasUnbound = subOptions.some((x) => x.dynamicTarget != undefined && (!x.propertyId || !x.propertyPool));

        return (
          <div>
            {/* NextUI doesn't support the wrap of TableRow, use div instead for now, waiting the updates of NextUI */}
            {/* see https://github.com/nextui-org/nextui/issues/729 */}
            {hideBindingAndConfig && isCandidateDriven ? (
              <Table removeWrapper aria-label={"Dynamic target"}>
                <TableHeader>
                  <TableColumn>
                    {descriptor.name}
                    &nbsp;
                    <Popover trigger={<ApartmentOutlined className={"text-base"} />}>
                      {t<string>(
                        "enhancer.target.dynamicLabel.tip",
                      )}
                    </Popover>
                  </TableColumn>
                </TableHeader>
                {/* @ts-ignore */}
                <TableBody />
              </Table>
            ) : hideBindingAndConfig ? (
              <Table removeWrapper aria-label={"Dynamic target"}>
                <TableHeader>
                  <TableColumn width="80%">
                    {descriptor.name}
                    &nbsp;
                    <Popover trigger={<ApartmentOutlined className={"text-base"} />}>
                      {t<string>(
                        "enhancer.target.dynamicLabel.tip",
                      )}
                    </Popover>
                  </TableColumn>
                  <TableColumn>{t<string>("common.label.operations")}</TableColumn>
                </TableHeader>
                {/* @ts-ignore */}
                <TableBody />
              </Table>
            ) : isCandidateDriven ? (
              <Table removeWrapper aria-label={"Dynamic target"}>
                <TableHeader>
                  <TableColumn align={"center"} width={80}>
                    {t<string>("enhancer.target.configured.label")}
                  </TableColumn>
                  <TableColumn width={"33.3333%"}>
                    {descriptor.name}
                    &nbsp;
                    <Popover trigger={<ApartmentOutlined className={"text-base"} />}>
                      {t<string>(
                        "enhancer.target.dynamicLabel.tip",
                      )}
                    </Popover>
                  </TableColumn>
                  <TableColumn width={"25%"}>
                    <div className={"flex items-center gap-1"}>
                      {t<string>("enhancer.target.bindProperty.label")}
                      {hasUnbound && (
                        <PropertiesMatcher
                          properties={subOptions
                            .filter((x) => x.dynamicTarget != undefined)
                            .map((td) => ({
                              type: descriptor.propertyType,
                              name: td.dynamicTarget!,
                            }))}
                          onValueChanged={(ps) => {
                            const namedOptions = subOptions.filter((x) => x.dynamicTarget != undefined);
                            for (let i = 0; i < ps.length; i++) {
                              const p = ps[i];

                              if (p) {
                                const idx = subOptions.indexOf(namedOptions[i]);
                                subOptions[idx] = {
                                  ...subOptions[idx],
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
                      {t<string>("enhancer.target.otherOptions.label")}
                      <OtherOptionsTip />
                    </div>
                  </TableColumn>
                </TableHeader>
                {/* @ts-ignore */}
                <TableBody />
              </Table>
            ) : (
              <Table removeWrapper aria-label={"Dynamic target"}>
                <TableHeader>
                  <TableColumn align={"center"} width={80}>
                    {t<string>("enhancer.target.configured.label")}
                  </TableColumn>
                  <TableColumn width={"33.3333%"}>
                    {descriptor.name}
                    &nbsp;
                    <Popover trigger={<ApartmentOutlined className={"text-base"} />}>
                      {t<string>(
                        "enhancer.target.dynamicLabel.tip",
                      )}
                    </Popover>
                  </TableColumn>
                  <TableColumn width={"25%"}>
                    <div className={"flex items-center gap-1"}>
                      {t<string>("enhancer.target.bindProperty.label")}
                      {hasUnbound && (
                        <PropertiesMatcher
                          properties={subOptions
                            .filter((x) => x.dynamicTarget != undefined)
                            .map((td) => ({
                              type: descriptor.propertyType,
                              name: td.dynamicTarget!,
                            }))}
                          onValueChanged={(ps) => {
                            const namedOptions = subOptions.filter((x) => x.dynamicTarget != undefined);
                            for (let i = 0; i < ps.length; i++) {
                              const p = ps[i];

                              if (p) {
                                const idx = subOptions.indexOf(namedOptions[i]);
                                subOptions[idx] = {
                                  ...subOptions[idx],
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
                      {t<string>("enhancer.target.otherOptions.label")}
                      <OtherOptionsTip />
                    </div>
                  </TableColumn>
                  <TableColumn>{t<string>("common.label.operations")}</TableColumn>
                </TableHeader>
                {/* @ts-ignore */}
                <TableBody />
              </Table>
            )}
            <div className={"flex flex-col gap-y-2"}>
              {subOptions.map((data, i) => {
                return (
                  <>
                    <TargetRow
                      key={i}
                      descriptor={descriptor}
                      dynamicTarget={data.dynamicTarget}
                      dynamicTargetCandidates={candidateTargets}
                      hideBindingAndConfig={hideBindingAndConfig}
                      readOnly={isCandidateDriven}
                      options={data}
                      propertyMap={propertyMap}
                      onChange={(newOptions) => {
                        subOptions[i] = newOptions;
                        updateGroups(groups);
                      }}
                      onDeleted={isCandidateDriven ? undefined : () => {
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
            {!isCandidateDriven && (
              <Button
                color={"success"}
                size={"sm"}
                variant={"light"}
                onPress={() => {
                  const currentTargets = subOptions
                    .filter((x) => x.dynamicTarget != undefined)
                    .map((x) => x.dynamicTarget!);
                  const nextTarget = generateNextWithPrefix(t<string>("enhancer.target.label"), currentTargets);
                  const newOptions = createEnhancerTargetOptions(descriptor);

                  newOptions.dynamicTarget = nextTarget;
                  subOptions.push(newOptions);
                  updateGroups(groups);
                }}
              >
                <PlusCircleOutlined className={"text-sm"} />
                {t<string>("enhancer.target.specifyDynamic.action")}
              </Button>
            )}
          </div>
        );
      })}
    </div>
  ) : null;
};

DynamicTargets.displayName = "DynamicTargets";

export default DynamicTargets;
