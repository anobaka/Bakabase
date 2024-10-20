import { LoadingOutlined, MinusSquareOutlined, PlusSquareOutlined, WarningOutlined } from '@ant-design/icons';
import React, { useCallback } from 'react';
import type { Entry } from '@/core/models/FileExplorer/Entry';
import { EntryStatus } from '@/core/models/FileExplorer/Entry';
import OperationButton from '@/pages/FileProcessor/TreeEntry/components/OperationButton';
import { Chip, Tooltip } from '@/components/bakaui';

type Props = {
  loading: boolean;
  entry: Entry;
  expandable?: boolean;
};

export default ({ loading, entry, expandable }: Props) => {
  const renderInner = useCallback(() => {
    if (loading) {
      return (
        <LoadingOutlined className={'text-base'} />
      );
    } else {
      switch (entry.status) {
        case EntryStatus.Default:
          if (expandable && entry.expandable) {
            if (entry.expanded) {
              return (
                <OperationButton onClick={(e) => { entry.ref?.collapse(); }} isIconOnly >
                  <MinusSquareOutlined className={'text-large'} />
                </OperationButton>
              );
            } else {
              return (
                <OperationButton onClick={(e) => { entry.ref?.expand(); }} isIconOnly >
                  <PlusSquareOutlined className={'text-large'} />
                </OperationButton>
              );
            }
          }
          break;
        case EntryStatus.Loading:
          break;
        case EntryStatus.Error:
          return (
            <Tooltip
              content={(
                <ul style={{ color: 'red' }}>
                  {Object.keys(entry.errors)
                    .map(e => {
                      return (
                        <li key={e}>{entry.errors[e]}</li>
                      );
                    })}
                </ul>
              )}
            >
              <Chip
                size={'sm'}
                variant={'light'}
                color={'danger'}
              >
                <WarningOutlined className={'text-base'} />
              </Chip>
            </Tooltip>
          );
      }
    }
    return null;
  }, [entry, loading, expandable]);

  return (
    <div className={'item'}>
      {renderInner()}
    </div>
  );
};
