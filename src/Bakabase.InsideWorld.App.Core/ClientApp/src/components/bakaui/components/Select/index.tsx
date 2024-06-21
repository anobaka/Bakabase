import type { SelectedItems, SelectProps as NextUISelectProps } from '@nextui-org/react';
import { Select, SelectItem } from '@nextui-org/react';
import type { Key } from '@react-types/shared';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Chip } from '@/components/bakaui';

type Data = { label?: any; value: Key; textValue?: string };

export interface SelectProps extends Omit<NextUISelectProps, 'children'> {
  dataSource?: Data[];
  children?: any;
}

export default ({
                              dataSource = [],
                              ...props
                            }: SelectProps) => {
  const { t } = useTranslation();

  const isMultiline = props.selectionMode === 'multiple';

  // console.log(props.selectedKeys, dataSource);
  const renderValue = props.renderValue ?? (isMultiline ? ((v: SelectedItems<Data>) => {
    // console.log(v);
    if (v.length > 0) {
      return (
        <div className={'flex flex-wrap gap-2'}>
          {v.reduce<ReactNode[]>((s, x, i) => {
            s.push(<Chip>{dataSource.find(d => d.value === x.data?.value)?.label ?? t('Unknown label')}</Chip>);
            return s;
          }, [])}
        </div>
      );
    }
    return undefined;
  }) : undefined);

  return (
    <Select
      items={dataSource ?? []}
      isMultiline={isMultiline}
      renderValue={renderValue}
      {...props}
    >
      {(data: Data) => {
        return (
          <SelectItem
            key={data.value}
            value={data.value}
            textValue={data.textValue ?? data.label?.toString()}
          >
            {data.label}
          </SelectItem>
        );
      }}
    </Select>
  );
};
