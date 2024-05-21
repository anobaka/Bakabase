import { QuestionCircleOutlined } from '@ant-design/icons';
import React from 'react';
import { useTranslation } from 'react-i18next';

export default () => {
  const { t } = useTranslation();
  return (
    <div className={'flex items-start gap-2 border-small px-2 py-1 rounded-small border-default-200 opacity-80'}>
      <QuestionCircleOutlined className={'leading-normal block'} />
      <div className={'leading-normal'}>
        <div>
          {t('You can set conventions on each path segments to fill the properties of resources.')}
        </div>
        <div>
          {t('If your path layer isn\'t stable to root path but it is stable to resource, \'The xxx layer to the resource\' is your better choice.')}
        </div>
      </div>
    </div>
  );
};
