import React, { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { FolderOpenOutlined } from '@ant-design/icons';
import { useUpdate } from 'react-use';
import BApi from '@/sdk/BApi';
import { buildLogger, splitPathIntoSegments, standardizePath } from '@/components/utils';
import type { IPscValue } from '@/components/PathSegmentsConfiguration/models/PscValue';
import { ResourceProperty } from '@/sdk/constants';
import PathSegmentsConfiguration, { PathSegmentConfigurationPropsMatcherOptions } from '@/components/PathSegmentsConfiguration';
import { MatcherValue } from '@/components/PathSegmentsConfiguration/models/MatcherValue';
import SimpleLabel from '@/components/SimpleLabel';
import FileSystemSelectorDialog from '@/components/FileSystemSelector/Dialog';
import BusinessConstants from '@/components/BusinessConstants';
import { Button, Chip, Divider, Modal } from '@/components/bakaui';
import type { DestroyableProps } from '@/components/bakaui/types';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';

const log = buildLogger('PathConfigurationDialog');

interface Props extends DestroyableProps{
  library: { id: number; pathConfigurations: Pc[]; name: string };
  pcIdx: number;
  onSaved: () => Promise<any>;
}

type Pc = {
  path: string;
  rpmValues: any[];
};

export default ({ library, pcIdx, ...props }: Props) => {
  const { t } = useTranslation();
  const forceUpdate = useUpdate();
  const { createPortal } = useBakabaseContext();

  const pc = library.pathConfigurations[pcIdx];

  useEffect(() => {
    log('Initialize with', props);
  }, []);

  const save = async (patches: any) => {
    const newPcs = library.pathConfigurations.slice();
    newPcs.splice(pcIdx, 1, { ...pc, ...patches });
    log(`Saving ${pcIdx} of ${library.pathConfigurations.length} in ${library.name}`, pc, newPcs);
    await BApi.mediaLibrary.patchMediaLibrary(library.id, {
      pathConfigurations: newPcs,
    });
    forceUpdate();
  };

  const showPsc = (segments: string[], pscValue: IPscValue, isDirectory: boolean) => {
    const simpleMatchers = {
      [ResourceProperty.RootPath]: true,
      [ResourceProperty.Resource]: false,
      [ResourceProperty.ParentResource]: false,
    };
    const matchers = Object.keys(simpleMatchers)
      .reduce<PathSegmentConfigurationPropsMatcherOptions[]>((ts, t) => {
        ts.push(new PathSegmentConfigurationPropsMatcherOptions({
          property: parseInt(t, 10),
          readonly: simpleMatchers[t],
        }));
        return ts;
      }, []);

    let tmpValue = pscValue;

    createPortal(Modal, {
      defaultVisible: true,
      size: 'xl',
      children: (
        <PathSegmentsConfiguration
          isDirectory={isDirectory || false}
          segments={segments!}
          onChange={value => {
            tmpValue = value;
          }}
          matchers={matchers}
          defaultValue={pscValue}
        />
      ),
      onOk: async () => {
        await save({
          rpmValues: tmpValue.rpmValues,
        });
      },
    });
  };

  const renderRpmValues = () => {
    const values = pc?.rpmValues || [];
    if (values.length == 0) {
      return null;
    }

    console.log(values);

    return (
      <div className="items">
        {(values).map((s, i) => {
          return (
            <div className={'segment'}>
              <div className="label">
                <SimpleLabel
                  status={s.property == ResourceProperty.Resource ? 'primary' : 'default'}
                >{t(ResourceProperty[s.property])}{s.property == ResourceProperty.CustomProperty ? `:${s.key}` : ''}</SimpleLabel>
              </div>
              <div className="value">
                {MatcherValue.ToString({
                  ...s,
                  type: s.valueType,
                })}
              </div>
            </div>
          );
        })}
      </div>
    );
  };

  const rootPathIsSet = !!pc?.path;

  return (
    <>
      <Modal
        size={'xl'}
        defaultVisible
        onClose={close}
        onDestroyed={props.onDestroyed}
        footer={{
          actions: ['cancel'],
        }}
        title={t('Path configuration')}
      >
        <div className="flex flex-col gap-2 shadow">
          <section>
            <div className="text-base font-bold mb-1">{t('Root path')}</div>
            <div>
              <Button
                variant={'light'}
                  // size={'sm'}
                color={'primary'}
                onClick={() => {
                    let startPath: string | undefined;
                    if (pc?.path) {
                      const segments = splitPathIntoSegments(pc.path);
                      startPath = segments.slice(0, segments.length - 1).join(BusinessConstants.pathSeparator);
                    }
                    createPortal(FileSystemSelectorDialog, {
                      startPath: startPath,
                      targetType: 'folder',
                      onSelected: e => {
                        save({ path: e.path });
                      },
                      defaultSelectedPath: pc?.path,
                    });
                  }}
              >{pc?.path ?? t('Setup')}
              </Button>
              {rootPathIsSet && (
              <FolderOpenOutlined
                className={'text-base cursor-pointer ml-1'}
                onClick={(e) => {
                      e.preventDefault();
                      e.stopPropagation();
                      BApi.tool.openFileOrDirectory({ path: pc.path });
                    }}
              />
                )}
            </div>
          </section>
          <Divider />
          <section className="">
            <div className="text-base font-bold">
              {t('Setup how to find resources and properties')}
            </div>
            {pc?.path && (
            <div className={'opacity-60 italic'}>
              {t('To setup this item, you should pick up a file first within your root path.')}
              <br />
              {t('If you want to populate properties as many as possible, you should pick up a file with more layers in path.')}
            </div>
              )}
            {renderRpmValues()}
            <div>
              {pc?.path ? (
                <Button
                      // size={'sm'}
                  variant={'light'}
                  color={'primary'}
                  onClick={() => {
                      createPortal(FileSystemSelectorDialog, {
                        // targetType: 'folder',
                        startPath: pc.path,
                        // targetType: 'file',
                        onSelected: (e) => {
                          const std = standardizePath(e.path)!;
                          const stdPrev = standardizePath(pc.path);
                          if (stdPrev && std.startsWith(stdPrev)) {
                            const segments = splitPathIntoSegments(e.path);
                            const pscValue: IPscValue = {};
                            if (pc) {
                              pscValue.rpmValues = JSON.parse(JSON.stringify(pc.rpmValues ?? []));
                              pscValue.path = pc.path;
                            }

                            showPsc(segments, pscValue, e.isDirectory);
                          } else {
                            createPortal(Modal, {
                              defaultVisible: true,
                              title: t('Error'),
                              children: t('You can select a file out of root path. If you want to change the root path of your library, you should click on your root path.'),
                            });
                          }
                        },
                      });
                    }}
                >
                  {t('Setup')}
                </Button>
                ) : (
                  <Chip
                    size={'sm'}
                    variant={'light'}
                    color={'warning'}
                  >
                    {t('You need to set up root path before configuring this item')}
                  </Chip>
                )}
            </div>
          </section>
        </div>
      </Modal>
    </>
  );
};
