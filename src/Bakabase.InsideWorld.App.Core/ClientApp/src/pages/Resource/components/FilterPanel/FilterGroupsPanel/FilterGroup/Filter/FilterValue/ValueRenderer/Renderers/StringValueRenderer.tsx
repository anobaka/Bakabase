import { useTranslation } from 'react-i18next';
import type { ValueRendererProps } from '../models';
import { Button, Chip } from '@/components/bakaui';

type StringValueRendererProps = ValueRendererProps<string>;

export default ({ value, onClick }: StringValueRendererProps) => {
  const { t } = useTranslation();

  return (
    <Button
      size={'sm'}
      onClick={onClick}
      color={'success'}
      variant={'light'}
      className={'min-w-fit pl-2 pr-2'}
    >
      {(value === null || value === undefined || value.length == 0) ? t('Not set') : value}
    </Button>
  );
};
