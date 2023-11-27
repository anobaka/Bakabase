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
  defaultSelectedPath?: string;
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
    defaultSelectedPath,
  } = props;

  const [entries, setEntries] = useState<Entry[]>();
  const selectedEntryRef = useRef<Entry>();
  const inputPathRef = useRef<string | undefined>(startPath);
  const inputBlurHandlerRef = useRef<any>();
  const highlightEntryNameRef = useRef<string>();

  /**
   * todo: Optimize Entry to use TreeEntry immediately instead of handling first layer manually.
   * @param root
   */
  const initializeRoot = async (root?: string) => {
    inputPathRef.current = root;
    BApi.file.getChildrenIwFsInfo({ root }).then(r => {
      const rootEntry = new RootEntry(root);
      rootEntry.type = IwFsType.Directory;
      let types: IwFsType[] | undefined;
      if (targetType) {
        switch (targetType) {
          case 'file':
            types = [IwFsType.Audio, IwFsType.CompressedFilePart, IwFsType.CompressedFileEntry, IwFsType.Image, IwFsType.Unknown, IwFsType.Video];
            break;
          case 'folder':
            types = [IwFsType.Directory];
            break;
        }
      }

      rootEntry.patchFilter({
        custom: propsFilter,
        types,
      });
      // @ts-ignore
      const newEntries = r.data?.entries?.map(e => new Entry({
        ...e,
        parent: rootEntry,
        properties: [],
      })).filter(e => {
        if (!types || types.includes(e.type) || e.type == IwFsType.Directory) {
          if (!propsFilter || propsFilter(e)) {
            return true;
          }
        }
        return false;
      }) || [];

      setEntries(newEntries);

      const selectedEntry = newEntries.find(a => a.name == highlightEntryNameRef.current);
      if (selectedEntry) {
        selectedEntryRef.current = selectedEntry;
        selectedEntry.select(true);
      } else {
        // Select if start path is selected on initialization
        if (entries == undefined && defaultSelectedPath == root) {
          selectedEntryRef.current = rootEntry;
        }
      }

      inputBlurHandlerRef.current = undefined;
      highlightEntryNameRef.current = undefined;

      forceUpdate();

      console.log('12345', root, selectedEntryRef.current);
    });
  };

  useEffect(() => {
    if (defaultSelectedPath) {
      highlightEntryNameRef.current = splitPathIntoSegments(defaultSelectedPath).pop();
    }

    initializeRoot(startPath);
  }, []);

  const isAvailable = (e: Entry) => {
    if (targetType) {
      switch (targetType) {
        case 'file':
          if (![IwFsType.Audio, IwFsType.CompressedFilePart, IwFsType.CompressedFileEntry, IwFsType.Image, IwFsType.Unknown, IwFsType.Video].includes(e.type)) {
            return false;
          }
          break;
        case 'folder':
          if (e.type != IwFsType.Directory) {
            return false;
          }
          break;
      }
    }
    if (propsFilter && !propsFilter(e)) {
      return false;
    }
    return true;
  };

  const selectionIsAvailable = selectedEntryRef.current && isAvailable(selectedEntryRef.current);

  return (
    <div
      className={'file-system-selector'}
    >
      <div className="line1">
        <ClickableIcon
          type={'arrowup'}
          colorType={'normal'}
          onClick={() => {
            const path = inputPathRef.current || '';
            const segments = splitPathIntoSegments(path);
            const isUncPath = path.startsWith(BusinessConstants.uncPathPrefix);
            console.log(path, segments, isUncPath);
            if (segments.length > 0 && !isUncPath) {
              const newRoot = segments.slice(0, segments.length - 1).join(BusinessConstants.pathSeparator);
              initializeRoot(newRoot);
            }
          }}
        />
        <Input
          // size={'small'}
          placeholder={t('You can type a path here')}
          value={inputPathRef.current}
          onChange={v => {
            clearInterval(inputBlurHandlerRef.current);
            const path = standardizePath(v)!;
            inputPathRef.current = path;
            const segments = splitPathIntoSegments(path);
            const isUncPath = path.startsWith(BusinessConstants.uncPathPrefix);
            let newRootSegmentLength = segments.length == 1 ? 1 : (segments.length == 2 && isUncPath) ? 2 : segments.length - 1;
            const newRoot = segments.slice(0, newRootSegmentLength).join(BusinessConstants.pathSeparator);
            highlightEntryNameRef.current = segments[newRootSegmentLength];
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
              trySelect={e => {
                if (selectedEntryRef.current != e) {
                  selectedEntryRef.current?.select(false);
                  selectedEntryRef.current = e;
                } else {
                  selectedEntryRef.current = undefined;
                }
                forceUpdate();
                return true;
              }}
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
      {
        selectionIsAvailable && (
          <div className="selected">
            {t('Selected')}: {selectedEntryRef.current!.path}
          </div>
        )
      }
      <div className="opt">
        <div className="left">
          <Button
            type={'normal'}
            size={'small'}
            disabled={selectedEntryRef.current?.type != IwFsType.Directory}
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
            disabled={!selectionIsAvailable}
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
