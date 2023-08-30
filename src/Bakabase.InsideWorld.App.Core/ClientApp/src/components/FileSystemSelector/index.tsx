import { useEffect, useRef, useState } from 'react';
import { Button, Icon, Input } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import TreeEntry from '@/pages/FileProcessor/TreeEntry';
import type { IEntryRef } from '@/core/models/FileExplorer/Entry';
import { Entry } from '@/core/models/FileExplorer/Entry';
import RootEntry from '@/core/models/FileExplorer/RootEntry';
import './index.scss';
import BApi from '@/sdk/BApi';
import { useUpdate } from 'react-use';
import { splitPathIntoSegments, standardizePath } from '@/components/utils';
import BusinessConstants from '@/components/BusinessConstants';
import { IwFsType } from '@/sdk/constants';
import CustomIcon from '@/components/CustomIcon';
import DeleteDialog from '@/pages/FileProcessor/DeleteDialog';
import ClickableIcon from '@/components/ClickableIcon';

export interface IFileSystemSelectorProps {
  startPath?: string;
  targetType?: 'file' | 'folder';
  onSelected?: (entry: Entry) => any;
  onCancel?: () => any;
  filter?: (entry: Entry) => boolean;
}

export default (props: IFileSystemSelectorProps) => {
  const { t } = useTranslation();
  const forceUpdate = useUpdate();

  const {
    startPath,
    targetType,
    onSelected = () => {
    },
    onCancel = () => {
    },
    filter: propsFilter = e => true,
  } = props;

  const [entries, setEntries] = useState<Entry[]>();
  const selectedEntryRef = useRef<Entry>();
  const inputPathRef = useRef<string | undefined>(startPath);
  const inputBlurHandlerRef = useRef<any>();
  const highlightNameRef = useRef<string>();
  const startPathRef = useRef(startPath);

  const initializeRoot = async (root?: string) => {
    startPathRef.current = root;
    BApi.file.getChildrenIwFsInfo({ root }).then(r => {
      const rootEntry = new RootEntry();
      const filter = (entry: Entry) => {
        if (targetType) {
          switch (targetType) {
            case 'file':
              if (entry.type == IwFsType.Directory || entry.type == IwFsType.Invalid) {
                return false;
              }
              break;
            case 'folder':
              if (entry.type != IwFsType.Directory) {
                return false;
              }
              break;
          }
        }
        if (propsFilter) {
          return propsFilter(entry);
        }
        return true;
      };

      rootEntry.patchFilter({ custom: filter });
      // @ts-ignore
      const newEntries = r.data?.entries?.map(e => new Entry({
        ...e,
        parent: rootEntry,
      })) || [];

      setEntries(newEntries);

      const selectedEntry = newEntries.find(a => a.name == highlightNameRef.current);
      if (selectedEntry) {
        selectedEntryRef.current = selectedEntry;
        selectedEntry.select(true);
      }

      inputBlurHandlerRef.current = undefined;
      highlightNameRef.current = undefined;

      console.log(root);
    });
  };

  useEffect(() => {
    initializeRoot(startPath);
  }, []);

  return (
    <div
      className={'file-system-selector'}
    >
      <div className="line1">
        <ClickableIcon
          type={'arrowup'}
          colorType={'normal'}
          onClick={() => {
            const path = startPathRef.current || '';
            const segments = splitPathIntoSegments(path);
            const isUncPath = path.startsWith(BusinessConstants.uncPathPrefix);
            console.log(path, segments, isUncPath);
            if (segments.length > 1 && !isUncPath) {
              const newRoot = segments.slice(0, segments.length - 1).join(BusinessConstants.pathSeparator);
              initializeRoot(newRoot);
            }
          }}
        />
        <Input
          size={'small'}
          placeholder={t('You can type a path here')}
          defaultValue={startPath}
          onChange={v => {
            clearInterval(inputBlurHandlerRef.current);
            const path = standardizePath(v)!;
            inputPathRef.current = path;
            const segments = splitPathIntoSegments(path);
            const isUncPath = path.startsWith(BusinessConstants.uncPathPrefix);
            let newRootSegmentLength = segments.length == 1 ? 1 : (segments.length == 2 && isUncPath) ? 2 : segments.length - 1;
            const newRoot = segments.slice(0, newRootSegmentLength).join(BusinessConstants.pathSeparator);
            highlightNameRef.current = segments[newRootSegmentLength];
            // console.log(segments, newRoot, highlightNameRef.current);
            inputBlurHandlerRef.current = setTimeout(() => {
              initializeRoot(newRoot);
            }, 1000);
            forceUpdate();
          }}
        />
      </div>
      {(entries == undefined || inputBlurHandlerRef.current) ? (
        <div className={'no-entry'}>
          <Icon type={'loading'} />
        </div>
      ) : (
        <div className="entries">
          {entries.map(e => (
            <TreeEntry
              key={e.path}
              basicMode
              entry={e}
              onDeleteKeyDown={(evt, en) => {
                DeleteDialog.show({
                  paths: [en.path],
                  afterClose: () => {
                    if (en.parent) {
                      en.parent.expand(true);
                    }
                  },
                });
              }}
              onClick={e => {
                if (selectedEntryRef.current != e) {
                  selectedEntryRef.current?.select(false);
                  selectedEntryRef.current = e;
                  // forceUpdate();
                  e.select(true);
                }
              }}
              onDoubleClick={(evt, en) => {
                if (en.expandable) {
                  if (en.expanded) {
                    en.collapse();
                  } else {
                    en.expand();
                  }
                }
              }}
              ref={(r: IEntryRef) => {
                e.ref = r;
              }}
            />
          ))}
        </div>
      )}
      <div className="opt">
        <div className="left">
          <Button
            type={'normal'}
            size={'small'}
            disabled={selectedEntryRef.current == undefined}
            className={'new-folder'}
            onClick={() => {
              BApi.file.createDirectory({ parent: selectedEntryRef.current!.path }).then(r => {
                if (!r.code) {
                  selectedEntryRef.current?.expand(true);
                }
              });
            }}
          >
            <CustomIcon type={'folder-add'} size={'small'} />
            {t('New Folder')}
          </Button>
        </div>
        <div className="right">
          <Button
            type={'primary'}
            size={'small'}
            onClick={() => {
              if (onSelected) {
                onSelected(selectedEntryRef.current!);
              }
            }}
          >
            {t('OK')}
          </Button>
          <Button
            type={'normal'}
            size={'small'}
            onClick={() => {
              if (onCancel) {
                onCancel();
              }
            }}
          >
            {t('Cancel')}
          </Button>
        </div>
      </div>
    </div>
  );
};
