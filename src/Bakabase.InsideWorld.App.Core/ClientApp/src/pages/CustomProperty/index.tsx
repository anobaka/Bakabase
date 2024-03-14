import './index.scss';
import { Button } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import { useEffect } from 'react';
import PropertyDialog from './components/PropertyDialog';

export default () => {
  const { t } = useTranslation();

  useEffect(() => {
    PropertyDialog.show({});
  }, []);

  return (
    <div id={'custom-property-page'}>
      <Button
        size={'small'}
        type={'primary'}
      >
        {t('Add')}
      </Button>
    </div>
  );
};
