import { Button, Dialog, Input, Message, Tag, Notification } from '@alifd/next';
import React, { useCallback, useRef, useState } from 'react';
import { Trans, useTranslation } from 'react-i18next';
import './index.scss';
import i18n from 'i18next';
import BApi from '@/sdk/BApi';
import { PasswordSearchOrder } from '@/sdk/constants';
import PasswordSelector from '@/components/PasswordSelector';
import { Popover, Tooltip } from '@/components/bakaui';

const QuickPasswordCount = 5;

interface IPassword {
  text: string;
  usedTimes: number;
  lastUsedAt: string;
}

interface Props {
  trigger: any;
  entry: {
    path: string;
  };
  passwords?: string[];
}

const loadTopNPasswords = async (type: PasswordSearchOrder): Promise<IPassword[]> => {
  const rsp = await BApi.password.searchPasswords({
    // @ts-ignore
    order: type,
    pageSize: QuickPasswordCount,
  });
  // @ts-ignore
  return rsp.data || [];
};

export default (props: Props) => {
  const {
    trigger,
    entry,
    passwords = [],
  } = props;
  const { t } = useTranslation();
  const customPasswordRef = useRef<string>();

  const [recentPasswords, setRecentPasswords] = useState<IPassword[]>([]);
  const [frequentlyUsedPasswords, setFrequentlyUsedPasswords] = useState<IPassword[]>([]);

  const [visible, setVisible] = useState(false);

  const decompress = useCallback((path, password?: string) => {
    // Notification.open({
    //   title: t('Start decompressing'),
    //   type: 'success',
    //   // content:
    //   //   "This is the content of the notification. This is the content of the notification. This is the content of the notification.",
    //   // type
    // });
    Message.notice(t('Start decompressing'));
    return BApi.file.decompressFiles({
      paths: [path],
      password,
    });
  }, []);

  const onBalloonVisible = useCallback(async () => {
    setRecentPasswords(await loadTopNPasswords(PasswordSearchOrder.Latest));
    setFrequentlyUsedPasswords(await loadTopNPasswords(PasswordSearchOrder.Frequency));
  }, []);

  const openPasswordSelector = useCallback(() => {
    PasswordSelector.show({
      onSelect: (password: string) => {
        decompress(entry.path, password);
      },
    });
  }, []);

  const renderTopNPasswords = useCallback((type: PasswordSearchOrder) => {
    let label;
    let passwords: IPassword[];
    switch (type) {
      case PasswordSearchOrder.Latest:
        label = 'recently used';
        passwords = recentPasswords;
        break;
      case PasswordSearchOrder.Frequency:
        label = 'frequently used';
        passwords = frequentlyUsedPasswords;
        break;
    }
    if (passwords.length > 0) {
      return (
        <div className={'secondary'}>
          <div className="tip">
            {t(`Alternatively, you can choose a password from ${label} passwords:`)}
            {passwords.length == 5 && (<Button
              className={'show-more'}
              text
              size={'small'}
              type={'primary'}
              onClick={openPasswordSelector}
            >{t('Show more')}</Button>)}
          </div>
          <div className="passwords">
            {passwords.map(p => {
              return (
                <Tag.Closeable
                  key={p.text}
                  size={'small'}
                  onClick={() => {
                    decompress(entry.path, p.text);
                  }}
                  onClose={(from) => {
                    Dialog.confirm({
                      title: t('Delete password from history?'),
                      closeable: true,
                      onOk: () => BApi.password.deletePassword(p.text),
                    });
                    return false;
                  }}
                >
                  {p.text}
                  {/* | {p.lastUsedAt} */}
                </Tag.Closeable>
              );
            })}
          </div>
        </div>
      );
    }
    return;
  }, [recentPasswords, frequentlyUsedPasswords]);

  return (
    <Tooltip
      content={(<div>
        <div className="common-tip">
          {t('Contents will be decompressed to the same directory as the compressed file.')}
        </div>
        <div className="password-guides">
          {passwords.length > 0 && (
            <div className={'default'}>

              <Trans
                i18nKey={'fp.te.db.defaultPassword'}
                values={{
                  password: passwords[0],
                }}
              >
                By default, we will use <Button type={'primary'} text>password</Button> as password.
              </Trans>
            </div>
          )}
          {passwords.length > 1 && (
            <div className={'secondary'}>
              <div className="tip">
                {t('Alternatively, you can choose a password from the following candidates:')}
              </div>
              <div className="passwords">
                {passwords.slice(1).map((password: string) => (
                  <Button
                    key={password}
                    size={'small'}
                    type={'normal'}
                    onClick={() => {
                      decompress(entry.path, password);
                    }}
                  >
                    {password}
                  </Button>
                ))}
              </div>
            </div>
          )}

          {renderTopNPasswords(PasswordSearchOrder.Latest)}
          {renderTopNPasswords(PasswordSearchOrder.Frequency)}

          <div className={'secondary'}>
            <div className="tip">{t('Or you can use a custom password:')}</div>
            <Input.Group addonAfter={(
              <Button
                type={'normal'}
                size={'small'}
                onClick={() => {
                  if (customPasswordRef.current) {
                    decompress(entry.path, customPasswordRef.current);
                  } else {
                    Message.error(i18n.t('Password can not be empty'));
                  }
                }}
              >
                {t('Use custom password to decompress')}
              </Button>
            )}
            >
              <Input
                size={'small'}
                placeholder={i18n.t('Password')}
                style={{ width: '100%' }}
                hasClear
                onKeyDown={e => {
                  e.stopPropagation();
                }}
                onChange={(v) => {
                  customPasswordRef.current = v;
                }}
              />
            </Input.Group>
          </div>
        </div>
      </div>)}
      onOpenChange={v => {
        if (v) {
          onBalloonVisible();
        }
      }}
      delay={1000}
      className="fp-te-db"
      placement={'left'}
      autoFocus={false}
      onClick={e => {
        e.stopPropagation();
      }}
    >
      {React.cloneElement(trigger, {
        onContextMenu: e => {
          // e.stopPropagation();
          // e.preventDefault();
          // setVisible(true);
        },
        onClick: (e) => {
          // console.log(e);
          // e.stopPropagation();
          // e.preventDefault();
          if (trigger.props.onClick) {
            trigger.props.onClick();
          }
          decompress(entry.path);
        },
      })}
    </Tooltip>
  );
};
