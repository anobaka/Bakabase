import type { Dayjs } from 'dayjs';
import type { ValueEditorProps } from '../models';
import { DateInput } from '@/components/bakaui';

interface DateTimeValueEditorProps extends ValueEditorProps<Dayjs> {
  mode?: 'date' | 'datetime';
}

export default ({ mode = 'datetime', initValue, onChange, ...props }: DateTimeValueEditorProps) => {
    return (
      <DateInput
        size={'sm'}
        granularity={mode == 'datetime' ? 'day' : 'second'}
        value={initValue}
        onChange={onChange}
      />
  );
};
