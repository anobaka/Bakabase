import { useTranslation } from 'react-i18next';
import type { ValueRendererProps } from '../models';
import type { EditableValueProps } from '../../models';
import { Checkbox, Switch } from '@/components/bakaui';

type BooleanValueRendererProps = Omit<ValueRendererProps<boolean>, 'variant'> & EditableValueProps & {
  variant: ValueRendererProps<boolean>['variant'] | 'switch';
};

export default ({ value, variant, onValueChange, editable, ...props }: BooleanValueRendererProps) => {
  const { t } = useTranslation();

  const v = variant ?? 'default';

  switch (v) {
    case 'default':
    case 'light':
      return (
        <Checkbox
          size={'sm'}
          disableAnimation={!editable}
          checked={value}
          onValueChange={onValueChange}
        />
      );
    case 'switch':
      return (
        <Switch
          size={'sm'}
          disableAnimation={!editable}
          checked={value}
          onValueChange={onValueChange}
        />
      );
  }
};
