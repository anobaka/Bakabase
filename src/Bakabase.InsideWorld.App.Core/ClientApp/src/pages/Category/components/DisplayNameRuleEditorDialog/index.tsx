import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Chip, Modal } from '@/components/bakaui';
import { createPortalOfComponent } from '@/components/utils';
import BApi from '@/sdk/BApi';
import { ResourceCategoryAdditionalItem, SpecialTextType } from '@/sdk/constants';
import type { IProperty } from '@/components/Property/models';

interface IProps {
  categoryId: number;
}

interface ICategory {
  id: number;
  name: string;
  rule?: string;
}

enum RulePartType {
  Text = 1,
  Wrapper = 2,
  Property = 3,
}

type RulePart = {text: string; type: RulePartType};
type Wrapper = {left: string; right: string};

const DisplayNameRuleEditorDialog = ({ categoryId }: IProps) => {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(true);

  const [wrappers, setWrappers] = useState< Wrapper[]>([]);
  const [category, setCategory] = useState<ICategory>();

  const [properties, setProperties] = useState<IProperty[]>([]);

  useEffect(() => {
    BApi.specialText.getAllSpecialText().then(r => {
      const texts = r.data?.[SpecialTextType.Wrapper] || [];
      const wrappers = texts.map(text => {
        return {
          left: text.value1!,
          right: text.value2!,
        };
      });
      setWrappers(wrappers);
    });

    BApi.resourceCategory.getResourceCategory(categoryId, { additionalItems: ResourceCategoryAdditionalItem.CustomProperties }).then(r => {
      const c = r.data || {};
      setCategory({
        id: c.id!,
        name: c.name!,
        rule: r.data!.displayNameRule?.textRule,
      });

      const arr: IProperty[] = [];
      const cps = c.customProperties as IProperty[] || [];
      arr.push(...cps);
      setProperties(arr);
    });
  }, []);

  const close = () => {
    setVisible(false);
  };

  const extractRuleParts = (rule: string, wrappedVariables: string[], wrappers: Wrapper[]): RulePart[] => {
    const parts: RulePart[] = [];
    for (let i = 0; i < rule.length; i++) {
      const c = rule[i];
      let matched = false;
      if (c == '{') {
        for (const wv of wrappedVariables) {
          if (rule.startsWith(wv, i)) {
            parts.push({
              text: wv,
              type: RulePartType.Property,
            });
            i += wv.length - 1;
            matched = true;
            break;
          }
        }
      }
      if (!matched) {
        if (wrappers.some(w => w.left == c || w.right == c)) {
          parts.push({
            text: c,
            type: RulePartType.Wrapper,
          });
        } else {
          const prevPart = parts[parts.length - 1];
          if (prevPart?.type == RulePartType.Text) {
            prevPart.text += c;
          } else {
            parts.push({
              text: c,
              type: RulePartType.Text,
            });
          }
        }
      }
    }
    return parts;
  };

  const renderRule = () => {
    const rule = category?.rule || '';
    return rule;
    const wrappedVariables = properties.map(p => `{${p.name}}`);
    const parts = extractRuleParts(rule, wrappedVariables, wrappers);
    console.log(parts);
    return parts.map(p => {
      switch (p.type) {
        case RulePartType.Text:
          return (
            <span>{p.text}</span>
          );
        case RulePartType.Wrapper:
          return (
            <span className={'font-bold'}>{p.text}</span>
          );
        case RulePartType.Property:
          return (
            <span style={{ color: 'var(--bakaui-primary)' }}>{p.text}</span>
          );
      }
    });
  };

  return (
    <Modal
      visible={visible}
      title={t('Edit display name rule for category {{name}}', { name: category?.name })}
      onClose={close}
      size={'xl'}
    >
      <div>
        <div>
          <div>
            {t('You can use any combination of text and following properties in rule, and you can add more properties in category configuration.')}
          </div>
          <div>
            {t('To add a property value as a variable in the rule, you can use the following format: {name of property}. For example, {name} will be replaced with the value of the property named "name".')}
          </div>
          <div>
            {t('Be careful if you have multiple properties with same name, only a random one will be replaced.')}
          </div>
        </div>
        <div className={'flex flex-wrap gap-1'}>
          {properties.map(p => (
            <Chip
              size={'sm'}
              key={p.id}
            >
              {p.name}
            </Chip>
          ))}
        </div>
      </div>
      <div>
        <div>
          {t('You can safely use any of following text wrappers to wrap the properties, and wrappers surrounding the property with empty value will be removed automatically. ' +
            'You can check and set the wrappers in special text configuration.')}
        </div>
        <div className={'flex flex-wrap gap-1'}>
          {wrappers.map(w => (
            <Chip
              size={'sm'}
              key={w.left}
            >
              {w.left}
              &emsp;
              {w.right}
            </Chip>
          ))}
        </div>
      </div>
      <div>
        <div>{t('Display name rule')}</div>
        <div
          suppressContentEditableWarning
          contentEditable
          className={'border-1 rounded'}
          onInput={e => {
            const nv = e.currentTarget.textContent || '';
            category!.rule = nv;
            setCategory({
              ...category!,
            });
          }}
        >
          {renderRule()}
        </div>
      </div>
    </Modal>
  );
};

DisplayNameRuleEditorDialog.show = (props: IProps) => createPortalOfComponent(DisplayNameRuleEditorDialog, props);

export default DisplayNameRuleEditorDialog;
