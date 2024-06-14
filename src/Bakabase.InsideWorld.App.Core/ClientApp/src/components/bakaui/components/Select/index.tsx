import type { SelectProps as NextUISelectProps } from '@nextui-org/react';
import { Select, SelectItem } from '@nextui-org/react';
import type { Key } from '@react-types/shared';
import type { ReactNode } from 'react';
import { Chip } from '@/components/bakaui';


export interface SelectProps extends Omit<NextUISelectProps, 'children'> {
  dataSource?: { label: any; value: Key; textValue?: string }[];
  children?: any;
}

export default ({
                              dataSource = [],
                              ...props
                            }: SelectProps) => {
  const isMultiline = props.selectionMode === 'multiple';

  // console.log(props.selectedKeys, dataSource);
  const renderValue = props.renderValue ?? (isMultiline ? (v => {
    if (v.length > 0) {
      return (
        <div className={'flex flex-wrap gap-2'}>
          {v.reduce<ReactNode[]>((s, t, i) => {
            s.push(<Chip>{dataSource.find(d => d.value.toString() === t.textValue)?.label}</Chip>);
            return s;
          }, [])}
        </div>
      );
    }
    return null;
  }) : undefined);

  return (
    <Select
      isMultiline={isMultiline}
      renderValue={renderValue}
      {...props}
    >
      {dataSource.map(d => {
        return (
          <SelectItem
            key={d.value}
            value={d.value}
            textValue={d.textValue ?? d.label.toString()}
          >
            {d.label}
          </SelectItem>
        );
      }) ?? []}
    </Select>
  );
};
