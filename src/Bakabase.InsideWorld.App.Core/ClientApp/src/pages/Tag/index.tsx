import React, { useEffect, useRef, useState } from 'react';
import './index.scss';
import { Badge, Button, Dialog, Input, Message, Select } from '@alifd/next';
import i18n from 'i18next';
import IceLabel from '@icedesign/label';
import { useUpdateEffect } from 'react-use';
import { AddTagGroups, BulkDeleteTags, GetAllTagGroups, GetAllTags, SortTagGroups, SortTagsInGroup, UpdateTag } from '@/sdk/apis';
import SortableTagGroupList from '@/pages/Tag/components/SortableTagGroupList';
import SimpleLabel from '@/components/SimpleLabel';

export default () => {
  const [isBulkDeleting, setIsBulkDeleting] = useState(false);
  const [selectedTags, setSelectedTags] = useState([]);

  const sortableTagGroupsRef = useRef();

  useUpdateEffect(() => {
    setSelectedTags([]);
  }, [isBulkDeleting]);

  return (
    <div className={'tag-page'}>
      <div className="title">
        <div className="left">
          <div className="label">{i18n.t('Tags')}</div>
          {/* <Badge count={filteredTags.length} overflowCount={999999} /> */}
          <SimpleLabel status={'default'}>{i18n.t('Alias applied')}</SimpleLabel>
        </div>
        <div className="right">
          <Button
            size={'small'}
            onClick={() => {
              let names;
              const dialog = Dialog.show({
                footer: false,
                closeMode: ['mask', 'esc'],
                content: (
                  <Input.Group addonAfter={(
                    <Button
                      type={'normal'}
                      size={'large'}
                      onClick={() => {
                        const ts = names?.filter((a) => a)
                          .map((a) => a.trim());
                        if (ts?.length > 0) {
                          AddTagGroups({
                            model: {
                              names: ts,
                            },
                          })
                            .invoke((a) => {
                              if (!a.code) {
                                sortableTagGroupsRef.current.loadAllGroups();
                                dialog.hide();
                              }
                            });
                        }
                      }}
                    >{i18n.t('Add')}
                    </Button>
                  )}
                  >
                    <Select
                      mode={'tag'}
                      style={{ width: 600 }}
                      size={'large'}
                      visible={false}
                      placeholder={i18n.t('Tag group name list, and space is the separator, and press entry to submit.')}
                      onChange={(v) => names = v}
                    />
                  </Input.Group>
                ),
              });
            }}
          >
            {i18n.t('Add tag groups')}
          </Button>
          {isBulkDeleting ? (
            <Button.Group
              size={'small'}
            >
              <Button
                warning
                size={'small'}
                onClick={() => {
                  if (selectedTags?.length > 0) {
                    const tagNamesToBeDeleted = selectedTags.map((x) => x.displayName);
                    Dialog.confirm({
                      title: i18n.t('Sure to delete the following tags?'),
                      className: 'tags-bulk-deleting-dialog',
                      content: (
                        <div className={'tags'}>
                          {tagNamesToBeDeleted.map((n) => (
                            <span>{n}</span>
                          ))}
                        </div>
                      ),
                      closeable: true,
                      onOk: () => new Promise(((resolve, reject) => {
                        BulkDeleteTags({
                          model: selectedTags.map(t => t.id),
                        })
                          .invoke((a) => {
                            if (!a.code) {
                              sortableTagGroupsRef.current.loadAllTags();
                              setIsBulkDeleting(false);
                              resolve();
                            } else {
                              reject();
                            }
                          })
                          .catch(() => {
                            reject();
                          });
                      })),
                    });
                  } else {
                    Message.error(i18n.t('Nothing selected'));
                  }
                }}
              >{i18n.t('Bulk delete')}&nbsp;{selectedTags.length}&nbsp;{i18n.t('tags')}
              </Button>
              <Button
                size={'small'}
                onClick={() => {
                  setIsBulkDeleting(false);
                }}
              >
                {i18n.t('Cancel')}
              </Button>
            </Button.Group>
          ) : (
            <Button
              warning
              size={'small'}
              onClick={() => {
                setIsBulkDeleting(true);
              }}
            >
              {i18n.t('Bulk delete')}
            </Button>
          )}

        </div>
      </div>
      <div className="groups-container">
        <SortableTagGroupList
          ref={sortableTagGroupsRef}
          selectedTagIds={selectedTags.map(s => s.id)}
          onSelectTag={(tag, selected) => {
            console.log(tag, selected);
            let newSelectedTags = (selectedTags || []).slice();
            if (selected) {
              if (newSelectedTags.every(a => a.id != tag.id)) {
                newSelectedTags.push(tag);
              }
            } else {
              newSelectedTags = newSelectedTags.filter((a) => a.id != tag.id);
            }
            setSelectedTags(newSelectedTags);
          }}
          isBulkDeleting={isBulkDeleting}
        />
      </div>
    </div>
  );
};
