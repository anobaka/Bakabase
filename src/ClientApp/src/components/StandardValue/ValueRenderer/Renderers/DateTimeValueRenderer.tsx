import type { Dayjs } from 'dayjs';
import { useEffect, useRef, useState } from 'react';
import type { ValueRendererProps } from '../models';
import { DateInput, TimeInput } from '@/components/bakaui';
import NotSet from '@/components/StandardValue/ValueRenderer/Renderers/components/NotSet';
import { buildLogger } from '@/components/utils';
type DateTimeValueRendererProps = ValueRendererProps<Dayjs> & {
  format?: string;
  as: 'datetime' | 'date';
};

const log = buildLogger('DateTimeValueRenderer');

export default (props: DateTimeValueRendererProps) => {
  const { value, format, as, variant, editor } = props;
  log(props);
  const [editing, setEditing] = useState(false);
  const valueRef = useRef<Dayjs>();

  const startEditing = editor ? () => {
    valueRef.current = value;
    setEditing(true);
  } : undefined;

  const f = format == undefined ? as == 'datetime' ? 'YYYY-MM-DD HH:mm:ss' : 'HH:mm:ss' : format;
  if (!editing) {
    if (value == undefined) {
      return (
        <NotSet onClick={startEditing} />
      );
    }
    if (variant == 'light') {
      return (
        <span onClick={startEditing}>{value?.format(f)}</span>
      );
    }
  }

  // console.log(as);

  return (
    <DateInput
      granularity={as == 'datetime' ? 'second' : 'day'}
      value={value}
      isReadOnly={!editor}
      onBlur={() => {
        log('onBlur', valueRef.current);
        editor?.onValueChange?.(valueRef.current, valueRef.current);
        setEditing(false);
      }}
      onKeyDown={e => {
        if (e.key == 'Enter') {
          log('onEnter', valueRef.current);
          editor?.onValueChange?.(valueRef.current, valueRef.current);
          setEditing(false);
        }
      }}
      onChange={d => {
        log('OnChange', d);
        valueRef.current = d;
      }}
    />
  );
};
