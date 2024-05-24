import { Badge, Balloon, Dialog, Message, Tag } from '@alifd/next';
import React from 'react';
import { useTranslation } from 'react-i18next';
import PscMatcher from '@/components/PathSegmentsConfiguration/models/PscMatcher';
import { ResourceProperty } from '@/sdk/constants';
import { Button } from '@/components/bakaui';
import BApi from '@/sdk/BApi';
import BusinessConstants from '@/components/BusinessConstants';
import { buildLayerBasedPathRegexString } from '@/components/utils';
import { MatcherValue } from '@/components/PathSegmentsConfiguration/models/MatcherValue';

export default () => {
  const { t } = useTranslation();
  if (fileResource) {
    const rootSegmentIndex = (PscMatcher?.match(segments, value[ResourceProperty.RootPath]?.[0],
      -1, undefined))?.index ?? -1;
    const rootPathIsSelected = rootSegmentIndex > -1;
    const loaderBtn = (
      <Button
        type={'primary'}
        text
        style={{ marginTop: '10px' }}
        loading={loadingFileResourceExtensions}
        disabled={!rootPathIsSelected}
        onClick={() => {
          setLoadingFileResourceExtensions(true);
          BApi.file.getFileExtensionCounts({
            sampleFile: fileResource,
            rootPath: segments.slice(0, rootSegmentIndex + 1).join(BusinessConstants.pathSeparator),
          })
            .then(a => {
              if (!a.code) {
                const counts = a.data ?? {};
                const list = Object.keys(counts)
                  .sort((x, y) => counts[y]! - counts[x]!)
                  .map(b => ({
                    ext: b,
                    count: counts[b]!,
                  }));
                setFileResourceExtensions(list);
                if (currentFileExt) {
                  setFileResourceExtensionCandidates([currentFileExt]);

                  // we should change the value of resource matcher to file-extension-based regex immediately when user click this button
                  const newRegex = buildLayerBasedPathRegexString(segments.length - rootSegmentIndex - 1, [currentFileExt]);
                  value[ResourceProperty.Resource] = [
                    MatcherValue.Regex(newRegex),
                  ];
                  setValue({ ...value });
                }
              }
            })
            .finally(() => {
              setLoadingFileResourceExtensions(false);
            });
        }}
      >
        {t('I need choose files with specific types as resources')}
      </Button>
    );

    return (
      <div className="file-resource-helper psc-block">
        <div className="title">{t('Match by file extensions')}</div>
        {fileResourceExtensions == undefined ? (
          <>
            <Message type={'warning'}>
              <>
                {t('Please pay attention, you marked a file as a resource. We\'ll treat all files or folders with same path layer as resources by default. It may not be your actual design(if you chose a movie and its subtitle file has the same path layer, then the subtitle file will be another resource).')}
                {t('If you want to mark resources with specific file types, you can click button below to load all file types and select some of them.')}
              </>
            </Message>
            {rootPathIsSelected ? loaderBtn : (
              <Balloon.Tooltip
                trigger={loaderBtn}
                triggerType={'hover'}
                align={'t'}
              >
                {t('Please select root path first.')}
              </Balloon.Tooltip>
            )}
          </>
        ) : fileResourceExtensions.length > 0 ? (
          <>
            <Message type={'notice'}>
              <>
                {t('You can mark resources by file types.')}
                {fileResourceExtensionCandidates.length > 0
                  ? `${t('You selected {{count}} file types:', { count: fileResourceExtensionCandidates.length })}${fileResourceExtensionCandidates.join(', ')}`
                  : t('None of file types has been selected, we\'ll treat all files or folders with same path layer as your file as resources.')}
              </>
            </Message>
            <Tag.Group style={{ marginTop: 5 }}>
              {fileResourceExtensions.map(e => {
                return (
                  <Tag.Selectable
                    size={'small'}
                    checked={fileResourceExtensionCandidates.indexOf(e.ext) > -1}
                    onChange={c => {
                      const candidates = fileResourceExtensionCandidates || [];
                      const idx = candidates.indexOf(e.ext);

                      const apply = candidates => {
                        const newRegex = buildLayerBasedPathRegexString(segments.length - rootSegmentIndex - 1, candidates);
                        value[ResourceProperty.Resource] = [
                          MatcherValue.Regex(newRegex),
                        ];
                        setValue({ ...value });
                        setFileResourceExtensionCandidates([...candidates]);
                      };

                      if (idx == -1) {
                        candidates.push(e.ext);
                      } else {
                        if (e.ext == currentFileExt) {
                          Dialog.confirm({
                            title: t('You are deselecting extension same as file you selected'),
                            content: t('After your deselection, the current file will not be treat as a resource, and you need reset the Resource part from path segments, are you sure to remove file type: {{ext}}?', { ext: currentFileExt }),
                            closeable: true,
                            v2: true,
                            closeMode: ['close', 'esc', 'mask'],
                            onOk: () => new Promise((resolve, reject) => {
                              candidates.splice(idx, 1);
                              apply(candidates);
                              resolve(undefined);
                            }),
                          });
                          return;
                        } else {
                          candidates.splice(idx, 1);
                        }
                      }
                      apply(candidates);
                    }}
                  >
                    {e.ext}
                    &nbsp;
                    <Badge count={e.count} overflowCount={99999999} style={{ backgroundColor: '#87d068' }} />
                  </Tag.Selectable>
                );
              })}
            </Tag.Group>
          </>
        ) : <>
          {t('No known file type is found.')}
        </>}
      </div>
    );
  }
  return;
};
