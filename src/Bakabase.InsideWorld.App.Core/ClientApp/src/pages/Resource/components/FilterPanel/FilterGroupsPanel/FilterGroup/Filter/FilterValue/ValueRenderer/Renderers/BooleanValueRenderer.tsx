import { useTranslation } from 'react-i18next';
import type { ValueRendererProps } from '../models';
import StringValueRenderer from './StringValueRenderer';

type BooleanValueRendererProps = ValueRendererProps<boolean>;

export default ({ value, ...props }: BooleanValueRendererProps) => {
  const { t } = useTranslation();

  const str = value === true ? t('Boolean.True') : value === false ? t('Boolean.False') : undefined;

  return (
    <StringValueRenderer
      value={str}
      {...props}
    />
  );
};
