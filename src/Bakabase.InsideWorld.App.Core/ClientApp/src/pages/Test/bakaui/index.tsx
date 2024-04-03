import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { CloseCircleFilled } from '@ant-design/icons';
import { useDisclosure } from '@nextui-org/react';
import { Button, Icon, Modal, Tooltip } from '@/components/bakaui';
import ClickableIcon from '@/components/ClickableIcon';
import CustomIcon from '@/components/CustomIcon';

export default () => {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(false);

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
        onClose={() => setVisible(false)}
      >
        children
      </Modal>
    </>
  );
};
