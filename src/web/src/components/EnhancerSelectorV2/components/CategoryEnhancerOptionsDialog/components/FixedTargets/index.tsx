"use client";

import type { EnhancerFullOptions } from "../../models";
import type { EnhancerDescriptor } from "../../../../models";
import type { IProperty } from "@/components/Property/models";
import type { PropertyPool } from "@/sdk/constants";

import { useTranslation } from "react-i18next";

import TargetRow from "../TargetRow";

import {
  Divider,
  Table,
  TableBody,
  TableColumn,
  TableHeader,
} from "@/components/bakaui";

interface Props {
  propertyMap?: { [key in PropertyPool]?: Record<number, IProperty> };
  options?: EnhancerFullOptions;
  category: { name: string; id: number; customPropertyIds?: number[] };
  enhancer: EnhancerDescriptor;
  onPropertyChanged?: () => any;
  onCategoryChanged?: () => any;
}

export default (props: Props) => {
  const { t } = useTranslation();

  const {
    propertyMap,
    options,
    category,
    enhancer,
    onPropertyChanged,
    onCategoryChanged,
  } = props;

  const fixedTargets = enhancer.targets.filter((t) => !t.isDynamic);

  return (
    <>
      {/* NextUI doesn't support the wrap of TableRow, use div instead for now, waiting the updates of NextUI */}
      {/* see https://github.com/nextui-org/nextui/issues/729 */}
      <Table removeWrapper aria-label={"Fixed targets"}>
        <TableHeader>
          <TableColumn width={"41.666667%"}>
            {t<string>("Enhancement target")}
          </TableColumn>
          <TableColumn width={"25%"}>
            {t<string>("Save as property")}
          </TableColumn>
          <TableColumn width={"25%"}>{t<string>("Other options")}</TableColumn>
          <TableColumn>{t<string>("Operations")}</TableColumn>
        </TableHeader>
        {/* @ts-ignore */}
        <TableBody />
      </Table>
      <div className={"flex flex-col gap-y-2"}>
        {fixedTargets.map((target, i) => {
          const targetOptions = options?.targetOptions?.find(
            (x) => x.target == target.id,
          );
          const targetDescriptor = enhancer.targets.find(
            (x) => x.id == target.id,
          )!;
          // console.log(target.name);

          return (
            <>
              <TargetRow
                key={`${target.id}-${targetOptions?.dynamicTarget}`}
                category={category}
                descriptor={targetDescriptor}
                enhancer={enhancer}
                options={targetOptions}
                propertyMap={propertyMap}
                target={target.id}
                onCategoryChanged={onCategoryChanged}
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
