import { useEffect, useState } from 'react';
import MultilevelValueEditor from '@/components/StandardValue/ValueEditor/Editors/MultilevelValueEditor';
import type { MultilevelData } from '@/components/StandardValue/models';
import BApi from '@/sdk/BApi';
import type { DestroyableProps } from '@/components/bakaui/types';

const StringMultilevelValueEditor = MultilevelValueEditor<string>;

type Category = { id: number; name: string };
type Library = { id: number; name: string; categoryId: number };

type Props = DestroyableProps & {
  onSelected: (dbValue: string[], bizValue: string[][]) => any;
  multiple?: boolean;
};

export default (props: Props) => {
  const {
    onSelected,
    multiple = true,
  } = props;
  const [categories, setCategories] = useState<Category[]>([]);
  const [libraries, setLibraries] = useState<Library[]>([]);

  useEffect(() => {
    BApi.category.getAllCategories().then(r => {
      // @ts-ignore
      setCategories(r.data || []);
    });
    BApi.mediaLibrary.getAllMediaLibraries().then(r => {
      // @ts-ignore
      setLibraries(r.data || []);
    });
  }, []);

  return (
    <StringMultilevelValueEditor
      getDataSource={async () => {
        const multilevelData: MultilevelData<string>[] = [];
        categories.forEach((c) => {
          const md: MultilevelData<string> = {
            value: `c-${c.id}`,
            label: c.name,
            children: libraries
              .filter(l => l.categoryId == c.id)
              .map<MultilevelData<string>>(x => ({
                value: x.id.toString(),
                label: x.name,
              })),
          };

          multilevelData.push(md);
        });
        return multilevelData.filter(d => d.children && d.children.length > 0);
      }}
      onValueChange={onSelected}
      multiple={multiple}
    />
  );
};
