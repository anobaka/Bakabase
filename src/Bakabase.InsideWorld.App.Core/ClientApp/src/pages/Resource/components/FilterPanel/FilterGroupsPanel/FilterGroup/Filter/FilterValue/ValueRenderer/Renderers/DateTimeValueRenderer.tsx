import type { ValueRendererProps } from '../models';
import StringValueRenderer from './StringValueRenderer';
interface DateTimeValueRendererProps extends ValueRendererProps<{format: (template?: string) => string}> {
  format: string;
}

export default ({ value, format, ...props }: DateTimeValueRendererProps) => {
  return (
    <StringValueRenderer
      value={value?.format(format)}
      {...props}
    />
  );
};
