import { Balloon } from '@alifd/next';
import CustomIcon from '@/components/CustomIcon';
import i18n from 'i18next';
import React from 'react';

export default ((props) => (
  <CustomIcon {...(props || {})}
              style={{ cursor: 'all-scroll' }} size={'small'} type={'menu'} title={i18n.t('Drag to sort')}
  />
));
