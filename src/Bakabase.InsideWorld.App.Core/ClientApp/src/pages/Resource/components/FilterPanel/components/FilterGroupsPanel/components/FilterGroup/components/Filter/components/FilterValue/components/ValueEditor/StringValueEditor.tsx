import type {
  ValueEditorProps,
} from './models';
import { Input } from '@/components/bakaui';

export default (props: ValueEditorProps) => {
  return (
    <Input value={props.value} />
  );
};
