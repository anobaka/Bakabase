import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useHistory } from 'react-router-dom';
import { history } from 'ice';
import { createPortal } from 'react-dom';
import { Button, Icon, Modal, Popover, Tooltip } from '@/components/bakaui';
import ClickableIcon from '@/components/ClickableIcon';
import EnhancerSelectorV2 from '@/components/EnhancerSelectorV2';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';

export default () => {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    createPortal(<Modal
      defaultVisible
    />, document.body);
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

      <Popover
        placement="right"
        closeMode={['esc', 'mask']}
        trigger={(
          <Button>Open Popover</Button>
        )}
      >
        <div className="px-1 py-2">
          <div className="text-small font-bold">Popover Content</div>
          <div className="text-tiny">This is the popover content</div>
        </div>
      </Popover>
    </>
  );
};
