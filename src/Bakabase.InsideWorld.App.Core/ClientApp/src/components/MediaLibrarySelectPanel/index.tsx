import { useEffect, useState } from 'react';
import { Tag } from '@alifd/next';
import React from 'react';
import BApi from '@/sdk/BApi';
import './index.scss';
import { MediaLibraryAdditionalItem } from '@/sdk/constants';

interface IProps {
  multiple?: boolean;
  value?: number[];
  onChange?: (ids: number[]) => any;
  defaultValue?: number[];
}

export default ({
                  multiple = false,
                  onChange,
                  value: propsValue,
  defaultValue,
                }: IProps) => {
  const [categoryLibraries, setCategoryLibraries] = useState<{
    category: string;
    libraries: {
      name: string;
      id: number;
    }[];
  }[]>([]);
  const [value, setValue] = useState<number[]>(propsValue || defaultValue || []);

  useEffect(() => {
      BApi.mediaLibrary.getAllMediaLibraries({ additionalItems: MediaLibraryAdditionalItem.Category }).then(r => {
        const cl = (r.data || []).reduce<{
          [cid: number]: {
            name: string;
            id: number;
          }[];
        }>((s, t) => {
          const arr = (s[t.category!.name!] ??= []);
          arr.push({
            name: t.name,
            id: t.id!,
          });
          return s;
        }, {});
        setCategoryLibraries(Object.keys(cl).map(category => ({
          category,
          libraries: cl[category],
        })));
      });
    }
    ,
    [],
  );
  return (
    <div className={'media-library-select-panel'}>
      {categoryLibraries.map((cl, i) => {
        return (
          <React.Fragment key={i}>
            <div className={'category'}>{cl.category}</div>
            <div className="libraries">
              <Tag.Group>
                {cl.libraries.map(l => {
                  const currentChecked = !!value?.includes(l.id);
                  return (
                    <Tag.Selectable
                      key={l.id}
                      checked={currentChecked}
                      className={'library'}
                      onChange={checked => {
                        if (!currentChecked && checked) {
                          const newValue = value && multiple ? [...value!, l.id] : [l.id];
                          if (!propsValue) {
                            setValue(newValue);
                          }
                          onChange && onChange(newValue);
                        }
                      }}
                    >{l.name}</Tag.Selectable>
                  );
                })}
              </Tag.Group>
            </div>
          </React.Fragment>
        );
      })}
    </div>
  );
};

