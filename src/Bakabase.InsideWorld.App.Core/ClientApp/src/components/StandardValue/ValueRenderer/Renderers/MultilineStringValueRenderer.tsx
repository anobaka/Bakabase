import { useTranslation } from 'react-i18next';
import type { ValueRendererProps } from '../models';

type MultilineStringValueRendererProps = ValueRendererProps<string>;

export default ({ value, onClick }: MultilineStringValueRendererProps) => {
  const { t } = useTranslation();

  return (
    <div
      onClick={onClick}
      className={'min-w-fit pl-2 pr-2'}
    >
      {(value === null || value === undefined || value.length == 0) ? t('Not set') : value}
    </div>
  );
};
