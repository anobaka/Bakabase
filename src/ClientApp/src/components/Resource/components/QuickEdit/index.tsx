import { EditOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { Button, Tooltip } from '@/components/bakaui';

// todo: design
export default () => {
  const { t } = useTranslation();
  return (
    <div>
      <Tooltip
        content={(
          <div>
            <div>{t('Edit property values quickly')}</div>
            <div>{t('You can hover over the property name in the resource details to set the property as quickly editable')}</div>
          </div>
        )}
      >
        <Button
          size={'sm'}
          variant={'light'}
          radius={'sm'}
          isIconOnly
        >
          <EditOutlined className={'text-base'} />
        </Button>
      </Tooltip>
    </div>
  );
};
