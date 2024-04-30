import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useHistory } from 'react-router-dom';
import { history } from 'ice';
import { Button, Icon, Modal, Tooltip } from '@/components/bakaui';
import ClickableIcon from '@/components/ClickableIcon';

export default () => {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(false);

  console.log(history);

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
      <Modal
        title={'title'}
        visible={visible}
        onClose={() => {
          console.log('12323112321');
          setVisible(false);
        }}
      >
        <div onClick={() => {
          history.push('/');
        }}
        >go</div>
      </Modal>
    </>
  );
};
