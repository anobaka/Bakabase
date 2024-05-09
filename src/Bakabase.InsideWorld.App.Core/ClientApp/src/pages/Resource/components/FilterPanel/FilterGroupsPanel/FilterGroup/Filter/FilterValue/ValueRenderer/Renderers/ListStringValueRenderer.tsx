import type { ValueRendererProps } from '../models';
import StringValueRenderer from './StringValueRenderer';

type ListStringValueRendererProps = ValueRendererProps<string[]>;

export default ({ value, ...props }: ListStringValueRendererProps) => {
  const combinedValue = value?.filter(v => v !== null && v !== undefined && v.length > 0).join(', ');

  return (
    <StringValueRenderer value={combinedValue} {...props} />
  );
};
