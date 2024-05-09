import type { ValueRendererProps } from '../models';
import StringValueRenderer from './StringValueRenderer';
type NumberValueRendererProps = ValueRendererProps<number>;

export default ({ value, ...props }: NumberValueRendererProps) => {
  return (
    <StringValueRenderer
      value={value?.toString()}
      {...props}
    />
  );
};
