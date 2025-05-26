import { useTranslation } from 'react-i18next';
import React, { useEffect, useState } from 'react';
import { DeleteOutlined, EditOutlined } from '@ant-design/icons';
import toast from 'react-hot-toast';
import {
  Button,
  Chip,
  Input,
  Modal,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Textarea,
} from '@/components/bakaui';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import BApi from '@/sdk/BApi';
import type { BootstrapModelsResponseModelsBaseResponse } from '@/sdk/Api';

export type ExtensionGroup = {
  id: number;
  name: string;
  extensions?: string[];
};

// const testGroups: Group[] = [
//   {
//     id: 1,
//     name: 'Group 1',
//     extensions: ['.jpg', '.png'],
//   },
//   {
//     id: 2,
//     name: 'Group 2',
//     extensions: ['.mp4', '.avi'],
//   },
//   {
//     id: 3,
//     name: 'Group 3',
//     extensions: ['.docx', '.pdf'],
//   },
//   {
//     id: 4,
//     name: 'Group 4',
//     extensions: ['.xlsx', '.csv'],
//   },
//   {
//     id: 5,
//     name: 'Group 5',
//     extensions: ['.pptx', '.txt'],
//   },
// ];

function extractExtensions(text: string): string[] {
  const extensions = text.replace(/\n/g, ' ').split(' ').map(x => x.trim().replace(/^\.+|\.+$/g, '')).filter(x => x.length > 0).map(x => `.${x}`);
  return Array.from(new Set(extensions));
}

export default () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [groups, setGroups] = useState<ExtensionGroup[]>([]);
  const [editingGroup, setEditingGroup] = useState<ExtensionGroup | undefined>(undefined);
  const [editingExtensionsText, setEditingExtensionsText] = useState<string>('');

  useEffect(() => {
    BApi.extensionGroup.getAllExtensionGroups().then(r => {
      setGroups(r.data || []);
    });
  }, []);

  return (
    <div>
      <Modal
        visible={!!editingGroup}
        title={t('Editing extension group')}
        onClose={() => setEditingGroup(undefined)}
        onOk={async () => {
          let r: BootstrapModelsResponseModelsBaseResponse;
          if (editingGroup!.id == 0) {
            r = await BApi.extensionGroup.addExtensionGroup(editingGroup!);
            if (!r.code) {
              groups.push(editingGroup!);
            }
          } else {
            r = await BApi.extensionGroup.putExtensionGroup(editingGroup!.id, editingGroup!);
            groups.splice(groups.findIndex(x => x == editingGroup), 1, editingGroup!);
          }
          if (r.code) {
            toast.error(r.message!);
            throw new Error(r.message);
          }
          setEditingGroup(undefined);
        }}
      >
        <Input
          label={t('Name')}
          onValueChange={v => {
            setEditingGroup({
              ...editingGroup!,
              name: v,
            });
          }}
          value={editingGroup?.name}
          isRequired
        />
        <div>
          <Textarea
            label={t('Extensions')}
            placeholder={t('Separate by space or newline')}
            value={editingExtensionsText}
            onValueChange={v => {
              setEditingExtensionsText(v);
              setEditingGroup({
                ...editingGroup!,
                extensions: extractExtensions(v)!,
              });
            }}
            fullWidth
            minRows={4}
          />
          <div className={'mt-2 flex flex-wrap gap-1'}>
            {editingGroup?.extensions.map((ext, i) => {
              return (
                <Chip size={'sm'} variant={'flat'} radius={'sm'}>{ext}</Chip>
              );
            })}
          </div>
        </div>
      </Modal>
      <div>
        <Button
          size={'sm'}
          color={'primary'}
          onPress={() => {
            setEditingExtensionsText('');
            setEditingGroup({
              id: 0,
              name: '',
              extensions: [],
            });
          }}
        >
          {t('Add a group')}
        </Button>
      </div>
      <Table isStriped removeWrapper className={'mt-2'}>
        <TableHeader>
          <TableColumn>{t('Name')}</TableColumn>
          <TableColumn>{t('Extensions')}</TableColumn>
          <TableColumn>{t('Operations')}</TableColumn>
        </TableHeader>
        <TableBody>
          {groups.map((eg, i) => {
            return (
              <TableRow>
                <TableCell>{eg.name}</TableCell>
                <TableCell>{eg.extensions?.map((ext, j) => {
                  return (
                    <Chip
                      size={'sm'}
                      variant={'flat'}
                      isCloseable
                      onClose={async () => {
                        eg.extensions?.splice(j, 1);
                        await BApi.extensionGroup.putExtensionGroup(eg.id, eg);
                        setGroups([...groups]);
                      }}
                    >
                      {ext}
                    </Chip>
                  );
                })}</TableCell>
                <TableCell>
                  <div className={'flex items-center gap-1'}>
                    <Button
                      size={'sm'}
                      color={'primary'}
                      isIconOnly
                      variant={'light'}
                      onPress={() => {
                        setEditingExtensionsText(eg.extensions.join(' '));
                        setEditingGroup(JSON.parse(JSON.stringify(eg)));
                      }}
                    >
                      <EditOutlined className={'text-medium'} />
                    </Button>
                    <Button
                      size={'sm'}
                      color={'danger'}
                      isIconOnly
                      variant={'light'}
                      onPress={() => {
                        createPortal(Modal, {
                          defaultVisible: true,
                          title: t('Sure to delete?'),
                          children: t('Be careful, this operation can not be undone'),
                          onOk: async () => {
                            await BApi.extensionGroup.deleteExtensionGroup(eg.id);
                            groups.splice(i, 1);
                            setGroups(groups.slice());
                          },
                        });
                      }}
                    >
                      <DeleteOutlined className={'text-medium'} />
                    </Button>
                  </div>
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </div>
  );
};
