import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import TargetRow from '../TargetRow';
import type { EnhancerTargetFullOptions } from '../../models';
import type { EnhancerDescriptor } from '../../../../models';
import OtherOptionsTip from '../OtherOptionsTip';
import {
  Checkbox,
  Divider,
  Popover,
  Table,
  TableBody,
  TableColumn,
  TableHeader,
  TableRow,
  Tooltip,
} from '@/components/bakaui';
import type { IProperty } from '@/components/Property/models';
import type { PropertyPool } from '@/sdk/constants';

interface Props {
  propertyMap?: { [key in PropertyPool]?: Record<number, IProperty> };
  enhancer: EnhancerDescriptor;
  onPropertyChanged?: () => any;
  optionsList?: EnhancerTargetFullOptions[];
  onChange?: (options: EnhancerTargetFullOptions[]) => any;
}

export default (props: Props) => {
  const { t } = useTranslation();

  const {
    optionsList: propsOptionsList,
    propertyMap,
    enhancer,
    onPropertyChanged,
    onChange,
  } = props;

  const [optionsList, setOptionsList] = useState<EnhancerTargetFullOptions[]>(propsOptionsList || []);

  const fixedTargets = enhancer.targets.filter(t => !t.isDynamic);

  return (
    <>
      {/* NextUI doesn't support the wrap of TableRow, use div instead for now, waiting the updates of NextUI */}
      {/* see https://github.com/nextui-org/nextui/issues/729 */}
      <Table removeWrapper aria-label={'Fixed targets'}>
        <TableHeader>
          <TableColumn align={'center'} width={80}>{t('Configured')}</TableColumn>
          <TableColumn width={'33.3333%'}>{t('Enhancement target')}</TableColumn>
          <TableColumn width={'25%'}>
            {t('Bind property')}
          </TableColumn>
          <TableColumn width={'25%'}>
            <div className={'flex items-center gap-1'}>
              {t('Other options')}
              <OtherOptionsTip />
            </div>
          </TableColumn>
          <TableColumn>{t('Operations')}</TableColumn>
        </TableHeader>
        {/* @ts-ignore */}
        <TableBody />
      </Table>
      <div className={'flex flex-col gap-y-2'}>
        {fixedTargets.map((target, i) => {
          const toIdx = optionsList.findIndex(x => x.target == target.id);
          const to = optionsList[toIdx];
          const targetDescriptor = enhancer.targets.find(x => x.id == target.id)!;
          // console.log(target.name);

          return (
            <>
              <TargetRow
                key={`${target.id}-${to?.dynamicTarget}`}
                options={to}
                propertyMap={propertyMap}
                descriptor={targetDescriptor}
                onPropertyChanged={onPropertyChanged}
                onDeleted={() => {
                  const newOptionsList = [...optionsList];
                  if (toIdx != -1) {
                    newOptionsList.splice(toIdx, 1);
                  }
                  setOptionsList(newOptionsList);
                  onChange?.(newOptionsList);
                }}
                onChange={o => {
                  const newOptionsList = [...optionsList];
                  if (toIdx == -1) {
                    newOptionsList.push(o);
                  } else {
                    newOptionsList[toIdx] = o;
                  }
                  setOptionsList(newOptionsList);
                  onChange?.(newOptionsList);
                }}
              />
              {fixedTargets.length - 1 !== i && (
                <Divider orientation={'horizontal'} />
              )}
            </>
          );
        })}
      </div>
    </>
  );
};
