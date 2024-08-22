
import { Trans, useTranslation } from 'react-i18next';
import { useEffect, useState } from 'react';
import { history } from 'ice';
import { Accordion, AccordionItem, Divider, Link, Modal, Snippet, Spacer } from '@/components/bakaui';
import BApi from '@/sdk/BApi';
import { createPortalOfComponent } from '@/components/utils';

interface IProps {
}

const ErrorModal = ({}: IProps) => {
  const { t } = useTranslation();

  const [appInfo, setAppInfo] = useState<{logPath: string}>();

  useEffect(() => {
    BApi.app.getAppInfo().then(rsp => {
      if (!rsp.code) {
        setAppInfo({
          logPath: rsp.data!.logPath!,
        });
      }
    });
  }, []);

  return (
    <Modal
      defaultVisible
      title={t('We have encountered some problems. You could try the following steps:')}
      size={'lg'}
      footer={{
          actions: ['cancel'],
        }}
    >
      <Accordion
        selectedKeys={'all'}
        selectionMode={'multiple'}
        isCompact
      >
        <AccordionItem
          key="1"
          title={(
            <span className={'font-bold'}>{t('Simply retry')}</span>
            )}
        >
          {t('Press \'F5\' to reload the page.')}
        </AccordionItem>
        <AccordionItem
          key="2"
          title={(
            <span className={'font-bold'}>{t('Restart the app')}</span>
            )}
        >
          {t('Shutdown and restart the app completely.')}
        </AccordionItem>
        <AccordionItem
          key="3"
          title={(
            <span className={'font-bold'}>{t('Contact support')}</span>
            )}
        >
          <div className={'flex flex-col gap-1 mb-2'}>
            <div>
              {t('You can find the latest log file at')}
              <Spacer y={1} />
              <Snippet
                size={'sm'}
                hideSymbol
                style={{ color: 'var(--bakaui-primary)' }}
                className={'cursor-pointer'}
                onClick={() => {
                    if (appInfo?.logPath) {
                      BApi.tool.openFileOrDirectory({ path: appInfo.logPath });
                    }
                  }}
              >
                <span className={'break-all whitespace-break-spaces'}>
                  {appInfo?.logPath}
                </span>
              </Snippet>
              <Spacer y={1} />
            </div>
            <div className={''}>
              <span className={'font-bold'}>
                {t('If you have programming experience,')}
              </span>
              &nbsp;
              {t('please provide the latest log file to the support team.')}
            </div>
            <div className={''}>
              <span className={'font-bold'}>
                {t('Otherwise,')}
              </span>
              &nbsp;
              {t('you can locate and collect the error messages in the log file, and provide they to the support team. (open an issue on github, or send to developer directly)')}
            </div>
            <div className={'mt-2'}>
              <Trans
                i18nKey={'ErrorHandlingModal.FindContactInConfigurationPage'}
              >
                You can find the concat at the bottom of
                <Link
                  size={'sm'}
                  className={'cursor-pointer'}
                  onClick={() => {
                      history!.push('/configuration');
                    }}
                >
                  {t('Configuration page')}
                </Link>
              </Trans>
            </div>
          </div>
        </AccordionItem>
        <AccordionItem
          key="4"
          title={(
            <span className={'font-bold'}>{t('Try other features')}</span>
            )}
        >
          <Link
            size={'sm'}
            className={'cursor-pointer'}
            onClick={() => {
                history!.push('/');
              }}
          >
            {t('Return to homepage')}
          </Link>
        </AccordionItem>
      </Accordion>
    </Modal>
  );
};

ErrorModal.show = (props: IProps) => createPortalOfComponent(ErrorModal, props);

export default ErrorModal;
