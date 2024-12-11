import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { Resource as ResourceModel } from '@/core/models/Resource';
import {
  Checkbox,
  Chip,
  Modal,
  Pagination,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Tooltip,
} from '@/components/bakaui';
import type { DestroyableProps } from '@/components/bakaui/types';
import Resource from '@/components/Resource';
import type { RecursivePartial } from '@/components/types';
import ToResourceSelector from '@/components/ResourceTransferModal/ToResourceSelector';
import BApi from '@/sdk/BApi';

type Props = {
  fromResources: ResourceModel[];
} & DestroyableProps;

const PageSize = 100;

type InputModelItem = {
  fromId: number;
  toId: number;
  keepMediaLibrary: boolean;
  deleteSourceResource: boolean;
};

const defaultInputModelItem: Partial<InputModelItem> = {
  keepMediaLibrary: true,
  deleteSourceResource: false,
};

type InputModel = {
  items: InputModelItem[];
  keepMediaLibraryForAll: boolean;
  deleteAllSourceResources: boolean;
};

const defaultInputModel: RecursivePartial<InputModel> = {
  items: [],
  keepMediaLibraryForAll: true,
  deleteAllSourceResources: false,
};

const addItem = (inputModel: RecursivePartial<InputModel>, item: RecursivePartial<InputModelItem>) => {
  if (!inputModel.items?.includes(item)) {
    inputModel.items ??= [];
    inputModel.items.push(item);
  }
};

export default ({
                  fromResources,
                  onDestroyed,
                }: Props) => {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);

  const [resources, setResources] = useState<ResourceModel[]>([]);
  const [inputModel, setInputModel] = useState<RecursivePartial<InputModel>>({ ...defaultInputModel });
  const [resourcePool, setResourcePool] = useState<Record<number, ResourceModel>>({});
  // const [loadingTargetResourceCandidates, setLoadingTargetResourceCandidates] = useState(false);
  // const [targetResourceCandidates, setTargetResourceCandidates] = useState<ResourceModel[]>([]);
  // const [targetResourceCandidatesOfFromId, setTargetResourceCandidatesOfFromId] = useState<number>();


  useEffect(() => {
    const resources = fromResources.slice((page - 1) * PageSize, page * PageSize);
    setResources(resources);
  }, [page, fromResources]);

  const TPagination = useCallback(({ page }: { page: number }) => {
    const total = Math.ceil(fromResources.length / PageSize);
    if (total > 1) {
      return (
        <Pagination
          total={total}
          page={page}
          onChange={page => setPage(page)}
        />
      );
    }
    return null;
  }, []);

  return (
    <Modal
      title={t('Transfer resources')}
      defaultVisible
      size={'full'}
      onDestroyed={onDestroyed}
    >
      <div className={'flex items-center justify-between'}>
        <div className={'flex items-center gap-2'}>
          <Checkbox
            onValueChange={v => {
              setInputModel({
                ...inputModel,
                deleteAllSourceResources: v,
              });
            }}
            isSelected={inputModel.deleteAllSourceResources}
          >{t('Delete all source resources')}</Checkbox>
          <Tooltip
            color={'secondary'}
            content={(
              <div>
                <div>{t('Category and media library of target resource will be replaced with data of source resource by default.')}</div>
                <div>{t('By enable this options, the category and media library of target resource will not be changed.')}</div>
              </div>
            )}
          >
            <Checkbox
              isSelected={inputModel.keepMediaLibraryForAll}
              onValueChange={v => {
                setInputModel({
                  ...inputModel,
                  keepMediaLibraryForAll: v,
                });
              }}
            >{t('Keep media libraries for all target sources')}</Checkbox>
          </Tooltip>
        </div>
        <div>
          Tips
        </div>
      </div>
      <TPagination page={page} />
      {resources.length == 0 ? (
        <div>{t('No resources to be transferred')}</div>
      ) : (
        <Table removeWrapper>
          <TableHeader>
            <TableColumn width={100}>{t('#')}</TableColumn>
            <TableColumn width={200}>{t('Source resource')}</TableColumn>
            <TableColumn width={200}>{t('Resource will be overwritten')}</TableColumn>
            <TableColumn>{t('Operations')}</TableColumn>
          </TableHeader>
          <TableBody>
            {resources.map((x, i) => {
              const item = inputModel.items?.find(y => y?.fromId == x.id) || {
                ...defaultInputModelItem,
                fromId: x.id,
              };
              const toResource = resourcePool[item.toId ?? 0];
              return (
                <TableRow>
                  <TableCell>
                    <div className={'flex justify-center'}>
                      <Chip radius={'sm'}>{i + 1}</Chip>
                    </div>
                  </TableCell>
                  <TableCell>
                    <Resource resource={x} />
                  </TableCell>
                  <TableCell>
                    {item.toId
                      ? toResource ? (
                        <Resource resource={toResource} />
                      ) : t('Unknown resource')
                      : t('Please select an target resource on the right side')}
                  </TableCell>
                  <TableCell className={'flex flex-col'}>
                    <div className={'h-full flex flex-col gap-2'}>
                      <ToResourceSelector onSelect={id => {
                        item.toId = id;
                        BApi.resource.getResourcesByKeys({ ids: [id] }).then(res => {
                          const r = res.data?.[0];
                          if (r) {
                            setResourcePool({
                              ...resourcePool,
                              [id]: r,
                            });
                          }
                        });
                        addItem(inputModel, item);
                        setInputModel({ ...inputModel });
                      }}
                      />
                      <Checkbox
                        isDisabled={inputModel.deleteAllSourceResources}
                        onValueChange={v => {
                          console.log(item);
                          item.deleteSourceResource = v;
                          addItem(inputModel, item);
                          console.log(inputModel);
                          setInputModel({ ...inputModel });
                        }}
                        isSelected={inputModel.deleteAllSourceResources || item.deleteSourceResource}
                      >{t('Delete source')}</Checkbox>
                      <Checkbox
                        isDisabled={inputModel.keepMediaLibraryForAll}
                        isSelected={inputModel.keepMediaLibraryForAll || item.keepMediaLibrary}
                        onValueChange={v => {
                          item.keepMediaLibrary = v;
                          addItem(inputModel, item);
                          setInputModel({ ...inputModel });
                        }}
                      >{t('Keep media library')}</Checkbox>
                    </div>
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      )}
      <TPagination page={page} />
    </Modal>
  );
};
