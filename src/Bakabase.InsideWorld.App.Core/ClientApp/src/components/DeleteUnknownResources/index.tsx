import { useTranslation } from 'react-i18next';
import { useEffect, useState } from 'react';
import { DeleteOutlined } from '@ant-design/icons';
import { Button, Tooltip } from '@/components/bakaui';
import BApi from '@/sdk/BApi';

type Props = {
  onDeleted?: () => any;
};

export default ({ onDeleted }: Props) => {
  const { t } = useTranslation();
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
      <Tooltip
        placement={'bottom'}
        content={t('Delete all resources bound to unknown category or media library')}
      >
        <Button
          variant={'light'}
          size={'sm'}
          color={'warning'}
          onClick={() => {
            BApi.resource.deleteUnknownResources().then(r => {
              init();
              onDeleted?.();
            });
          }}
        >
          <DeleteOutlined className={'text-base'} />
          {t('Delete unknown resources')}({count})
        </Button>
      </Tooltip>
    );
  }

  return null;
};
