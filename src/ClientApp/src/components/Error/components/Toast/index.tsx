import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import { CheckOutlined, CloseOutlined, CopyOutlined, WarningOutlined } from '@ant-design/icons';
import type { Toast } from 'react-hot-toast';
import toast from 'react-hot-toast';
import { Button, Chip } from '@/components/bakaui';

type Props = {
  toast: Toast;
  title: string;
  description?: string;
};

export default (props: Props) => {
  const {
    toast: tst,
    title,
    description,
  } = props;
  const { t } = useTranslation();
  const [copied, setCopied] = useState(false);
  return (
    <div className={'flex items-center gap-4'}>
      <Chip
        variant={'light'}
        color={'danger'}
        classNames={{ content: 'flex' }}
      ><WarningOutlined className={'text-xl'} /></Chip>
      <div className={'flex flex-col gap-1 break-all'}>
        <div>{title}</div>
        {description && <pre className={'opacity-80 text-xs'}>{description}</pre>}
      </div>
      <div className={'flex items-center'}>
        <Button
          size={'sm'}
          isIconOnly
          onClick={async () => {
            try {
              let text = title;
              if (description) {
                text += `\n${description}`;
              }
              await navigator.clipboard.writeText(text);
              setCopied(true);
              console.log('Text copied to clipboard');
            } catch (err) {
              console.error('Failed to copy text: ', err);
              setCopied(false);
            }
          }}
          variant={'light'}
        >
          {copied ? (
            <CheckOutlined className={'text-base'} />
          ) : (
            <CopyOutlined className={'text-base'} />
          )}
        </Button>
        <Button
          size={'sm'}
          onClick={() => toast.dismiss(tst.id)}
          isIconOnly
          variant={'light'}
        >
          <CloseOutlined className={'text-base'} />
        </Button>
      </div>
    </div>
  );
};
