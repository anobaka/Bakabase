import { Balloon, Button, Checkbox, Dialog, Input, Message, Radio, Slider, Step as StepComponent, Switch } from '@alifd/next';
import React, { useEffect, useRef, useState } from 'react';
import './index.scss';
import { history, Link } from 'ice';
import { useUpdateEffect } from 'react-use';
import IceLabel from '@icedesign/label';
import { useTranslation } from 'react-i18next';
import { ComponentType, coverSelectOrders, ResourceMatcherValueType, resourceProperties, ResourceProperty } from '@/sdk/constants';
import BasicCategoryComponentSelector from '@/components/BasicCategoryComponentSelector';
import { GetAllResourceCategories, OpenFileSelector, SaveDataFromSetupWizard } from '@/sdk/apis';
import { buildLayerBasedPathRegexString, buildLogger, splitPathIntoSegments, standardizePath } from '@/components/utils';
import TagSelector from '@/components/TagSelector';
import type { IPathSegmentConfigurationRef } from '@/components/PathSegmentsConfiguration';
import PathSegmentsConfiguration, { PathSegmentConfigurationPropsMatcherOptions } from '@/components/PathSegmentsConfiguration';
import type { LayerValue } from '@/components/PathSegmentsConfiguration/models/matchers';
import EnhancerSelector from '@/components/EnhancerSelector';
import CustomIcon from '@/components/CustomIcon';
import type { IPscValue } from '@/components/PathSegmentsConfiguration/models/PscValue';
import BusinessConstants from '@/components/BusinessConstants';
import FileSystemSelectorDialog from '@/components/FileSystemSelector/Dialog';

enum Step {
  CategoryName = 1,
  PlayableFile,
  Player,
  MediaLibrary,
  MediaLibraryName,
  MediaLibraryPathConfigurations,
  MediaLibraryPathFilter,
  MediaLibraryPathFixedTags,
  MediaLibraryPathSegments,
  Enhancers,
  Others,
}

const StepIndicatorItem = {
  'Setup a category': {
    index: 0,
    steps: [Step.CategoryName, Step.Player, Step.PlayableFile],
  },
  'Setup media libraries': {
    index: 1,
    steps: [Step.MediaLibrary, Step.MediaLibraryName, Step.MediaLibraryPathConfigurations, Step.MediaLibraryPathFilter, Step.MediaLibraryPathFixedTags, Step.MediaLibraryPathSegments],
  },
  'Setup other options': {
    index: 2,
    steps: [Step.Enhancers, Step.Others],
  },
};

const StepStepIndicatorMap = Object.keys(StepIndicatorItem)
  .reduce((s, t) => {
    const d = StepIndicatorItem[t];
    for (let step of d.steps) {
      s[step] = {
        label: t,
        index: d.index,
      };
    }
    return s;
  }, {});

const stepIndicatorItems = Object.keys(StepIndicatorItem)
  .map(a => ({
    label: a,
    index: StepIndicatorItem[a].index,
  }));

const StepInfo = {
  [Step.CategoryName]: {
    label: 'Set a name for category',
    tips: ['A category is used to handle one type resources (mostly cataloged by media type).',
      'A good practice naming category is \'Anime\', \'Comic\' and \'Game\' instead of \'ACG\'(mixed types).',
    ],
  },
  [Step.PlayableFile]: {
    label: 'Select a component to discover playable files',
  },
  [Step.Player]: {
    label: 'What player is able to play those files?',
  },
  [Step.MediaLibrary]: {
    label: 'Set media libraries',
  },
  [Step.MediaLibraryName]: {
    label: 'Set a name for media library',
    tips: ['You can set a name for media library freely using your own rules. Such as \'pending\', \'watching\', \'done\'.'],
  },
  [Step.MediaLibraryPathConfigurations]: {
    label: 'Where are your resources saved?',
  },
  [Step.MediaLibraryPathFilter]: {
    label: 'Locate your rest resources',
  },
  [Step.MediaLibraryPathFixedTags]: {
    label: 'Shall we add some tags for resources?',
  },
  [Step.MediaLibraryPathSegments]: {
    label: 'Do you want to extract some properties from path segments?',
  },
  [Step.Enhancers]: {
    label: 'Some enhancers are suitable for those resources',
    prevStep: Step.MediaLibrary,
    tips: [
      'Expand properties of resources, such as publisher(property), publication date(property), tags(property), cover(file), etc',
    ],
  },
  [Step.Others]: {
    label: 'Here are rest options',
  },
};

// function buildPscValue(samplePath:pathConfiguration): Map<ResourceProperty, Array<string | LayerValue>>|undefined {
//   if (!pathConfiguration) {
//     return undefined;
//   }
//
//   const pscValue: Map<ResourceProperty, Array<string | LayerValue>> = new Map();
//
//   if (pathConfiguration.path) {
//     pscValue[ResourceProperty.RootPath] = [pathConfiguration.path];
//   }
//
//   if (pathConfiguration.regex) {
//     pscValue[ResourceProperty.Resource] = [pathConfiguration.regex];
//   }
//
//   if (pathConfiguration.segments) {
//     for (const s of pathConfiguration.segments) {
//       if (s.type != ResourceProperty.RootPath && s.type != ResourceProperty.Resource) {
//         if (!(s.type in pscValue)) {
//           pscValue[s.type] = [];
//         }
//         pscValue[s.type].push({
//           ...s,
//           // adjust layer to absolute
//         });
//       }
//     }
//   }
//
//   return pscValue;
// }

function buildPathConfiguration(samplePath: string, pscValue: Map<ResourceProperty, Array<string | LayerValue>>): any | undefined {
  const rootPathValues = pscValue[ResourceProperty.RootPath] || [];
  const resourceValues = pscValue[ResourceProperty.Resource] || [];

  const resourceRegexStr = resourceValues[0];

  const pc: any = {
    path: rootPathValues[0],
    regex: resourceRegexStr,
  };
  const otherSegments = Object.keys(pscValue)
    .map((a) => {
      const t: ResourceProperty = parseInt(a);
      if (t != ResourceProperty.Resource && t != ResourceProperty.RootPath) {
        return (pscValue[a] || []).map((b) => {
          return {
            ...b,
            type: t,
          };
        });
      } else {
        return undefined;
      }
    })
    .filter((a) => a)
    .reduce((s, t) => {
      for (const x of t) {
        s.push(x);
      }
      return s;
    }, []);

  pc.segments = otherSegments;

  return pc;
}

const TestCategory = {
  name: '新分类',
  componentsData: [
    {
      name: 'ImagePlayableFileSelector',
      valid: true,
      reasonForInvalid: null,
      description: 'Select top 1 files in extensions [.gif,.psd,.png,.jpg,.webp,.bmp,.tiff,.jpeg,.ico,.svg] as start files.',
      isPreset: true,
      type: 2,
      typeKey: 'Bakabase.InsideWorld.Business.Components.Resource.Components.StartFileSelector.ImagePlayableFileSelector, Bakabase.InsideWorld.Business, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null',
      customOptionsId: 0,
    },
    {
      name: 'SelfPlayer',
      valid: true,
      reasonForInvalid: null,
      description: 'Use playable file as executable',
      isPreset: true,
      type: 3,
      typeKey: 'Bakabase.InsideWorld.Business.Components.Resource.Components.Player.SelfPlayer, Bakabase.InsideWorld.Business, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null',
      customOptionsId: 0,
    },
  ],
};

const TestMediaLibraries = [
  {
    name: '新mtk1',
    pathConfigurations: [
      {
        path: 'D:/test',
        regex: '^[^\\/]+/[^\\/]+/[^\\/]+/[^\\/]+/[^\\/]+$',
        segments: [
          [
            {
              layer: 5,
              isReverse: false,
              type: 2,
            },
          ],
          [
            {
              layer: 5,
              isReverse: false,
              type: 10,
            },
          ],
          [
            {
              layer: 5,
              isReverse: false,
              type: 11,
            },
          ],
          [
            {
              layer: 5,
              isReverse: false,
              type: 13,
            },
          ],
        ],
        fixedTags: [
          {
            id: 87,
            name: '蜘蛛侠 - Copy (6) - Copy',
            preferredAlias: null,
            color: null,
            order: 0,
            groupId: 0,
            groupName: 'Default',
            groupNamePreferredAlias: null,
          },
          {
            id: 88,
            name: '蜘蛛侠 - Copy (7)',
            preferredAlias: null,
            color: null,
            order: 0,
            groupId: 0,
            groupName: 'Default',
            groupNamePreferredAlias: null,
          },
          {
            id: 90,
            name: '蜘蛛侠 - Copy (8) - Copy',
            preferredAlias: null,
            color: null,
            order: 0,
            groupId: 0,
            groupName: 'Default',
            groupNamePreferredAlias: null,
          },
        ],
        fixedTagIds: [
          87,
          88,
          90,
        ],
      },
    ],
  },
];

const log = buildLogger('Category Setup Wizard');

export default ({ categoryId }: { categoryId?: number }) => {
  const { t } = useTranslation();

  const [step, setStep] = useState(Step.CategoryName);
  const [category, setCategory] = useState<any>();
  const [mediaLibraries, setMediaLibraries] = useState();
  const [currentMediaLibraryIndex, setCurrentMediaLibraryIndex] = useState<number | undefined>();
  const [mediaLibrary, setMediaLibrary] = useState();

  const [pscSamplePath, setPscSamplePath] = useState<string>();
  const pscSamplePathIsDirectoryRef = useRef(false);
  const [pathConfiguration, setPathConfiguration] = useState<any>();

  const [pscValue, setPscValue] = useState<IPscValue>();
  const psc1Ref = useRef<IPathSegmentConfigurationRef>();

  const [syncAfterSaving, setSyncAfterSaving] = useState(true);

  const goToNextStep = () => {
    setStep(Math.min(Step.Others, step + 1));
  };

  useEffect(() => {
    if (categoryId > 0) {
      GetAllResourceCategories()
        .invoke((a) => {
          setCategory(a.data.find((b) => b.id == categoryId));
        });
    }

    // setPscSamplePath('D:\\FE Test\\多层文件夹1[密码是123456]\\多层文件夹1\\多层文件夹2\\多层文件夹3\\123.txt');

    setStep(Step.CategoryName);
  }, []);

  useUpdateEffect(() => {
  }, [step]);

  const safeCategory = category || {};

  const renderSteps = () => {
    const steps: any[] = [];

    for (const s of Object.values(Step)
      .filter((a) => !isNaN(Number(a)))) {
      const data: any = {
        step: s,
        operations: [],
      };

      if (s == step) {
        switch (s) {
          case Step.CategoryName: {
            const hasError = (typeof safeCategory?.name === 'string' || safeCategory?.name instanceof String) && safeCategory?.name.length == 0;
            data.component = (
              <>
                <Input
                  size={'large'}
                  width={600}
                  state={hasError ? 'error' : undefined}
                  value={safeCategory?.name}
                  onChange={(name) => setCategory({
                    ...(safeCategory),
                    name,
                  })}
                />
              </>
            );
            data.operations.push(
              <Button
                key={s}
                type={'primary'}
                size={'large'}
                onClick={() => {
                  if (!(safeCategory?.name?.length > 0)) {
                    setCategory({
                      ...(safeCategory),
                      name: '',
                    });
                  } else {
                    goToNextStep();
                  }
                }}
              >
                {t('Next step')}
              </Button>,
            );
            break;
          }
          case Step.PlayableFile:
          case Step.Player: {
            const componentType = Step.PlayableFile == step ? ComponentType.PlayableFileSelector : ComponentType.Player;
            const cds = safeCategory?.componentsData || [];
            const componentKey = cds.find((a) => a.componentType == componentType)?.componentKey;
            const prevKey = componentKey;
            data.component = (
              <BasicCategoryComponentSelector
                onChange={(componentKeys) => {
                  const arr = cds.filter(a => a.componentKey != prevKey);
                  arr.push({
                    componentKey: componentKeys[0],
                    componentType: componentType,
                  });
                  setCategory({
                    ...category,
                    componentsData: arr,
                  });
                  goToNextStep();
                }}
                componentType={componentType}
                value={[componentKey]}
              />
            );
            break;
          }
          case Step.MediaLibrary: {
            const hasLibraries = mediaLibraries?.length > 0;
            data.component = (
              <div className={'media-libraries-container'}>
                {hasLibraries && (
                  <>
                    <div className={'media-libraries'}>
                      {mediaLibraries.map((m, i) => {
                        return (
                          <div className={'media-library'} key={i}>
                            <div
                              className="left"
                              onClick={() => {
                                setMediaLibrary(m);
                                setCurrentMediaLibraryIndex(i);
                                setStep(Step.MediaLibraryName);
                              }}
                            >
                              <CustomIcon type={'check-circle'} />
                              {m.name}
                            </div>
                            <div className="right">
                              <CustomIcon
                                type={'delete'}
                                onClick={() => {
                                  Dialog.confirm({
                                    title: t('Sure to delete?'),
                                    closeable: true,
                                    onOk: () => {
                                      setMediaLibraries(mediaLibraries.filter((a) => a !== m));
                                    },
                                  });
                                }}
                              />
                            </div>
                          </div>
                        );
                      })}
                    </div>
                  </>
                )}
                <Button
                  type={hasLibraries ? 'normal' : 'primary'}
                  onClick={() => {
                    setMediaLibrary({});
                    setCurrentMediaLibraryIndex(undefined);
                    setStep(Step.MediaLibraryName);
                  }}
                >
                  <CustomIcon type={'plus-circle'} />
                  {t('Add a media library')}
                </Button>
              </div>
            );
            data.operations.push(
              <Button
                key={s}
                disabled={!hasLibraries}
                type={hasLibraries ? 'primary' : 'normal'}
                size={'large'}
                onClick={() => {
                  setStep(Step.Enhancers);
                }}
              >
                {t(hasLibraries ? 'Complete the configurations and go to the next step' : 'Add at least one media library to continue')}
              </Button>,
            );
            break;
          }
          case Step.MediaLibraryName: {
            const hasError = mediaLibrary?.name == undefined || (typeof mediaLibrary?.name === 'string' || mediaLibrary?.name instanceof String) && mediaLibrary?.name.length == 0;
            data.component = (
              <Input
                size={'large'}
                state={hasError ? 'error' : undefined}
                value={mediaLibrary?.name}
                onChange={(name) => setMediaLibrary({
                  ...(mediaLibrary || {}),
                  name,
                })}
              />
            );
            data.operations.push(
              <Button
                key={s}
                type={'primary'}
                size={'large'}
                onClick={() => {
                  if (!hasError) {
                    setStep(Step.MediaLibraryPathConfigurations);
                  }
                }}
              >
                {t('Next step')}
              </Button>,
            );
            break;
          }
          case Step.MediaLibraryPathConfigurations: {
            const hasPathConfigurations = mediaLibrary?.pathConfigurations?.length > 0;
            data.component = (
              <div className={'path-configurations-container'}>
                {hasPathConfigurations && (
                  <>
                    <div className={'path-configurations'}>
                      {mediaLibrary?.pathConfigurations.map((m) => {
                        return (
                          <div className={'path-configuration'}>
                            <div className="left">
                              <CustomIcon type={'check-circle'} />
                              {m.path}
                            </div>
                            <div className="right">
                              <CustomIcon
                                type={'delete'}
                                onClick={() => {
                                  Dialog.confirm({
                                    title: t('Sure to delete?'),
                                    closeable: true,
                                    onOk: () => {
                                      setMediaLibrary({
                                        ...mediaLibrary,
                                        pathConfigurations: mediaLibrary.pathConfigurations.filter((a) => a != m),
                                      });
                                    },
                                  });
                                }}
                              />
                            </div>
                          </div>
                        );
                      })}
                    </div>
                  </>
                )}
                <Button
                  type={hasPathConfigurations ? 'normal' : 'primary'}
                  onClick={() => {
                    FileSystemSelectorDialog.show({
                      onSelected: e => {
                        pscSamplePathIsDirectoryRef.current = e.isDirectory;
                        setPscSamplePath(e.path);
                        goToNextStep();
                      },
                    });
                    // OpenFileSelector()
                    //   .invoke((a) => {
                    //     if (a.data) {
                    //
                    //     }
                    //   });
                  }}
                >
                  <CustomIcon type={'plus-circle'} />
                  {t(hasPathConfigurations ? 'Select a file you want to play in another root path' : 'Select a file you want to play')}
                </Button>
              </div>
            );
            data.operations.push(
              <Button
                type={hasPathConfigurations ? 'primary' : 'normal'}
                size={'large'}
                key={s}
                text={!hasPathConfigurations}
                onClick={() => {
                  const mls = mediaLibraries || [];
                  if (currentMediaLibraryIndex > -1) {
                    mls[currentMediaLibraryIndex] = mediaLibrary;
                  } else {
                    mls.push(mediaLibrary);
                  }
                  setMediaLibrary(undefined);
                  setMediaLibraries(mls);
                  setStep(Step.MediaLibrary);
                }}
              >
                {t(hasPathConfigurations ? 'Complete the configurations and return to media library list' : 'Set it later and return to media library list')}
              </Button>,
            );
            break;
          }
          case Step.MediaLibraryPathFilter: {
            if (pscSamplePath) {
              const segments = splitPathIntoSegments(pscSamplePath);
              const defaultResourceLayer = 1;
              const defaultValue: IPscValue = {
                path: segments.slice(0, segments.length - 1).join(BusinessConstants.pathSeparator),
                rpmValues: [
                  {
                    property: ResourceProperty.Resource,
                    valueType: ResourceMatcherValueType.Layer,
                    layer: defaultResourceLayer,
                  },
                ],
              };
              data.component = (
                <>
                  <Message type={'notice'}>
                    {t('Your resource is located by system automatically, you can click any path segment to change it if it\'s not correct.')}
                    <br />
                    {t('And you can set at most one parent resource too.')}
                  </Message>
                  <Message type={'warning'}>
                    {t('A root path is required for a media library, you must choose any of path segments to set it.')}
                    <br />
                    {t('A resource can be a movie, manga, etc. And it is the smallest unit bound to media library and category.')}
                    <br />
                    {t('All sub file entries will be treated as the contents of resource, but not resources self.')}
                  </Message>
                  <PathSegmentsConfiguration
                    segments={segments}
                    isDirectory={pscSamplePathIsDirectoryRef.current}
                    defaultValue={defaultValue}
                    matchers={[
                      new PathSegmentConfigurationPropsMatcherOptions({
                        property: ResourceProperty.RootPath,
                        readonly: false,
                      }),
                      new PathSegmentConfigurationPropsMatcherOptions({
                        property: ResourceProperty.Resource,
                        readonly: false,
                      }),
                      new PathSegmentConfigurationPropsMatcherOptions({
                        property: ResourceProperty.ParentResource,
                        readonly: false,
                      }),
                    ]}
                    onChange={(v) => {
                      setPscValue(v);
                    }}
                    ref={psc1Ref}
                  />
                </>
              );

              const pscData = psc1Ref.current?.coreData;
              const isValid = pscData && !pscData.hasError;

              data.operations.push(
                <Button
                  key={s}
                  disabled={!isValid}
                  type={'primary'}
                  size={'large'}
                  onClick={() => {
                    if (isValid) {
                      setPathConfiguration({
                        ...pathConfiguration,
                        ...pscValue,
                      });
                      goToNextStep();
                    }
                  }}
                >
                  {t(isValid ? 'Next step' : 'Please locate your resource first')}
                </Button>,
              );
            }
            break;
          }
          case Step.MediaLibraryPathFixedTags: {
            data.component = (
              <>
                {pathConfiguration?.fixedTags && (
                  <div className={'tags'}>
                    {pathConfiguration.fixedTags.map((t) => {
                      return (
                        <IceLabel inverse={false} status={'default'}>{t.displayName}</IceLabel>
                      );
                    })}
                  </div>
                )}
                <div className="add">
                  <Button
                    type={'primary'}
                    text
                    size={'large'}
                    onClick={() => {
                      Dialog.show({
                        title: t('Add tags for resources'),
                        content: (
                          <TagSelector
                            onChange={(value, tags) => {
                              setPathConfiguration({
                                ...(pathConfiguration || {}),
                                fixedTags: value.tagIds.map(id => tags[id]),
                                fixedTagIds: value.tagIds,
                              });
                            }}
                            defaultValue={pathConfiguration?.fixedTagIds}
                          />
                        ),
                      });
                    }}
                  >
                    {t('Pick some tags')}
                  </Button>
                </div>
              </>
            );

            const skip = !(pathConfiguration?.fixedTags?.length > 0);

            data.operations.push(
              <Button
                key={s}
                type={skip ? 'normal' : 'primary'}
                size={'large'}
                text={skip}
                onClick={() => {
                  setStep(Step.MediaLibraryPathSegments);
                }}
              >
                {t(skip ? 'Set it later and go to next step' : 'Next step')}
              </Button>,
            );

            break;
          }
          case Step.MediaLibraryPathSegments: {
            if (pscValue) {
              if (pscSamplePath) {
                const segments = splitPathIntoSegments(pscSamplePath);
                const simpleMatchers = {
                  [ResourceProperty.Resource]: true,
                  [ResourceProperty.RootPath]: true,
                  [ResourceProperty.ParentResource]: true,
                  [ResourceProperty.ReleaseDt]: false,
                  [ResourceProperty.Publisher]: false,
                  [ResourceProperty.Name]: false,
                  [ResourceProperty.Language]: false,
                  [ResourceProperty.Volume]: false,
                  [ResourceProperty.Original]: false,
                  [ResourceProperty.Series]: false,
                  [ResourceProperty.Tag]: false,
                  // [MatcherType.Introduction]: false,
                  [ResourceProperty.Rate]: false,
                  [ResourceProperty.CustomProperty]: false,
                };
                const matchers = Object.keys(simpleMatchers)
                  .reduce<PathSegmentConfigurationPropsMatcherOptions[]>((ts, t) => {
                    ts.push(new PathSegmentConfigurationPropsMatcherOptions({
                      property: parseInt(t, 10),
                      readonly: simpleMatchers[t],
                    }));
                    return ts;
                  }, []);
                data.component = (
                  <>
                    <PathSegmentsConfiguration
                      segments={segments}
                      isDirectory={pscSamplePathIsDirectoryRef.current}
                      defaultValue={pscValue}
                      matchers={matchers}
                      onChange={(value) => {
                        setPscValue(value);
                      }}
                    />
                  </>
                );

                const newValuesSet = resourceProperties.some((a) => a.value != ResourceProperty.RootPath &&
                  a.value != ResourceProperty.Resource &&
                  a.value != ResourceProperty.ParentResource &&
                  ((pscValue.rpmValues || []).filter(r => r.property == a.value))?.length > 0);

                const submit = () => {
                  const pc = {
                    ...pathConfiguration,
                    ...pscValue,
                  };
                  if (!mediaLibrary.pathConfigurations) {
                    mediaLibrary.pathConfigurations = [];
                  }
                  mediaLibrary.pathConfigurations.push({
                    ...pathConfiguration,
                    ...pc,
                  });
                  setPathConfiguration(undefined);
                  setStep(Step.MediaLibraryPathConfigurations);
                };

                if (newValuesSet) {
                  data.operations.push(
                    <Button
                      key={s}
                      type={'normal'}
                      size={'large'}
                      text
                      onClick={submit}
                    >
                      {t('Set it later and return to media library path configurations')}
                    </Button>,
                  );
                } else {
                  // todo: error judgement
                  const hasError = false;
                  data.operations.push(
                    <Button
                      key={s}
                      type={'primary'}
                      size={'large'}
                      disabled={hasError}
                      onClick={() => {
                        if (!hasError) {
                          submit();
                        }
                      }}
                    >
                      {t(hasError ? 'Invalid data is found' : 'Complete the configurations and return to media library path configurations')}
                    </Button>,
                  );
                }
              }
            }
            break;
          }
          case Step.Enhancers: {
            const eKeys = category.componentsData?.filter(d => d.componentType == ComponentType.Enhancer).map(d => d.componentKey);
            const eo = {
              ...(category?.enhancementOptions || {}),
              enhancerKeys: eKeys,
            };
            data.component = (
              <EnhancerSelector
                defaultValue={eo}
                onChange={(v) => {
                  const cds = (category.componentsData ?? []).filter(a => a.componentType != ComponentType.Enhancer);
                  let newCds = cds;
                  if (v.enhancerKeys?.length > 0) {
                    const eCds = v.enhancerKeys?.filter(k => k != undefined && k.length > 0).map(k => (
                      {
                        componentKey: k,
                        componentType: ComponentType.Enhancer,
                      }
                    ));
                    newCds = cds.concat(eCds);
                  }
                  setCategory({
                    ...category,
                    enhancementOptions: v,
                    componentsData: newCds,
                  });
                }}
              />
            );
            const skip = !category?.enhancementOptions;
            data.operations.push(
              <Button
                type={skip ? 'normal' : 'primary'}
                size={'large'}
                text={skip}
                key={s}
                onClick={goToNextStep}
              >
                {t(skip ? 'Set it later and go to next step' : 'Next step')}
              </Button>,
            );
            break;
          }
          case Step.Others: {
            data.component = (
              <>
                <div className={'other-options'}>
                  <div className="option">
                    <div className="label">{t('Priority on cover selection')}</div>
                    <div className="value">
                      <Radio.Group
                        size={'large'}
                        dataSource={coverSelectOrders.map((a) => ({
                          label: t(a.label),
                          value: a.value,
                        }))}
                        onChange={(v) => {
                          setCategory({
                            ...category,
                            coverSelectionOrder: v,
                          });
                        }}
                      />
                    </div>
                  </div>
                  <div className="option">
                    <div className="label">
                      {t('Generate nfo')}
                      <Balloon.Tooltip
                        trigger={(
                          <CustomIcon type={'question-circle'} />
                        )}
                        align={'t'}
                      >
                        {t('You can share tags and rate of same physical filesystem item from different app instances by enabling this option, ' +
                          'but it may cause poor performance of tag-related operations.')}
                        {t('Generated nfo files are not capable loaded by other apps.')}
                      </Balloon.Tooltip>
                    </div>
                    <div className="value">
                      <Switch
                        size={'small'}
                        checked={category?.generateNfo}
                        onChange={(v) => setCategory({
                          ...category,
                          generateNfo: v,
                        })}
                      />
                    </div>
                  </div>
                </div>
                <div className="sync-after-saving">
                  <Checkbox
                    label={t('Sync after saving')}
                    checked={syncAfterSaving}
                    onChange={(c) => setSyncAfterSaving(c)}
                  />
                  <div className="tip">
                    {t('Resources will not be loaded using new configuration automatically if this option is disabled.')}
                  </div>
                </div>
              </>
            );
            data.operations.push(
              <Button
                type={'primary'}
                key={s}
                size={'large'}
                onClick={() => {
                  const dialog = Dialog.notice({
                    title: t('Saving'),
                    closeable: false,
                    footer: false,
                  });
                  // console.log({
                  //   category,
                  //   mediaLibraries,
                  //   syncAfterSaving,
                  // });
                  // return;
                  SaveDataFromSetupWizard({
                    model: {
                      category,
                      mediaLibraries,
                      syncAfterSaving,
                    },
                  })
                    .invoke((a) => {
                      if (!a.code) {
                        history.push('/category');
                      }
                    })
                    .finally(() => {
                      dialog.hide();
                    });
                }}
              >
                {t(category?.id > 0 ? 'Save' : 'Create now')}
              </Button>,
            );
            break;
          }
        }
      }

      steps.push(data);
    }

    return (
      steps.map((s) => {
        const info = StepInfo[s.step];
        return (
          <div className={`step step-${s.step}`} key={s.step}>
            <div className={'step-inner'}>
              <div className="title">{t(info.label)}</div>
              {info.tips?.length > 0 && (
                <div className="help">
                  <Message type={'notice'}>
                    {info.tips.map((tip, i) => {
                        return (
                          <React.Fragment key={i}>
                            {t(tip)}
                            {i != info.tips.length - 1 && <br />}
                          </React.Fragment>
                        );
                      },
                    )}
                  </Message>
                </div>
              )}
              <div className={'component'}>
                {s.component}
              </div>
              <div className="operations">
                {s.operations}
              </div>
            </div>
          </div>
        );
      })
    );
  };

  console.log('[SetupWizard]step', `-------------------------${step}-------------------------`);
  console.log('[SetupWizard]category', category);
  console.log('[SetupWizard]mediaLibraries', mediaLibraries);
  console.log('[SetupWizard]mediaLibrary', mediaLibrary);
  console.log('[SetupWizard]pscSamplePath', pscSamplePath);
  console.log('[SetupWizard]pscValue', pscValue);
  console.log('[SetupWizard]pathConfiguration', pathConfiguration);
  console.log('[SetupWizard]', '-------------------------end-------------------------');

  return (
    <div className={'new-category-page'}>
      <div className="top">
        <div className={'back'}>
          {step > Step.CategoryName && (
            <Button
              type={'normal'}
              size={'small'}
              onClick={() => {
                if (step > Step.CategoryName) {
                  const stepInfo = StepInfo[step];
                  if (stepInfo.prevStep) {
                    setStep(stepInfo.prevStep);
                  } else {
                    setStep(step - 1);
                  }
                }
              }}
            >
              <CustomIcon type={'arrowleft'} size={'xs'} />
              {t('Return to previous step')}
            </Button>
          )}
        </div>
        <div className="step-indicator">
          <StepComponent
            current={StepStepIndicatorMap[step].index}
            stretch
            shape={'arrow'}
          >
            {stepIndicatorItems.map(si => {
              return (
                <StepComponent.Item
                  key={si.index}
                  title={t(si.label)}
                  // content={item[1]}
                />
              );
            })}
          </StepComponent>
        </div>
        <div className="end-creation">
          <Link to={'/category'}>
            <Button
              size={'small'}
              // type={'secondary'}
              warning
            >
              <CustomIcon type={'close-circle'} size={'xs'} />
              {t('End creation')}
            </Button>
          </Link>
        </div>
      </div>
      <div className="steps">
        <Slider
          animation={'fade'}
          speed={200}
          infinite={false}
          activeIndex={step - 1}
          draggable={false}
          arrows={false}
          dots={false}
          adaptiveHeight
        >
          {renderSteps()}
        </Slider>
      </div>

    </div>
  );
};
