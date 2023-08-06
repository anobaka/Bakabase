import { Button, Dialog, Icon, Table } from '@alifd/next';
import dayjs from 'dayjs';
import React, { useCallback, useState } from 'react';
import { useTranslation } from 'react-i18next';
import BApi from '@/sdk/BApi';
import { createPortalOfComponent } from '@/components/utils';

interface IProps {
  enhancements: any[];
  resourceId: number;
}

function ResourceEnhancementsDialog(props: IProps) {
  const { enhancements: propsEnhancements = [], resourceId } = props;
  const [enhancements, setEnhancements] = useState<any[]>(propsEnhancements);
  const [visible, setVisible] = useState(true);
  const { t } = useTranslation();
  const [enhancing, setEnhancing] = useState(false);

  const close = useCallback(() => {
    setVisible(false);
  }, []);

  return (
    <Dialog
      title={t('Enhancement records')}
      visible={visible}
      closeable
      footerActions={['ok']}
      onOk={close}
      onClose={close}
      onCancel={close}
    >
      <Table dataSource={enhancements || []}>
        <Table.Column
          title={t('Enhancer')}
          dataIndex={'enhancerName'}
          width={200}
        />
        <Table.Column
          title={t('Status')}
          dataIndex={'success'}
          cell={(c) => (c ? (<Icon style={{ color: 'green' }} type="success" />) : (<Icon style={{ color: 'red' }} type="error" />))}
          width={80}
        />
        <Table.Column
          title={t('Enhancement')}
          dataIndex={'enhancement'}
          width={800}
          cell={(c, i, r) => (<>
            <pre>{c}</pre>
            <pre>{r.message}</pre>
          </>)}
        />
        <Table.Column
          title={t('Enhance Dt')}
          dataIndex={'createDt'}
          width={200}
          cell={(c) => dayjs(c)
            .format('YYYY-MM-DD HH:mm:ss')}
        />
      </Table>
      <div
        className="opt"
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 5,
        }}
      >
        {enhancements?.length > 0 && (
          <Button
            style={{ marginTop: 10 }}
            onClick={async () => {
              await BApi.resource.removeResourceEnhancementRecords(resourceId);
              setEnhancements([]);
            }}
            warning
          >{t('Remove all records')}
          </Button>
        )}

        <Button
          style={{ marginTop: 10 }}
          loading={enhancing}
          onClick={async () => {
            setEnhancing(true);
            try {
              await BApi.resource.enhanceResource(resourceId);
              const eRsp = await BApi.resource.getResourceEnhancementRecords(resourceId);
              setEnhancements(eRsp.data || []);
            } finally {
              setEnhancing(false);
            }
          }}
          type={'primary'}
        >{t('Enhance now')}
        </Button>
      </div>
    </Dialog>
  );
}

ResourceEnhancementsDialog.show = (props: IProps) => createPortalOfComponent(ResourceEnhancementsDialog, props);

export default ResourceEnhancementsDialog;
