import { Button, Dropdown, Menu, Tag } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import { useUpdate } from 'react-use';
import FilterDialog from '../FilterDialog';
import type { IBulkModificationFilterGroup } from '../../BulkModification';
import TargetRenderer from './components/TargetRenderer';
import ClickableIcon from '@/components/ClickableIcon';
import {
  BulkModificationFilterGroupOperation,
  bulkModificationFilterGroupOperations,
  BulkModificationFilterOperation,
  BulkModificationProperty,
} from '@/sdk/constants';
import { uuidv4 } from '@/components/utils';
import {
  buildFilterGroupReactKey,
  buildFilterReactKey,
} from '@/pages/BulkModification/components/BulkModification/FilterGroup/helpers';

interface IProps {
  group: IBulkModificationFilterGroup;
  isRoot?: boolean;
  onRemove?: () => any;
  onChange?: (group: IBulkModificationFilterGroup) => any;
}

const FilterGroup = (props: IProps) => {
  const forceUpdate = useUpdate();

  const { t } = useTranslation();
  const { group, isRoot = false, onRemove, onChange: propsOnChange } = props;

  // console.log(group);

  const onChange = () => {
    propsOnChange?.(group);
  };

  const renderConditions = () => {
    if (!group.filters && !group.groups) {
      return;
    }

    const filters: any[] = (group.filters || []).map((filter, i) => (
      <Tag.Closeable
        key={buildFilterReactKey(filter)}
        // key={`f-${i}`}
        size={'small'}
        className={'filter'}
        onClose={() => {
          group.filters!.splice(i, 1);
          forceUpdate();
          onChange();
          return true;
        }}
        onClick={() => {
          FilterDialog.show(
            {
              filter,
              onSubmit: filter => {
                group.filters![i] = filter;
                forceUpdate();
                onChange();
              },
            },
          );
        }}
      >
        <span className="property">
          {t(BulkModificationProperty[filter.property!])}{filter?.propertyKey}
        </span>
        <span className="operation">
          {t(`BulkModificationFilterOperation.${BulkModificationFilterOperation[filter.operation!]}`)}
        </span>
        <span className={'target'}>
          <TargetRenderer
            property={filter.property!}
            target={filter.target}
          />
        </span>
      </Tag.Closeable>
    ));

    const groups: any[] = (group.groups || []).map((g, i) => (
      <FilterGroup
        key={buildFilterGroupReactKey(g)}
        group={g}
        onRemove={() => {
          group.groups!.splice(i, 1);
          forceUpdate();
          onChange();
        }}
        onChange={onChange}
      />
    ));

    const conditions = filters.concat(groups);
    const elements: any[] = [];

    console.log(group);

    for (let i = 0; i < conditions.length; i++) {
      elements.push(conditions[i]);
      if (i < conditions.length - 1) {
        elements.push(
          <Dropdown
            key={`dropdown-${i}`}
            trigger={(
              <Button
                size={'small'}
                text
                type={'primary'}
              >
                {t(`BulkModificationFilterGroupOperation.${BulkModificationFilterGroupOperation[group.operation]}`)}
              </Button>
            )}
            triggerType={['click']}
          >
            <Menu>
              {bulkModificationFilterGroupOperations.map(operation => (
                <Menu.Item
                  key={operation.value}
                  onClick={() => {
                    group.operation = operation.value;
                    forceUpdate();
                    onChange();
                  }}
                  disabled={group.operation == operation.value}
                >
                  {t(`BulkModificationFilterGroupOperation.${BulkModificationFilterGroupOperation[operation.value]}`)}
                </Menu.Item>
              ))}
            </Menu>
          </Dropdown>,
        );
      }
    }
    return elements;
  };

  return (
    <div
      className="group"
      onClick={() => {

      }}
    >
      {renderConditions()}
      <Dropdown
        trigger={<ClickableIcon colorType={'normal'} type={'plus-circle'} size={'small'} />}
        // onVisibleChange={console.log}
        triggerType={['click']}
        afterOpen={() => console.log('after open')}
        onClick={e => e.preventDefault()}
      >
        <Menu>
          <Menu.Item onClick={() => {
            FilterDialog.show({
              onSubmit: filter => {
                if (!group.filters) {
                  group.filters = [];
                }
                group.filters.push(filter);
                forceUpdate();
                onChange();
              },
            });
          }}
          >{t('Add a filter')}</Menu.Item>
          <Menu.Item onClick={() => {
            if (!group.groups) {
              group.groups = [];
            }
            group.groups.push({ operation: BulkModificationFilterGroupOperation.And });
            forceUpdate();
          }}
          >{t('Add a group')}</Menu.Item>
        </Menu>
      </Dropdown>
      {isRoot ? null : (
        <ClickableIcon
          colorType={'danger'}
          type={'delete'}
          size={'small'}
          onClick={onRemove}
        />)}
    </div>
  );
};

export default FilterGroup;
