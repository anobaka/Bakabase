import { EditOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { useRef, useState } from 'react';
import { useUpdate } from 'react-use';
import type { ValueRendererProps } from '../models';
import type { LinkValue } from '../../models';
import NotSet from './components/NotSet';
import ExternalLink from '@/components/ExternalLink';
import { Button, Input, Popover } from '@/components/bakaui';

type LinkValueRendererProps = ValueRendererProps<LinkValue> & {
};

export default ({
                  value,
                  editor,
                  variant,
                  ...props
                }: LinkValueRendererProps) => {
  const { t } = useTranslation();
  const [editingValue, setEditingValue] = useState<LinkValue>();

  const renderInner = () => {
    if (value?.url) {
      return (
        <ExternalLink to={value.url}>
          {value.text ?? value.url}
        </ExternalLink>
      );
    } else {
      if (value?.text != undefined && value.text.length > 0) {
        return (
          <span>
            {value.text}
          </span>
        );
      }
    }
    return;
  };

  const inner = renderInner();
  if (editor) {
    return (
      <span className={'flex items-center gap-2'}>
        {inner}
        <Popover
          isOpen={!!editingValue}
          isKeyboardDismissDisabled
          shouldCloseOnBlur={false}
          onOpenChange={isOpen => {
            if (isOpen) {
              setEditingValue({ ...value });
            }
          }}
          trigger={(
            <Button size={'sm'} isIconOnly>
              <EditOutlined className={'text-base'} />
            </Button>
          )}
        >
          <div className={'flex flex-col gap-1'}>
            <Input
              size={'sm'}
              label={t('Text')}
              value={editingValue?.text}
              onValueChange={text => {
                setEditingValue({
                  ...editingValue,
                  text,
                });
              }}
            />
            <Input
              size={'sm'}
              label={t('Link')}
              value={editingValue?.url}
              onValueChange={url => {
                setEditingValue({
                  ...editingValue,
                  url,
                });
              }}
            />
            <div className={'flex items-center gap-2 justify-center'}>
              <Button
                size={'sm'}
                color={'primary'}
                onClick={() => {
                  editor?.onValueChange?.(editingValue, editingValue);
                  setEditingValue(undefined);
                }}
              >
                {t('Submit')}
              </Button>
              <Button
                size={'sm'}
                onClick={() => {
                  setEditingValue(undefined);
                }}
              >
                {t('Cancel')}
              </Button>
            </div>
          </div>
        </Popover>
      </span>
    );
  } else {
    return inner;
  }
};
