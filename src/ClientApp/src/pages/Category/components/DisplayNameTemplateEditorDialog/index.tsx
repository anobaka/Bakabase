import React, { useCallback, useEffect, useRef, useState } from 'react';
import { Trans, useTranslation } from 'react-i18next';
import { renderToString } from 'react-dom/server';
import ContentEditable from 'react-contenteditable';
import { useUpdate } from 'react-use';
import { InfoCircleOutlined } from '@ant-design/icons';
import { extractResourceDisplayNameTemplate } from './helpers';
import { Button, Chip, Code, Modal, Tooltip } from '@/components/bakaui';
import BApi from '@/sdk/BApi';
import {
  CategoryResourceDisplayNameSegmentType,
  CategoryAdditionalItem,
  SpecialTextType,
} from '@/sdk/constants';
import type { IProperty } from '@/components/Property/models';
import {
  type ResourceDisplayNameTemplateSegment,
  ResourceDisplayNameTemplateSegmentType,
} from '@/core/models/Category/ResourceDisplayNameTemplate';
import type { Wrapper } from '@/core/models/Text/Wrapper';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import type { DestroyableProps } from '@/components/bakaui/types';


interface IProps extends DestroyableProps {
  categoryId: number;
  onSaved?: () => any;
}

interface ICategory {
  id: number;
  name: string;
}

const renderDisplayNameSegment = (p: ResourceDisplayNameTemplateSegment) => {
  switch (p.type) {
    case ResourceDisplayNameTemplateSegmentType.Text:
      return (
        <span>{p.text}</span>
      );
    case ResourceDisplayNameTemplateSegmentType.Wrapper:
      return (
        <span
          // className={'font-bold'}
          style={{ color: 'var(--bakaui-secondary)' }}
        >{p.text}</span>
      );
    case ResourceDisplayNameTemplateSegmentType.Property:
      return (
        <span style={{ color: 'var(--bakaui-primary)' }}>{p.text}</span>
      );
  }
};

export default ({
                  categoryId,
                  onSaved,
                  ...props
                }: IProps) => {
  const { t } = useTranslation();
  const forceUpdate = useUpdate();
  const { createPortal } = useBakabaseContext();
  const [visible, setVisible] = useState(true);

  const [wrappers, setWrappers] = useState<Wrapper[]>([]);
  const [category, setCategory] = useState<ICategory>();

  const propertiesRef = useRef<IProperty[]>([]);
  const [templateHtml, setTemplateHtml] = useState<string>('');
  const templateRef = useRef('');

  const init = useCallback(async () => {
    const tr = await BApi.specialText.getAllSpecialTexts();
    const texts = tr.data?.[SpecialTextType.Wrapper] || [];
    const wrappers = texts.map(text => {
      return {
        left: text.value1!,
        right: text.value2!,
      };
    });
    setWrappers(wrappers);

    const cr = await BApi.category.getCategory(categoryId, { additionalItems: CategoryAdditionalItem.CustomProperties });
    const c = cr.data || {};
    setCategory({
      id: c.id!,
      name: c.name!,
    });

    const arr: IProperty[] = [];
    const cps = c.customProperties as IProperty[] || [];
    arr.push(...cps);
    propertiesRef.current = arr;

    templateRef.current = c.resourceDisplayNameTemplate ?? '';
    setTemplateHtml(c.resourceDisplayNameTemplate ? buildTemplateHtml(c.resourceDisplayNameTemplate) : '');
  }, []);

  useEffect(() => {
    init();
  }, []);

  const close = () => {
    setVisible(false);
  };

  const buildTemplateHtml = (template: string) => {
    const variables = propertiesRef.current.map(p => p.name!);
    const parts = extractResourceDisplayNameTemplate(template, variables, wrappers);
    const components = parts.map(p => renderDisplayNameSegment(p));
    const html = renderToString(<>{components}</>);
    console.log(parts, html);
    return html;
  };

  return (
    <Modal
      defaultVisible
      onDestroyed={props.onDestroyed}
      title={t('Edit display name template for category {{name}}', { name: category?.name })}
      onClose={close}
      size={'xl'}
      onOk={async () => {
        await BApi.category.patchCategory(categoryId, {
          resourceDisplayNameTemplate: templateRef.current,
        });
        onSaved?.();
      }}
    >
      <div>
        <div>
          <div className={'flex flex-wrap'}>
            <InfoCircleOutlined className={'text-medium'} />
            &nbsp;
            {t('You can use any combination of text and following properties in template, and you can add more properties in category configuration.')}
          </div>
          <div className={'flex flex-wrap items-center'}>
            <InfoCircleOutlined className={'text-medium'} />
            &nbsp;
            <Trans
              i18nKey={'category.displayNameTemplate.propertyExample'}
              values={{
                samplePropertyName: propertiesRef.current[0]?.name ?? t('Name'),
              }}
            >
              To add a property value as a variable in the template, you can use the following
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
          {propertiesRef.current.map(p => (
            <Button
              size={'sm'}
              key={p.id}
              onClick={() => {
                templateRef.current += `{${p.name}}`;
                setTemplateHtml(buildTemplateHtml(templateRef.current));
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
        {t('If you leave the template with empty value, the file name will be the display name.')}
      </div>
      <div>
        <div>{t('Display name template')}</div>
        <ContentEditable
          key={'0'}
          autoFocus
          className={'border-1 rounded mt-1 p-2'}
          html={templateHtml}
          onChange={v => {
            // console.log('changes', v, v.target.value, v.currentTarget.textContent);
            templateRef.current = v.currentTarget.textContent || '';
            setTemplateHtml(buildTemplateHtml(templateRef.current));
          }}
          tagName={'pre'}
        />
      </div>
      {templateRef.current.length > 0 && (
        <div>
          <Button
            onClick={() => {
              BApi.category.previewCategoryDisplayNameTemplate(categoryId, { template: templateRef.current })
                .then(r => {
                  const data = r.data || [];
                  createPortal(Modal, {
                    defaultVisible: true,
                    size: 'xl',
                    footer: {
                      actions: ['ok'],
                    },
                    title: t('Preview display names of resources'),
                    children: (
                      <div>
                        {data.map(d => {
                          return (
                            <div className={'p-2'}>
                              <div className={'text-xs opacity-60 mb-1'}>
                                {d.resourcePath}
                              </div>
                              <div className={'text-base flex items-center'}>
                                {d.segments?.map(s => {
                                  const type: CategoryResourceDisplayNameSegmentType = s.type as CategoryResourceDisplayNameSegmentType;
                                  let feType: ResourceDisplayNameTemplateSegmentType;
                                  switch (type) {
                                    case CategoryResourceDisplayNameSegmentType.StaticText:
                                      feType = ResourceDisplayNameTemplateSegmentType.Text;
                                      break;
                                    case CategoryResourceDisplayNameSegmentType.Property:
                                      feType = ResourceDisplayNameTemplateSegmentType.Property;
                                      break;
                                    case CategoryResourceDisplayNameSegmentType.LeftWrapper:
                                    case CategoryResourceDisplayNameSegmentType.RightWrapper:
                                      feType = ResourceDisplayNameTemplateSegmentType.Wrapper;
                                      break;
                                  }
                                  const p = {
                                    text: s.text!,
                                    type: feType,
                                  };
                                  return (
                                    <Tooltip
                                      content={t(`CategoryResourceDisplayNameSegmentType.${CategoryResourceDisplayNameSegmentType[type]}`)}
                                    >
                                      <div className={'cursor-pointer hover:font-bold'}>
                                        {renderDisplayNameSegment(p)}
                                      </div>
                                    </Tooltip>
                                  );
                                })}
                              </div>
                            </div>
                          );
                        })}
                      </div>
                    ),
                  });
                });
            }}
          >{t('Click to preview')}</Button>
        </div>
      )}
    </Modal>
  );
};
