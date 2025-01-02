import { QuestionCircleOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { Tooltip } from '@/components/bakaui';

export default () => {
  const { t } = useTranslation();
  return (
    <div className={'flex items-center gap-1'}>
      {t('Scope')}
      <Tooltip content={(
        <div className={'flex flex-col gap-1'}>
          <div>{t('A property may have multiple dimensional values, and you can choose one of the dimensions as the base data.')}</div>
        </div>
      )}
      >
        <QuestionCircleOutlined className={'text-base'} />
      </Tooltip>
    </div>
  );
};
