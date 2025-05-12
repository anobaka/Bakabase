import { useTranslation } from 'react-i18next';
import { QuestionCircleOutlined } from '@ant-design/icons';
import { Popover } from '@/components/bakaui';

export default () => {
  const { t } = useTranslation();
  return (
    <Popover
      trigger={(
        <QuestionCircleOutlined className={'text-medium'} />
      )}
    >
      <div className={'flex flex-col gap-1'}>
        <div>
          <div className={'font-bold text-medium'}>{t('Auto bind property')}</div>
          <div>
            <div>{t('If this option is checked, a property with the same name and type of the target will be bound automatically.')}</div>
            <div>{t('If there isn\'t such a property, a new property will be created and bound to this target.')}</div>
            <div>{t('If this option is checked in default options of a dynamic target, all unlisted dynamic targets will be bound to properties of the same type with the same name.')}</div>
          </div>
        </div>
        <div>
          <div className={'font-bold text-medium'}>{t('Auto match on empty values')}</div>
          <div>
            <div>{t('By default, an empty layer will be created if we meet an empty value in multilevel data.')}</div>
            <div>{t('If checked, this kind of data will be matched with the most similar multilevel value in the database.')}</div>
            <div>{t('For example, assume we already have a multilevel data: a->b->c, then for the incoming data: ->->c, the values of 1st and 2nd layers of which are empty.')}</div>
            <div>{t('If this option is checked, we\'ll save ->->c as a->b->c, otherwise the incoming value ->->c will be saved.')}</div>
            <div>{t('This option may cause unexpected behaviors, make sure you have enough confidence to merge the produced data into data in db before check it.')}</div>
          </div>
        </div>
      </div>
    </Popover>
  );
};
