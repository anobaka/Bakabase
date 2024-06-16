import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { FolderOpenOutlined } from '@ant-design/icons';
import { useUpdate } from 'react-use';
import BApi from '@/sdk/BApi';
import { buildLogger, splitPathIntoSegments, standardizePath } from '@/components/utils';
import { ResourceProperty } from '@/sdk/constants';
import PathSegmentsConfiguration, {
  PathSegmentConfigurationPropsMatcherOptions,
} from '@/components/PathSegmentsConfiguration';
import FileSystemSelectorDialog from '@/components/FileSystemSelector/Dialog';
import BusinessConstants from '@/components/BusinessConstants';
import { Button, Chip, Divider, Modal } from '@/components/bakaui';
import type { DestroyableProps } from '@/components/bakaui/types';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import { PscPropertyType } from '@/components/PathSegmentsConfiguration/models/PscPropertyType';
import { PscMatcherValue } from '@/components/PathSegmentsConfiguration/models/PscMatcherValue';
import {
  convertToPathConfigurationDtoFromPscValue,
  convertToPscValueFromPathConfigurationDto,
} from '@/components/PathSegmentsConfiguration/helpers';
import type { BakabaseAbstractionsModelsDomainPathConfiguration } from '@/sdk/Api';

const log = buildLogger('PathConfigurationDialog');

type Library = { id: number; pathConfigurations: BakabaseAbstractionsModelsDomainPathConfiguration[]; name: string };

interface Props extends DestroyableProps {
  libraryId: number;
  pcIdx: number;
  onSaved: (pc: BakabaseAbstractionsModelsDomainPathConfiguration) => any;
}

export default ({
                  libraryId,
                  pcIdx,
                  onSaved,
                  ...props
                }: Props) => {
  const { t } = useTranslation();
  const forceUpdate = useUpdate();
  const { createPortal } = useBakabaseContext();

  const [library, setLibrary] = useState<Library>();
  const [customPropertyMap, setCustomPropertyMap] = useState<Record<number, { name: string }>>({});

  useEffect(() => {
    log('Initialize with', props);

    loadLibrary();

    BApi.customProperty.getAllCustomPropertiesV2().then(r => {
      const ps = r.data || [];
      setCustomPropertyMap(ps.reduce<Record<number, { name: string }>>((s, t) => {
        s[t.id!] = { name: t.name! };
        return s;
      }, {}));
    });
  }, []);

  const loadLibrary = async () => {
    const r = await BApi.mediaLibrary.getMediaLibrary(libraryId);
    const l = r.data;
    if (l) {
      setLibrary({
        id: l.id!,
        name: l.name!,
        pathConfigurations: l.pathConfigurations ?? [],
      });
    }
  };

  if (!library) {
    return null;
  }

  const pc = library.pathConfigurations[pcIdx];

  const save = async (patches: Partial<BakabaseAbstractionsModelsDomainPathConfiguration>) => {
    const newPcs = library.pathConfigurations.slice();
    newPcs.splice(pcIdx, 1, { ...pc, ...patches });
    log(`Saving ${pcIdx} of ${library.pathConfigurations.length} in ${library.name}`, pc, newPcs);
    await BApi.mediaLibrary.patchMediaLibrary(library.id, {
      pathConfigurations: newPcs,
    });
    await loadLibrary();
  };

  const showPsc = (segments: string[], pc: BakabaseAbstractionsModelsDomainPathConfiguration, isDirectory: boolean) => {
    const simpleMatchers = {
      [PscPropertyType.RootPath]: true,
      [PscPropertyType.Resource]: false,
      [PscPropertyType.ParentResource]: false,
      [PscPropertyType.CustomProperty]: false,
    };
    const matchers = Object.keys(simpleMatchers)
      .reduce<PathSegmentConfigurationPropsMatcherOptions[]>((ts, t) => {
        ts.push(new PathSegmentConfigurationPropsMatcherOptions({
          propertyType: parseInt(t, 10),
          readonly: simpleMatchers[t],
        }));
        return ts;
      }, []);

    let tmpValue = convertToPscValueFromPathConfigurationDto(pc);

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
          defaultValue={tmpValue}
        />
      ),
      onOk: async () => {
        const dto = convertToPathConfigurationDtoFromPscValue(tmpValue);
        console.log(5555, dto);
        await save(dto);
        onSaved(dto);
        // await save({
        //   rpmValues: dto.rpmValues ?? undefined,
        //   path: dto.path ?? undefined,
        // });
      },
    });
  };

  const renderRpmValues = () => {
    const values = pc?.rpmValues || [];
    if (values.length == 0) {
      return null;
    }

    console.log(values, 55555);

    return (
      <div className="flex flex-col gap-1 my-1">
        {(values).map((s, i) => {
          let label = s.isCustomProperty ? customPropertyMap[s.propertyId!]?.name : t(ResourceProperty[s.propertyId!]);
          if (label.length == 0) {
            label = t('Unknown property');
          }
          return (
            <div className={'flex items-center gap-2'}>
              <Chip
                radius={'sm'}
                size={'sm'}
                color={s.isResourceProperty ? 'primary' : 'default'}
              >{label}</Chip>
              <div className="value">
                {PscMatcherValue.ToString(t, {
                  fixedText: s.fixedText ?? undefined,
                  layer: s.layer ?? undefined,
                  regex: s.regex ?? undefined,
                  valueType: s.valueType!,
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
                    defaultSelectedPath: pc?.path ?? undefined,
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
                    BApi.tool.openFileOrDirectory({ path: pc.path! });
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
            <div className={''}>
              {pc?.path ? (
                <Button
                  // size={'sm'}
                  variant={'light'}
                  color={'primary'}
                  onClick={() => {
                    createPortal(FileSystemSelectorDialog, {
                      // targetType: 'folder',
                      startPath: pc.path!,
                      // targetType: 'file',
                      onSelected: (e) => {
                        const std = standardizePath(e.path)!;
                        const stdPrev = standardizePath(pc.path!);
                        if (stdPrev && std.startsWith(stdPrev)) {
                          const segments = splitPathIntoSegments(e.path);
                          showPsc(segments, pc, e.isDirectory);
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
