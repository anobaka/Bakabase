"use client";

import type { EnhancerTargetFullOptions } from "../../models";
import type { EnhancerDescriptor } from "../../../../models";
import type { IProperty } from "@/components/Property/models";
import type { PropertyPool } from "@/sdk/constants";

import { useTranslation } from "react-i18next";
import { useState } from "react";

import TargetRow from "../TargetRow";
import OtherOptionsTip from "../OtherOptionsTip";

import {
  Divider,
  Table,
  TableBody,
  TableColumn,
  TableHeader,
} from "@/components/bakaui";
import PropertiesMatcher from "@/components/PropertiesMatcher";

interface Props {
  propertyMap?: { [key in PropertyPool]?: Record<number, IProperty> };
  enhancer: EnhancerDescriptor;
  onPropertyChanged?: () => any;
  optionsList?: EnhancerTargetFullOptions[];
  onChange?: (options: EnhancerTargetFullOptions[]) => any;
  hideBindingAndConfig?: boolean;
}
const FixedTargets = (props: Props) => {
  const { t } = useTranslation();

  const {
    optionsList: propsOptionsList,
    propertyMap,
    enhancer,
    onPropertyChanged,
    onChange,
    hideBindingAndConfig,
  } = props;

  const [optionsList, setOptionsList] = useState<EnhancerTargetFullOptions[]>(
    propsOptionsList || [],
  );

  const fixedTargets = enhancer.targets.filter((t) => !t.isDynamic);

  const hasUnbound = fixedTargets.some((target) => {
    const to = optionsList.find((x) => x.target === target.id);
    return !to?.propertyId || !to?.propertyPool;
  });

  return (
    <>
      {/* NextUI doesn't support the wrap of TableRow, use div instead for now, waiting the updates of NextUI */}
      {/* see https://github.com/nextui-org/nextui/issues/729 */}
      {hideBindingAndConfig ? (
        <Table removeWrapper aria-label={"Fixed targets"}>
          <TableHeader>
            <TableColumn width="80%">
              {t<string>("enhancer.target.fixed.label")}
            </TableColumn>
            <TableColumn>{t<string>("common.label.operations")}</TableColumn>
          </TableHeader>
          {/* @ts-ignore */}
          <TableBody />
        </Table>
      ) : (
        <Table removeWrapper aria-label={"Fixed targets"}>
          <TableHeader>
            <TableColumn align={"center"} width={80}>
              {t<string>("enhancer.target.configured.label")}
            </TableColumn>
            <TableColumn width={"33.3333%"}>
              {t<string>("enhancer.target.fixed.label")}
            </TableColumn>
            <TableColumn width={"25%"}>
              <div className={"flex items-center gap-1"}>
                {t<string>("enhancer.target.bindProperty.label")}
                {hasUnbound && (
                  <PropertiesMatcher
                    properties={fixedTargets.map((td) => ({
                      type: td.propertyType,
                      name: td.name,
                    }))}
                    onValueChanged={(ps) => {
                      for (let i = 0; i < ps.length; i++) {
                        const p = ps[i];

                        if (p) {
                          const td = fixedTargets[i]!;
                          let to = optionsList.find((x) => x.target == td.id);

                          if (!to) {
                            to = { target: td.id };
                            optionsList.push(to);
                          }
                          to.propertyId = p.id;
                          to.propertyPool = p.pool;

                          if (propertyMap) {
                            const pMap = (propertyMap[p.pool] ??= {});
                            if (!(p.id in pMap)) {
                              pMap[p.id] = p;
                            }
                          }
                        }
                      }

                      setOptionsList(optionsList);
                      onChange?.(optionsList);
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
        {fixedTargets.map((target, i) => {
          const toIdx = optionsList.findIndex((x) => x.target == target.id);
          const to = optionsList[toIdx];
          const targetDescriptor = enhancer.targets.find(
            (x) => x.id == target.id,
          )!;
          // console.log(target.name);

          return (
            <>
              <TargetRow
                key={`${target.id}-${to?.dynamicTarget}`}
                descriptor={targetDescriptor}
                hideBindingAndConfig={hideBindingAndConfig}
                options={to}
                propertyMap={propertyMap}
                onChange={(o) => {
                  const newOptionsList = [...optionsList];

                  if (toIdx == -1) {
                    newOptionsList.push(o);
                  } else {
                    newOptionsList[toIdx] = o;
                  }
                  setOptionsList(newOptionsList);
                  onChange?.(newOptionsList);
                }}
                onDeleted={() => {
                  const newOptionsList = [...optionsList];

                  if (toIdx != -1) {
                    newOptionsList.splice(toIdx, 1);
                  }
                  setOptionsList(newOptionsList);
                  onChange?.(newOptionsList);
                }}
                onPropertyChanged={onPropertyChanged}
              />
              {fixedTargets.length - 1 !== i && (
                <Divider orientation={"horizontal"} />
              )}
            </>
          );
        })}
      </div>
    </>
  );
};

FixedTargets.displayName = "FixedTargets";

export default FixedTargets;
