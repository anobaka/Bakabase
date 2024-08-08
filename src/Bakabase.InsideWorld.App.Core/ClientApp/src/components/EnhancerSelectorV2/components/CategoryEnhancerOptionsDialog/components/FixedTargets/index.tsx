import { useTranslation } from 'react-i18next';
import { TableCell } from '@nextui-org/react';
import TargetRow from '../TargetRow';
import type { EnhancerFullOptions } from '../../models';
import type { EnhancerDescriptor } from '../../../../models';
import { Table, TableBody, TableColumn, TableHeader, TableRow } from '@/components/bakaui';
import type { IProperty } from '@/components/Property/models';

interface Props {
  propertyMap?: { [key: number]: IProperty };
  options?: EnhancerFullOptions;
  category: { name: string; id: number; customPropertyIds?: number[] };
  enhancer: EnhancerDescriptor;
}

export default (props: Props) => {
  const { t } = useTranslation();

  const {
    propertyMap,
    options,
    category,
    enhancer,
  } = props;
  return (
    <>
      {/* NextUI doesn't support the wrap of TableRow, use div instead for now, waiting the updates of NextUI */}
      {/* see https://github.com/nextui-org/nextui/issues/729 */}
      <Table removeWrapper>
        <TableHeader>
          <TableColumn width={'41.666667%'}>{t('Enhancement target')}</TableColumn>
          <TableColumn width={'25%'}>{t('Save as property')}</TableColumn>
          <TableColumn width={'25%'}>{t('Other options')}</TableColumn>
          <TableColumn >{t('Operations')}</TableColumn>
        </TableHeader>
        {/* @ts-ignore */}
        <TableBody />
      </Table>
      <div className={'flex flex-col gap-y-2'}>
        {enhancer.targets.filter(t => !t.isDynamic).map((target) => {
          const targetOptions = options?.targetOptions?.find(x => x.target == target.id);
          const targetDescriptor = enhancer.targets.find(x => x.id == target.id)!;
          // console.log(target.name);

          return (
            <TargetRow
              key={`${target.id}-${targetOptions?.dynamicTarget}`}
              category={category}
              enhancer={enhancer}
              target={target.id}
              options={targetOptions}
              property={propertyMap?.[targetOptions?.propertyId ?? 0]}
              descriptor={targetDescriptor}
            />
          );
        })}
      </div>
    </>
  );
};
