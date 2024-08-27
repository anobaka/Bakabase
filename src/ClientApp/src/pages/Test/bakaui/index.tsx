import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useHistory } from 'react-router-dom';
import { history } from 'ice';
import { createPortal } from 'react-dom';
import { SyncOutlined } from '@ant-design/icons';
import type { Dayjs } from 'dayjs';
import dayjs from 'dayjs';
import { Button, DateInput, Icon, Modal, Popover, Tooltip } from '@/components/bakaui';
import ClickableIcon from '@/components/ClickableIcon';
import EnhancerSelectorV2 from '@/components/EnhancerSelectorV2';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';

export default () => {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(false);
  const [date, setDate] = useState<Dayjs>(dayjs(new Date(2005, 2, 2)));

  useEffect(() => {
    createPortal(<Modal
      defaultVisible
    />, document.body);
  }, []);

  return (
    <>
      <DateInput
        value={date}
        onChange={v => {
        console.log(v?.valueOf());
        setDate(v);
      }}
      />
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

      <Popover trigger={(
        <Button
          size={'sm'}
          isIconOnly
          variant={'light'}
        >
          <SyncOutlined className={'text-lg'} />
        </Button>
      )}
      >
        1232112321321
      </Popover>
    </>
  );
};
