import { useTranslation } from 'react-i18next';
import type { ValueRendererProps } from '../models';

type FormulaValueRendererProps = Omit<ValueRendererProps<string>, 'variant'> & {
  variant: ValueRendererProps<string>['variant'];
};

export default ({ value, variant, editor, ...props }: FormulaValueRendererProps) => {
  const { t } = useTranslation();

  const v = variant ?? 'default';

  return (
    <span>{t('Not supported')}</span>
  );
};
