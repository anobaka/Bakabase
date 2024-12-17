import { useTranslation } from 'react-i18next';
import { useEffect, useState } from 'react';
import { FileUnknownOutlined } from '@ant-design/icons';
import { Button, Divider, Modal } from '@/components/bakaui';
import BApi from '@/sdk/BApi';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import ResourceTransferModal from '@/components/ResourceTransferModal';

type Props = {
  onDeleted?: () => any;
};

export default ({ onDeleted }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [count, setCount] = useState(0);

  const init = async () => {
    const r = await BApi.resource.getUnknownResourcesCount();
    setCount(r.data ?? 0);
  };

  useEffect(() => {
    init();
  }, []);

  if (count == 0) {
    return (
      <Button
        variant={'light'}
        size={'sm'}
        color={'warning'}
        onClick={() => {
          createPortal(Modal, {
            defaultVisible: true,
            title: t('Choose a method to handle unknown resources'),
            children: (
              <div className={'flex flex-col gap-4'}>
                <div className={'flex flex-col gap-2'}>
                  <div>
                    <div>{t('You can transfer data of unknown resources to existed resources.')}</div>
                    <div>{t('You can also transfer some of unknown resources and then delete the rests.')}</div>
                  </div>
                  <div>
                    <Button
                      size={'sm'}
                      color={'primary'}
                      onClick={() => {
                        const loadingModal = createPortal(Modal, {
                          defaultVisible: true,
                          footer: false,
                          title: t('Loading unknown resources'),
                        });
                        BApi.resource.getUnknownResources().then(r => {
                          loadingModal.destroy();
                          createPortal(
                            ResourceTransferModal, {
                              fromResources: r.data || [],
                            },
                          );
                        });
                      }}
                    >
                      {t('Transfer data')}
                    </Button>
                  </div>
                </div>
                <Divider orientation={'horizontal'} />
                <div className={'flex flex-col gap-2'}>
                  <div>{t('You can discard all unknown resources permanently.')}</div>
                  <div>
                    <Button
                      size={'sm'}
                      color={'danger'}
                      onClick={() => {
                        createPortal(Modal, {
                          defaultVisible: true,
                          title: t('Delete {{count}} unknown resources permanently', { count }),
                          children: (
                            <div>
                              {t('Be careful, this operation can not be undone')}
                            </div>
                          ),
                          onOk: async () => {
                            await BApi.resource.deleteUnknownResources();
                            init();
                            onDeleted?.();
                          },
                        });
                      }}
                    >
                      {t('Delete {{count}} unknown resources permanently')}
                    </Button>
                  </div>
                </div>
              </div>
            ),
            footer: false,
          });
          // BApi.resource.deleteUnknownResources().then(r => {
          //   init();
          //   onDeleted?.();
          // });
        }}
      >
        <FileUnknownOutlined className={'text-base'} />
        {t('Handle {{count}} unknown resources', { count })}
      </Button>
    );
  }

  return null;
};
