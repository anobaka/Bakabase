import { Badge, Dialog, Radio } from '@alifd/next';
import { useCallback, useEffect, useState } from 'react';
import { createPortalOfComponent } from '@/components/utils';
import BApi from '@/sdk/BApi';
import { PasswordSearchOrder, passwordSearchOrders } from '@/sdk/constants';

import './index.scss';
import { useTranslation } from 'react-i18next';
import ClickableIcon from '@/components/ClickableIcon';

interface IProps {
  onSelect: (password: string) => any;
  afterClose?: () => any;
}

function PasswordSelector(props: IProps) {
  const {
    onSelect,
    afterClose,
  } = props;
  const { t } = useTranslation();

  const [passwords, setPasswords] = useState<{ text: string; usedTimes: number; lastUsedAt: string }[]>([]);
  const [visible, setVisible] = useState(true);
  const [order, setOrder] = useState(PasswordSearchOrder.Latest);

  const initialize = useCallback(async () => {
    const rsp = await BApi.password.getAllPasswords();
    // @ts-ignore
    setPasswords(rsp.data || []);
  }, []);

  useEffect(() => {
    initialize();
  }, []);

  const close = useCallback(() => {
    setVisible(false);
  }, []);

  const renderPasswords = useCallback(() => {
    const filtered = passwords.sort((a, b) => (order === PasswordSearchOrder.Latest ? (b.lastUsedAt.localeCompare(a.lastUsedAt)) : (b.usedTimes - a.usedTimes)));
    return filtered.map(p => {
      return (
        <div
          className="password"
          onClick={() => {
            onSelect(p.text);
            close();
          }}
        >
          <div className="top">
            <div className="text">{p.text}</div>
            <ClickableIcon
              type={'delete'}
              colorType={'danger'}
              onClick={(e) => {
                e.stopPropagation();
                e.preventDefault();
                Dialog.confirm({
                  title: t('Delete password from history?'),
                  closeable: true,
                  onOk: () => BApi.password.deletePassword(p.text),
                });
              }}
            />
          </div>
          <div className="info">
            <div className="last-used-at">{p.lastUsedAt}</div>
            <Badge count={p.usedTimes} />
            {/* <div className="used-times">{p.usedTimes}</div> */}
          </div>
        </div>
      );
    });
  }, [passwords, order]);

  return (
    <Dialog
      title={t('Passwords')}
      afterClose={afterClose}
      visible={visible}
      className={'password-selector-dialog'}
      v2
      width={'auto'}
      footerActions={['cancel']}
      closeMode={['close', 'mask', 'esc']}
      onClose={close}
      onCancel={close}
    >
      <div className="orders">
        <div className="title">{t('Order')}</div>
        <div className="content">
          <Radio.Group
            value={order}
            dataSource={[
              {
                label: t('Used recently'),
                value: PasswordSearchOrder.Latest,
              },
              {
                label: t('Used frequently'),
                value: PasswordSearchOrder.Frequency,
              },
            ]}
            onChange={v => {
              // @ts-ignore
              setOrder(v);
            }}
          />
        </div>
      </div>
      <div className="passwords">
        <div className="title">{t('Passwords')}</div>
        <div className="content">
          {renderPasswords()}
        </div>
      </div>
    </Dialog>
  );
}

PasswordSelector.show = props => createPortalOfComponent(PasswordSelector, props);

export default PasswordSelector;
