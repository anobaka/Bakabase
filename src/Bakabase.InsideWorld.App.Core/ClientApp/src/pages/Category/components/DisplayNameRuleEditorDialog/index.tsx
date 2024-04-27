import React, { useEffect, useRef, useState } from 'react';
import { Trans, useTranslation } from 'react-i18next';
import { renderToString } from 'react-dom/server';
import ContentEditable from 'react-contenteditable';
import { useUpdate } from 'react-use';
import { InfoCircleOutlined } from '@ant-design/icons';
import { Chip, Code, Modal, Button } from '@/components/bakaui';
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
}

enum RulePartType {
  Text = 1,
  Wrapper = 2,
  Property = 3,
}

type RulePart = { text: string; type: RulePartType };
type Wrapper = { left: string; right: string };

const DisplayNameRuleEditorDialog = ({ categoryId }: IProps) => {
  const { t } = useTranslation();
  const forceUpdate = useUpdate();
  const [visible, setVisible] = useState(true);

  const [wrappers, setWrappers] = useState<Wrapper[]>([]);
  const [category, setCategory] = useState<ICategory>();

  const [properties, setProperties] = useState<IProperty[]>([]);
  const [ruleHtml, setRuleHtml] = useState<string>('');
  const ruleTextRef = useRef('');

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
      });

      setRuleHtml(buildRuleHtml(c.displayNameRule?.textRule || '') || '');
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
    console.log(wrappedVariables, wrappers);
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

  const buildRuleHtml = (rule: string) => {
    const wrappedVariables = properties.map(p => `{${p.name}}`);
    const parts = extractRuleParts(rule, wrappedVariables, wrappers);
    const components = parts.map(p => {
      switch (p.type) {
        case RulePartType.Text:
          return (
            <span>{p.text}</span>
          );
        case RulePartType.Wrapper:
          return (
            <span
              // className={'font-bold'}
              style={{ color: 'var(--bakaui-secondary)' }}
            >{p.text}</span>
          );
        case RulePartType.Property:
          return (
            <span style={{ color: 'var(--bakaui-primary)' }}>{p.text}</span>
          );
      }
    });
    const html = renderToString(<>{components}</>);
    console.log(parts, html);
    return html;
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
          <div className={'flex flex-wrap'}>
            <InfoCircleOutlined className={'text-medium'} />
            &nbsp;
            {t('You can use any combination of text and following properties in rule, and you can add more properties in category configuration.')}
          </div>
          <div className={'flex flex-wrap items-center'}>
            <InfoCircleOutlined className={'text-medium'} />
            &nbsp;
            <Trans
              i18nKey={'category.displayNameRule.propertyExample'}
              values={{
                samplePropertyName: properties[0]?.name ?? t('Name'),
              }}
            >
              To add a property value as a variable in the rule, you can use the following
              format: <Code>{'{name of property}'}</Code>.
              For example, <Code>{'{samplePropertyName}'}</Code> will be replaced with the value of the property
              named <Code>sampleName</Code>
            </Trans>
          </div>
          <div className={'flex flex-wrap'}>
            <InfoCircleOutlined className={'text-medium'} />
            &nbsp;
            {t('Be careful if you have multiple properties with same name, only a random one will be replaced.')}
          </div>
        </div>
        <div className={'flex flex-wrap gap-1 mt-2'}>
          {properties.map(p => (
            <Button
              size={'sm'}
              key={p.id}
              onClick={() => {
                ruleTextRef.current += `{${p.name}}`;
                setRuleHtml(buildRuleHtml(ruleTextRef.current));
              }}
            >
              {p.name}
            </Button>
          ))}
        </div>
      </div>
      <div>
        <div className={'flex flex-wrap'}>
          <InfoCircleOutlined className={'text-medium'} />
          &nbsp;
          {t('You can safely use any of following text wrappers to wrap the properties, and wrappers surrounding the property with empty value will be removed automatically.')}
          {t('You can check and set the wrappers in special text configuration.')}
        </div>
        <div className={'flex flex-wrap gap-1 mt-2'}>
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
      <div className={'flex flex-wrap'}>
        <InfoCircleOutlined className={'text-medium'} />
        &nbsp;
        {t('If you leave the rule with empty value, the file name will be the display name.')}
      </div>
      <div>
        <div>{t('Display name rule')}</div>
        <ContentEditable
          autoFocus
          className={'border-1 rounded mt-1 p-2'}
          html={ruleHtml}
          onChange={v => {
            // console.log('changes', v, v.target.value, v.currentTarget.textContent);
            ruleTextRef.current = v.currentTarget.textContent || '';
            setRuleHtml(buildRuleHtml(ruleTextRef.current));
          }}
          tagName={'pre'}
        />
      </div>
      {ruleTextRef.current.length > 0 && (
        <div>
          <Button
            // variant={'light'}
            // color={'primary'}
            onClick={() => {}}
          >{t('Click to preview')}</Button>
        </div>
      )}
    </Modal>
  );
};

DisplayNameRuleEditorDialog.show = (props: IProps) => createPortalOfComponent(DisplayNameRuleEditorDialog, props);

export default DisplayNameRuleEditorDialog;
