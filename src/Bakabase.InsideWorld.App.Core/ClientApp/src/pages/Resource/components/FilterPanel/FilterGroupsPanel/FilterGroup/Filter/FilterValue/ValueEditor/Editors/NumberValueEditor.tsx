import type { ValueEditorProps } from '../models';
import { Input } from '@/components/bakaui';

type NumberValueEditorProps = ValueEditorProps<number>;

export default (props: NumberValueEditorProps) => {
  return (
    <Input
      value={props.initValue?.toString()}
      onValueChange={v => {
        const nStr = v?.match(/[\d,]+(\.\d+)?/)?.[0];
        const n = Number(nStr);
        const r = Number.isNaN(n) ? n : undefined;
        props.onChange?.(r);
      }}
    />
  );
};
