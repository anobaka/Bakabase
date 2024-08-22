import { useTranslation } from 'react-i18next';
import { Button } from '@/components/bakaui';

type Props = {
  onClick?: () => any;
};

export default (props: Props) => {
  const { t } = useTranslation();
  const { onClick } = props;

  if (onClick) {
    return (
      <Button
        onClick={onClick}
        radius={'sm'}
        variant={'light'}
        size={'sm'}
      >
        <span className={'opacity-40'}>{t('Click to set')}</span>
      </Button>
    );
  } else {
    return (
      <span className={'opacity-40'}>{t('Not set')}</span>
    );
  }
};
