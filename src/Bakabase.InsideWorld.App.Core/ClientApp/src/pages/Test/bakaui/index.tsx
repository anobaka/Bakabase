import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useHistory } from 'react-router-dom';
import { history } from 'ice';
import { Button, Icon, Modal, Tooltip } from '@/components/bakaui';
import ClickableIcon from '@/components/ClickableIcon';
import EnhancerSelectorV2 from '@/components/EnhancerSelectorV2';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';

export default () => {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(false);

  const { createPortal } = useBakabaseContext();

  useEffect(() => {
    createPortal(Modal, {
      children: (<div onClick={() => {
        history.push('/');
      }}
      >ddd</div>),
      defaultVisible: true,
    });
  }, []);

  return (
    <>
      <Tooltip
        content={t('Bulk operations')}
      >
        <ClickableIcon
          colorType={'normal'}
          type={'Multiselect'}
        />
      </Tooltip>

      <Button color={'primary'} onClick={() => setVisible(true)}>Open Modal</Button>
    </>
  );
};
