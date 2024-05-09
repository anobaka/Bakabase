import type { Duration } from 'dayjs/plugin/duration';
import type { ValueEditorProps } from '../models';
import { TimeInput } from '@/components/bakaui';

type TimeValueEditorProps = ValueEditorProps<Duration>;

export default ({ initValue, onChange, ...props }: TimeValueEditorProps) => {
    return (
      <TimeInput
        size={'sm'}
        value={initValue}
        onChange={onChange}
      />
  );
};
