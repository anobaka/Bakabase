import { useTranslation } from 'react-i18next';
import type { ValueRendererProps } from '../models';
import type { EditableValueProps } from '../../models';
import { Checkbox, Switch } from '@/components/bakaui';

type FormulaValueRendererProps = Omit<ValueRendererProps<string[]>, 'variant'> & EditableValueProps<string[]> & {
  variant: ValueRendererProps<boolean>['variant'];
};

export default ({ value, variant, onValueChange, editable, ...props }: FormulaValueRendererProps) => {
  const { t } = useTranslation();

  const v = variant ?? 'default';

  return (
    <span>{t('Not supported')}</span>
  );
};
