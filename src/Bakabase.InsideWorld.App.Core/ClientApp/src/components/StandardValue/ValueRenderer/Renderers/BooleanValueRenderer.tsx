import { useTranslation } from 'react-i18next';
import type { ValueRendererProps } from '../models';
import { Checkbox, Switch } from '@/components/bakaui';

type BooleanValueRendererProps = Omit<ValueRendererProps<boolean>, 'variant'> & {
  variant: ValueRendererProps<boolean>['variant'] | 'switch';
};

export default ({ value, variant, editor, ...props }: BooleanValueRendererProps) => {
  const { t } = useTranslation();

  const v = variant ?? 'default';

  switch (v) {
    case 'default':
    case 'light':
      return (
        <Checkbox
          size={'sm'}
          disableAnimation={!editor}
          checked={value}
          onValueChange={v => editor?.onValueChange?.(v, v)}
        />
      );
    case 'switch':
      return (
        <Switch
          size={'sm'}
          disableAnimation={!editor}
          checked={value}
          onValueChange={v => editor?.onValueChange?.(v, v)}
        />
      );
  }
};
