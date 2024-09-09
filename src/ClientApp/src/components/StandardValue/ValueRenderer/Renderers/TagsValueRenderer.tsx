import type { ValueRendererProps } from '../models';
import type { MultilevelData, TagValue } from '../../models';
import MultilevelValueEditor from '../../ValueEditor/Editors/MultilevelValueEditor';
import NotSet from './components/NotSet';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import { Card, CardBody, Chip } from '@/components/bakaui';
import { buildLogger, uuidv4 } from '@/components/utils';

type TagsValueRendererProps = ValueRendererProps<TagValue[], string[]> & {
  getDataSource?: () => Promise<(TagValue & {value: string})[]>;
};

const log = buildLogger('TagsValueRenderer');

export default (props: TagsValueRendererProps) => {
  const { createPortal } = useBakabaseContext();

  log(props);

  const {
    value,
    editor,
    variant,
    getDataSource,
  } = props;

  const simpleLabels = value?.map(v => {
    if (v.group != undefined && v.group.length > 0) {
      return `${v.group}:${v.name}`;
    }
    return v.name;
  });

  const showEditor = () => {
    createPortal(MultilevelValueEditor<string>, {
      value: editor?.value,
      getDataSource: async () => {
        const ds = await getDataSource?.() || [];
        const data: MultilevelData<string>[] = [];
        for (const d of ds) {
          if (d.group == undefined || d.group.length == 0) {
            data.push({
              value: d.value,
              label: d.name,
            });
          } else {
            let group = data.find(x => x.label == d.group);
            if (!group) {
              group = {
                value: uuidv4(),
                label: d.group,
                children: [],
              };
              data.push(group);
            }
            group.children!.push({
              value: d.value,
              label: d.name,
            });
          }
        }
        return data;
      },
      onValueChange: (dbValue, bizValue) => {
        if (dbValue) {
          const bv: TagValue[] = [];
          for (const b of bizValue!) {
            if (b.length == 1) {
              bv.push({
                name: b[0],
              });
            } else {
              bv.push({
                name: b[1],
                group: b[0],
              });
            }
          }

          editor?.onValueChange?.(dbValue, bv);
        }
      },
    });
  };

  const startEditing = editor ? showEditor : undefined;

  if (!value || value.length == 0) {
    return (
      <NotSet onClick={startEditing} />
    );
  }

  if (variant == 'light') {
    return (
      <span onClick={startEditing}>{simpleLabels?.join(',')}</span>
    );
  } else {
    return (
      <Card>
        <CardBody>
          <div className={'flex flex-wrap gap-1'} onClick={startEditing}>
            {simpleLabels?.map(l => {
              return (
                <Chip
                  size={'sm'}
                  radius={'sm'}
                >
                  {l}
                </Chip>
              );
            })}
          </div>
        </CardBody>
      </Card>
    );
  }
};
