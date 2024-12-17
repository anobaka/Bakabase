import { useTranslation } from 'react-i18next';
import { useEffect, useState } from 'react';
import { DeleteOutlined, FileUnknownOutlined, SyncOutlined } from '@ant-design/icons';
import { Button, Card, CardBody, CardHeader, Divider, Modal } from '@/components/bakaui';
import BApi from '@/sdk/BApi';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import ResourceTransferModal from '@/components/ResourceTransferModal';

type Props = {
  onHandled?: () => any;
};

export default ({ onHandled }: Props) => {
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

  if (count > 0) {
    return (
      <Button
        variant={'light'}
        size={'sm'}
        color={'warning'}
        onClick={() => {
          createPortal(Modal, {
            defaultVisible: true,
            title: t('Choose a method to handle unknown resources'),
            size: 'lg',
            children: (
              <div className={'flex flex-col gap-4 mb-4'}>
                <div>
                  <div>
                    {t('When the system fails to find the file or folder corresponding to the resource path, the resource is marked as an unknown resource.')}
                  </div>
                  <div>
                    {t('In most cases, this is caused by changes in the names of files or folders.')}
                  </div>
                </div>
                <div className={'flex items-start gap-2 justify-around'}>
                  <Card
                    isHoverable
                    className="w-[300px]"
                    isPressable
                    onPress={() => {
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
                    <CardHeader className="flex gap-3 text-lg">
                      <SyncOutlined className={'text-success'} />
                      {t('Transfer data')}
                    </CardHeader>
                    <Divider />
                    <CardBody>
                      <div>{t('You can transfer the data of unknown resources to existing resources.')}</div>
                      <div>{t('You can also transfer the data of some of unknown resources to existing resources first, and then delete the remaining unknown resources.')}</div>
                    </CardBody>
                  </Card>
                  <Card
                    isHoverable
                    className="w-[300px]"
                    isPressable
                    onPress={() => {
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
                          onHandled?.();
                        },
                      });
                    }}
                  >
                    <CardHeader className="flex gap-3 text-lg">
                      <DeleteOutlined className={'text-danger'} />
                      {t('Delete {{count}} unknown resources permanently', { count })}
                    </CardHeader>
                    <Divider />
                    <CardBody>
                      <div>{t('You can delete all unknown resources permanently.')}</div>
                    </CardBody>
                  </Card>
                </div>
              </div>
            ),
            onClose: () => {
              onHandled?.();
            },
            footer: false,
          });
        }}
      >
        <FileUnknownOutlined className={'text-base'} />
        {t('Handle {{count}} unknown resources', { count })}
      </Button>
    );
  }

  return null;
};
