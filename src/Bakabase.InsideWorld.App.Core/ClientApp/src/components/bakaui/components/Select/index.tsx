import { Select, SelectItem } from '@nextui-org/react';
import type { CollectionBase, Key } from '@react-types/shared';
import type { ReactNode } from 'react';
import type { SelectedItems } from '@nextui-org/select/dist/use-select';
import { Chip } from '@/components/bakaui';


export interface SelectProps<T> extends Omit<CollectionBase<T>, 'children'>, Omit<React.ComponentPropsWithoutRef<'select'>, 'size' | 'color' | 'children'> {
  size?: 'sm' | 'md' | 'lg';
  label?: string;
  selectionMode?: 'single' | 'multiple';
  style?: any;
  onSelectionChange?: (keys: Set<Key>) => any;
  dataSource?: { label: any; value: Key }[];
  selectedKeys?: 'all' | Iterable<Key>;
  renderValue?: (items: SelectedItems<T>) => ReactNode;
}

export default <T = object>({
                              dataSource = [],
                              ...props
                            }: SelectProps<T>) => {
  const isMultiline = props.selectionMode === 'multiple';
  return (
    <Select
      isMultiline={isMultiline}
      renderValue={v => {
        console.log(v, dataSource, 5555);
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
      }}
      {...props}
    >
      {dataSource.map(d => {
        return (
          <SelectItem key={d.value} value={d.value} textValue={d.value.toString()}>
            {d.label}
          </SelectItem>
        );
      }) ?? []}
    </Select>
  );
};
