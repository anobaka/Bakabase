import { useTranslation } from 'react-i18next';
import type { ValueRendererProps } from '../models';
import { Chip } from '@/components/bakaui';

type StringValueRendererProps = ValueRendererProps<string>;

export default ({ value, onClick }: StringValueRendererProps) => {
  const { t } = useTranslation();

  return (
    <Chip
      onClick={onClick}
      color={'primary'}
      size={'sm'}
      variant={'light'}
      className={'cursor-pointer'}
    >
      {(value === null || value === undefined || value.length == 0) ? t('Not set') : value}
    </Chip>
  );
};
