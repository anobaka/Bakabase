import type { ValueEditorProps } from '../models';
import { Checkbox } from '@/components/bakaui';

type BooleanValueEditorProps = ValueEditorProps<boolean>;

export default (props: BooleanValueEditorProps) => {
  return (
    <Checkbox
      size={'sm'}
      isSelected={props.initValue}
      onValueChange={v => {
        props.onChange?.(v);
      }}
    />
  );
};
