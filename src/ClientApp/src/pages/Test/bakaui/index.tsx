import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useHistory } from 'react-router-dom';
import { history } from 'ice';
import { createPortal } from 'react-dom';
import { SyncOutlined } from '@ant-design/icons';
import type { Dayjs } from 'dayjs';
import dayjs from 'dayjs';
import { Button, Carousel, DateInput, Icon, Modal, NumberInput, Popover, Tooltip } from '@/components/bakaui';
import ClickableIcon from '@/components/ClickableIcon';
import EnhancerSelectorV2 from '@/components/EnhancerSelectorV2';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';

const contentStyle: React.CSSProperties = {
  margin: 0,
  height: '100%',
  color: '#fff',
  textAlign: 'center',
  background: '#364d79',
};

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
      <NumberInput
        label="Amount"
        placeholder="Enter the amount"
        value={123}
        onValueChange={console.log}
      />
      <div className={'w-[400px] h-[400px]'}>
        <Carousel>
          <img
            src={'http://localhost:5001/tool/thumbnail?path=C%3A%2FUsers%2Fanoba%2FAppData%2FRoaming%2FBakabase.Debugging%2Fdata%2Fenhancer%2F4%2F8907%2Fcover.5.RJ01248749_img_smp5.jpg'}
          />
          <img
            src={'http://localhost:5001/tool/thumbnail?path=C%3A%2FUsers%2Fanoba%2FAppData%2FRoaming%2FBakabase.Debugging%2Fdata%2Fenhancer%2F4%2F8907%2Fcover.5.RJ01248749_img_smp5.jpg'}
          />
          <img
            src={'http://localhost:5001/tool/thumbnail?path=C%3A%2FUsers%2Fanoba%2FAppData%2FRoaming%2FBakabase.Debugging%2Fdata%2Fenhancer%2F4%2F8907%2Fcover.5.RJ01248749_img_smp5.jpg'}
          />
          <img
            src={'http://localhost:5001/tool/thumbnail?path=C%3A%2FUsers%2Fanoba%2FAppData%2FRoaming%2FBakabase.Debugging%2Fdata%2Fenhancer%2F4%2F8907%2Fcover.5.RJ01248749_img_smp5.jpg'}
          />
        </Carousel>
      </div>
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
